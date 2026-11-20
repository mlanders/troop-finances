using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;

namespace BsaTroopFinances.Services;

public class ScoutAccountService(
    IScoutRepository scoutRepository,
    IScoutLedgerRepository ledgerRepository,
    IInvoiceRepository invoiceRepository,
    IInvoicePaymentRepository invoicePaymentRepository,
    IFundraiserEventRepository fundraiserRepository,
    IGuardianScoutLinkRepository linkRepository,
    IGuardianRepository guardianRepository,
    IReimbursementRequestRepository reimbursementRepository,
    IFinanceAuditRepository auditRepository,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IScoutAccountService
{
    // Every method that changes money runs as one database transaction: the ledger entry, the
    // cached balance, the invoice, the payment rows and the audit entry are saved together or not
    // at all. Emails go out only after the transaction commits.

    public async Task ApplyFundraiserAllocationAsync(
        string scoutId, string eventId, decimal amount, string description, string createdBy, decimal? hoursWorked = null)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Allocation amount must be positive.");
            }
            if (hoursWorked is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hoursWorked), "Hours worked must be positive when provided.");
            }

            var fundraiser = await fundraiserRepository.GetByIdAsync(eventId)
                ?? throw new InvalidOperationException("Fundraiser not found.");
            var allocated = (await ledgerRepository.GetForSourceAsync(LedgerSourceType.FundraiserAllocation, eventId))
                .Sum(e => e.Amount);
            if (!fundraiser.TotalRaised.HasValue || allocated + amount > fundraiser.NetProceeds)
            {
                throw new InvalidOperationException("This allocation would exceed the fundraiser proceeds available.");
            }

            var entry = new ScoutLedgerEntryEntity
            {
                PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId),
                RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                EntryTypeValue = LedgerEntryType.Credit,
                SourceTypeValue = LedgerSourceType.FundraiserAllocation,
                SourceId = eventId,
                Amount = amount,
                HoursWorked = hoursWorked.HasValue ? hoursWorked.Value : null,
                Description = description,
                CreatedUtc = timeProvider.GetUtcNow(),
                CreatedBy = createdBy,
            };

            await ledgerRepository.AppendEntryAsync(entry);
            var scout = await scoutRepository.GetByIdAsync(scoutId);
            await auditRepository.AddAsync(FinanceAuditEntityType.Fundraiser, eventId, "Proceeds Allocated", createdBy,
                description, $"{amount:C} allocated to {scout?.FullName ?? scoutId}" +
                (hoursWorked.HasValue ? $" for {hoursWorked.Value:0.##} hours." : "."));
        });
    }

    public async Task<InvoiceEntity> IssueInvoiceAsync(
        string scoutId, decimal totalAmount, decimal amountToApplyFromAccount, DateTimeOffset dueDate,
        string description, string issuedBy)
    {
        var issued = await unitOfWork.InTransactionAsync(async () =>
        {
            if (totalAmount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalAmount), "Invoice amount must be positive.");
            }

            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException($"Scout '{scoutId}' not found.");

            // Normal invoice issuance can never push a balance negative -- clamp to what's actually
            // available (and never more than the invoice itself).
            var amountApplied = Math.Clamp(amountToApplyFromAccount, 0, Math.Min(totalAmount, scout.CachedBalance));
            var amountDue = totalAmount - amountApplied;

            var invoiceRowKey = InvoiceEntity.NewRowKey();
            var invoice = new InvoiceEntity
            {
                PartitionKey = InvoiceEntity.BuildPartitionKey(scoutId),
                RowKey = invoiceRowKey,
                InvoiceId = invoiceRowKey,
                ScoutId = scoutId,
                ScoutName = scout.FullName,
                Description = description,
                TotalAmount = totalAmount,
                AmountAppliedFromAccount = amountApplied,
                AmountDueFromFamily = amountDue,
                StatusValue = amountDue == 0 ? InvoiceStatus.Paid : InvoiceStatus.Outstanding,
                IssuedUtc = timeProvider.GetUtcNow(),
                DueDate = dueDate,
                IssuedBy = issuedBy,
            };

            if (amountDue == 0)
            {
                invoice.PaidUtc = invoice.IssuedUtc;
                invoice.PaidBy = issuedBy;
            }

            if (amountApplied > 0)
            {
                var ledgerEntry = new ScoutLedgerEntryEntity
                {
                    PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId),
                    RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                    EntryTypeValue = LedgerEntryType.Debit,
                    SourceTypeValue = LedgerSourceType.InvoiceApplied,
                    SourceId = invoice.InvoiceId,
                    Amount = amountApplied,
                    Description = $"Applied to invoice: {description}",
                    CreatedUtc = invoice.IssuedUtc,
                    CreatedBy = issuedBy,
                };

                await ledgerRepository.AppendEntryAsync(ledgerEntry);
                invoice.LedgerEntryRowKey = ledgerEntry.RowKey;
            }

            await invoiceRepository.AddAsync(invoice);
            await auditRepository.AddAsync(FinanceAuditEntityType.Invoice, invoice.InvoiceId, "Issued", issuedBy,
                details: $"{totalAmount:C} issued to {scout.FullName}; {amountApplied:C} applied from scout fund and {amountDue:C} due. Due date: {dueDate.UtcDateTime:MMM d, yyyy}.");
            return invoice;
        });

        await notificationService.InvoiceIssuedAsync(issued);
        return issued;
    }

    public async Task<InvoiceEntity> IssueGuardianInvoiceAsync(
        string guardianId, decimal totalAmount, DateTimeOffset dueDate, string description, string issuedBy)
    {
        var issued = await unitOfWork.InTransactionAsync(async () =>
        {
            if (totalAmount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalAmount), "Invoice amount must be positive.");
            }

            var guardian = await guardianRepository.GetByIdAsync(guardianId)
                ?? throw new InvalidOperationException($"Guardian '{guardianId}' not found.");

            var invoiceRowKey = InvoiceEntity.NewRowKey();
            var invoice = new InvoiceEntity
            {
                PartitionKey = InvoiceEntity.BuildPartitionKey(guardian.GuardianId),
                RowKey = invoiceRowKey,
                InvoiceId = invoiceRowKey,
                GuardianId = guardian.GuardianId,
                GuardianName = guardian.FullName,
                Description = description,
                TotalAmount = totalAmount,
                AmountAppliedFromAccount = 0,
                AmountDueFromFamily = totalAmount,
                StatusValue = InvoiceStatus.Outstanding,
                IssuedUtc = timeProvider.GetUtcNow(),
                DueDate = dueDate,
                IssuedBy = issuedBy,
            };

            await invoiceRepository.AddAsync(invoice);
            await auditRepository.AddAsync(FinanceAuditEntityType.Invoice, invoice.InvoiceId, "Issued", issuedBy,
                details: $"{totalAmount:C} issued to {guardian.FullName} (adult). Due date: {dueDate.UtcDateTime:MMM d, yyyy}.");
            return invoice;
        });

        await notificationService.InvoiceIssuedAsync(issued);
        return issued;
    }

    public async Task MarkInvoicePaidAsync(string ownerId, string invoiceId, string paidBy)
    {
        var invoice = await invoiceRepository.GetByIdAsync(ownerId, invoiceId)
            ?? throw new InvalidOperationException($"Invoice '{invoiceId}' not found.");
        await RecordInvoicePaymentAsync(ownerId, invoiceId, invoice.AmountDueFromFamily, InvoicePaymentMethod.Check, "Manual mark paid", paidBy);
    }

    public async Task RecordInvoicePaymentAsync(
        string ownerId, string invoiceId, decimal amount, string method, string? reference, string paidBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
            }
            if (!InvoicePaymentMethod.IsValid(method))
            {
                throw new ArgumentException("Select a supported payment method.", nameof(method));
            }
            if (InvoicePaymentMethod.RequiresReference(method) && string.IsNullOrWhiteSpace(reference))
            {
                throw new ArgumentException("Describe how an \"Other\" payment was made.", nameof(reference));
            }

            var invoice = await invoiceRepository.GetByIdAsync(ownerId, invoiceId)
                ?? throw new InvalidOperationException($"Invoice '{invoiceId}' not found.");
            if (invoice.StatusValue != InvoiceStatus.Outstanding || amount > invoice.AmountDueFromFamily)
            {
                throw new InvalidOperationException("Payment must not exceed the outstanding invoice amount.");
            }

            if (method == InvoicePaymentMethod.ScoutFunds)
            {
                if (invoice.IsForGuardian)
                {
                    throw new InvalidOperationException("Scout funds can't pay an invoice billed to an adult.");
                }

                var scout = await scoutRepository.GetByIdAsync(invoice.ScoutId)
                    ?? throw new InvalidOperationException($"Scout '{invoice.ScoutId}' not found.");
                if (amount > scout.CachedBalance)
                {
                    throw new InvalidOperationException("Payment exceeds available scout funds.");
                }
            }

            await ApplyPaymentAsync(invoice, amount, method, reference, paidBy);
        });
    }

    public async Task<string> RecordFamilyPaymentAsync(IReadOnlyList<FamilyPaymentLine> lines, string method, string? reference, string paidBy)
    {
        return await unitOfWork.InTransactionAsync(async () =>
        {
            if (lines.Count == 0)
            {
                throw new ArgumentException("Select at least one invoice to pay.", nameof(lines));
            }
            if (lines.Any(l => l.Amount < 0 || l.FromScoutAccount < 0 || l.Amount + l.FromScoutAccount <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Every invoice needs an amount to pay.");
            }

            // The family's own payment (if any of it isn't covered by scout accounts).
            var familyPays = lines.Any(l => l.Amount > 0);
            if (familyPays && !InvoicePaymentMethod.External.Contains(method))
            {
                throw new ArgumentException("A family payment must be by check, cash, Venmo or other.", nameof(method));
            }
            if (familyPays && InvoicePaymentMethod.RequiresReference(method) && string.IsNullOrWhiteSpace(reference))
            {
                throw new ArgumentException("Describe how an \"Other\" payment was made.", nameof(reference));
            }
            if (lines.Select(l => l.InvoiceId).Distinct().Count() != lines.Count)
            {
                throw new ArgumentException("The same invoice is listed more than once.", nameof(lines));
            }

            // Check every line first so a bad one gives a clear message; the transaction then makes sure
            // a failure partway through records nothing.
            var invoices = new List<(InvoiceEntity Invoice, decimal Amount, decimal FromScoutAccount)>();
            foreach (var line in lines)
            {
                var invoice = await invoiceRepository.GetByIdAsync(line.OwnerId, line.InvoiceId)
                    ?? throw new InvalidOperationException($"Invoice '{line.InvoiceId}' not found.");
                if (invoice.StatusValue != InvoiceStatus.Outstanding)
                {
                    throw new InvalidOperationException($"{invoice.BilledTo}'s invoice \"{invoice.Description}\" is {invoice.StatusValue.ToLowerInvariant()}, not outstanding.");
                }
                if (line.Amount + line.FromScoutAccount > invoice.AmountDueFromFamily)
                {
                    throw new InvalidOperationException(
                        $"{line.Amount + line.FromScoutAccount:C} is more than the {invoice.AmountDueFromFamily:C} still due on {invoice.BilledTo}'s invoice \"{invoice.Description}\".");
                }
                if (line.FromScoutAccount > 0 && invoice.IsForGuardian)
                {
                    throw new InvalidOperationException($"Scout funds can't pay {invoice.BilledTo}'s own invoice \"{invoice.Description}\".");
                }
                invoices.Add((invoice, line.Amount, line.FromScoutAccount));
            }

            // Each scout's funds only pay their own invoices, and never take them below zero --
            // counting every line of theirs together.
            foreach (var scoutLines in invoices.Where(i => i.FromScoutAccount > 0).GroupBy(i => i.Invoice.ScoutId))
            {
                var scout = await scoutRepository.GetByIdAsync(scoutLines.Key)
                    ?? throw new InvalidOperationException($"Scout '{scoutLines.Key}' not found.");
                var wanted = scoutLines.Sum(i => i.FromScoutAccount);
                if (wanted > scout.CachedBalance)
                {
                    throw new InvalidOperationException(
                        $"{wanted:C} from {scout.FullName}'s scout account is more than the {Math.Max(0, scout.CachedBalance):C} in it.");
                }
            }

            var groupId = Guid.NewGuid().ToString("N");
            var groupTotal = invoices.Sum(i => i.Amount);
            var familyCount = invoices.Count(i => i.Amount > 0);
            foreach (var (invoice, amount, fromScoutAccount) in invoices)
            {
                // Scout funds first, as their own payment: not part of the family's check, so a bank
                // deposit (which skips scout-funds payments) never counts them.
                if (fromScoutAccount > 0)
                {
                    await ApplyPaymentAsync(invoice, fromScoutAccount, InvoicePaymentMethod.ScoutFunds, null, paidBy,
                        groupNote: "Family payment, from the scout's account:");
                }

                if (amount > 0)
                {
                    var note = $"Family payment: {amount:C} of {groupTotal:C} across {familyCount} invoice{(familyCount == 1 ? "" : "s")}.";
                    await ApplyPaymentAsync(invoice, amount, method, reference, paidBy, groupId, groupTotal, note);
                }
            }

            return groupId;
        });
    }

    /// <summary>Records one payment against one invoice: the shared write path for single
    /// payments and each line of a family payment.</summary>
    private async Task ApplyPaymentAsync(InvoiceEntity invoice, decimal amount, string method, string? reference, string paidBy,
        string groupId = "", decimal groupTotal = 0, string? groupNote = null)
    {
        var now = timeProvider.GetUtcNow();

        if (method == InvoicePaymentMethod.ScoutFunds)
        {
            invoice.AmountAppliedFromAccount += amount;
        }

        await invoicePaymentRepository.AddAsync(new InvoicePaymentEntity
        {
            PartitionKey = InvoicePaymentEntity.BuildPartitionKey(invoice.InvoiceId),
            RowKey = InvoicePaymentEntity.NewRowKey(now),
            InvoiceId = invoice.InvoiceId,
            ScoutId = invoice.ScoutId,
            Amount = amount,
            Method = method,
            Reference = reference?.Trim() ?? string.Empty,
            RecordedBy = paidBy,
            PaidUtc = now,
            PaymentGroupId = groupId,
            GroupTotal = groupTotal,
        });

        invoice.AmountDueFromFamily -= amount;
        if (invoice.AmountDueFromFamily <= 0)
        {
            invoice.AmountDueFromFamily = 0;
            invoice.StatusValue = InvoiceStatus.Paid;
            invoice.PaidUtc = now;
            invoice.PaidBy = paidBy;
        }
        await invoiceRepository.UpdateAsync(invoice);

        if (method == InvoicePaymentMethod.ScoutFunds)
        {
            await ledgerRepository.AppendEntryAsync(new ScoutLedgerEntryEntity
            {
                PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(invoice.ScoutId),
                RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                EntryTypeValue = LedgerEntryType.Debit,
                SourceTypeValue = LedgerSourceType.InvoicePayment,
                SourceId = invoice.InvoiceId,
                Amount = amount,
                Description = $"Payment from scout funds for invoice: {invoice.Description}",
                CreatedUtc = now,
                CreatedBy = paidBy,
            });
        }

        await auditRepository.AddAsync(FinanceAuditEntityType.Invoice, invoice.InvoiceId, "Payment Recorded", paidBy,
            reference, $"{(groupNote is null ? "" : groupNote + " ")}{amount:C} received via {method}; remaining due {invoice.AmountDueFromFamily:C}.");
    }

    public async Task VoidInvoiceAsync(string ownerId, string invoiceId, string reason, string voidedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var invoice = await invoiceRepository.GetByIdAsync(ownerId, invoiceId)
                ?? throw new InvalidOperationException($"Invoice '{invoiceId}' not found.");

            // Never the case for an invoice billed to an adult: scout funds aren't applied to those.
            var amountToReverse = invoice.IsForGuardian ? 0 : invoice.AmountAppliedFromAccount;
            if (amountToReverse > 0)
            {
                var reversal = new ScoutLedgerEntryEntity
                {
                    PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(invoice.ScoutId),
                    RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                    EntryTypeValue = LedgerEntryType.Credit,
                    SourceTypeValue = LedgerSourceType.InvoiceVoidReversal,
                    SourceId = invoice.InvoiceId,
                    Amount = amountToReverse,
                    Description = $"Reversal for voided invoice: {invoice.Description}",
                    CreatedUtc = timeProvider.GetUtcNow(),
                    CreatedBy = voidedBy,
                };

                await ledgerRepository.AppendEntryAsync(reversal);
            }

            invoice.StatusValue = InvoiceStatus.Voided;
            invoice.VoidedUtc = timeProvider.GetUtcNow();
            invoice.VoidedBy = voidedBy;
            invoice.VoidReason = reason;

            await invoiceRepository.UpdateAsync(invoice);
            await auditRepository.AddAsync(FinanceAuditEntityType.Invoice, invoiceId, "Voided", voidedBy, reason);
        });
    }

    public async Task ApplyManualAdjustmentAsync(string scoutId, decimal amount, string reason, string createdBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            if (amount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Adjustment amount can't be zero.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A reason is required for a manual adjustment.", nameof(reason));
            }

            var entry = new ScoutLedgerEntryEntity
            {
                PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId),
                RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                EntryTypeValue = amount > 0 ? LedgerEntryType.Credit : LedgerEntryType.Debit,
                SourceTypeValue = LedgerSourceType.ManualAdjustment,
                SourceId = string.Empty,
                Amount = Math.Abs(amount),
                Description = reason,
                CreatedUtc = timeProvider.GetUtcNow(),
                CreatedBy = createdBy,
            };

            // Deliberately bypasses the clamping IssueInvoiceAsync/ApplyFundraiserAllocationAsync
            // apply -- this is the one intentional way a balance can go negative.
            await ledgerRepository.AppendEntryAsync(entry);

            var scout = await scoutRepository.GetByIdAsync(scoutId);
            await auditRepository.AddAsync(FinanceAuditEntityType.Scout, scoutId, "Manual Adjustment", createdBy, reason,
                $"{(amount > 0 ? "Credited" : "Debited")} {Math.Abs(amount):C} {(amount > 0 ? "to" : "from")} {scout?.FullName ?? scoutId}.");
        });
    }

    public async Task RenameScoutAsync(string scoutId, string newFullName, string changedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            newFullName = newFullName.Trim();
            if (string.IsNullOrWhiteSpace(newFullName))
            {
                throw new ArgumentException("A name is required.", nameof(newFullName));
            }

            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException($"Scout '{scoutId}' not found.");
            var oldFullName = scout.FullName;
            if (oldFullName == newFullName)
            {
                return;
            }

            scout.FullName = newFullName;
            await scoutRepository.UpdateAsync(scout);

            // Amount is 0, so this records the change in the scout's history without touching the balance.
            await ledgerRepository.AppendEntryAsync(new ScoutLedgerEntryEntity
            {
                PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId),
                RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                EntryTypeValue = LedgerEntryType.Credit,
                SourceTypeValue = LedgerSourceType.NameChange,
                SourceId = string.Empty,
                Amount = 0,
                Description = $"Name changed: {oldFullName} → {newFullName}",
                CreatedUtc = timeProvider.GetUtcNow(),
                CreatedBy = changedBy,
            });

            // The name is copied onto these records when they're created; bring the copies up to date.
            foreach (var link in await linkRepository.GetGuardiansForScoutAsync(scoutId))
            {
                link.ScoutName = newFullName;
                await linkRepository.AddLinkAsync(link);
            }

            foreach (var invoice in await invoiceRepository.GetByScoutAsync(scoutId))
            {
                invoice.ScoutName = newFullName;
                await invoiceRepository.UpdateAsync(invoice);
            }

            foreach (var request in await reimbursementRepository.GetByScoutAsync(scoutId))
            {
                request.ScoutName = newFullName;
                await reimbursementRepository.UpdateAsync(request);
            }
        });
    }

    public async Task TransferBalanceAsync(string scoutId, BalanceDestination to, decimal amount, string reason, string transferredBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException("That scout isn't on the list any more.");
            await MoveBalanceAsync(scout, to, amount, reason, transferredBy);
        });
    }

    public async Task DeactivateAsync(string scoutId, BalanceDestination? moveBalanceTo, string? reason, string deactivatedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException("That scout isn't on the list any more.");
            if (!scout.IsActive)
            {
                return;
            }

            var balance = scout.CachedBalance;
            var moved = moveBalanceTo is not null && balance > 0;
            if (moved)
            {
                await MoveBalanceAsync(scout, moveBalanceTo!, balance, reason ?? string.Empty, deactivatedBy);
                // The ledger append updated the stored balance; work from the fresh row.
                scout = await scoutRepository.GetByIdAsync(scoutId) ?? scout;
            }

            scout.IsActive = false;
            await scoutRepository.UpdateAsync(scout);
            await auditRepository.AddAsync(FinanceAuditEntityType.Scout, scoutId, "Deactivated", deactivatedBy,
                details: moved
                    ? $"Deactivated {scout.FullName} after moving their {balance:C} balance {DescribeDestination(moveBalanceTo!, await DestinationNameAsync(moveBalanceTo!))}."
                    : balance != 0
                        ? $"Deactivated {scout.FullName}; their {balance:C} balance stays on the account."
                        : $"Deactivated {scout.FullName}.");
        });
    }

    public async Task ReactivateAsync(string scoutId, string reactivatedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException("That scout isn't on the list any more.");
            if (scout.IsActive)
            {
                return;
            }

            scout.IsActive = true;
            await scoutRepository.UpdateAsync(scout);
            await auditRepository.AddAsync(FinanceAuditEntityType.Scout, scoutId, "Reactivated", reactivatedBy,
                details: $"Reactivated {scout.FullName}.");
        });
    }

    /// <summary>The ledger entries and audit record for a transfer; runs inside the caller's transaction.</summary>
    private async Task MoveBalanceAsync(ScoutEntity scout, BalanceDestination to, decimal amount, string reason, string by)
    {
        reason = reason.Trim();
        if (amount <= 0)
        {
            throw new InvalidOperationException("Enter an amount to move.");
        }

        if (amount > scout.CachedBalance)
        {
            throw new InvalidOperationException($"{scout.FullName} only has {Math.Max(0, scout.CachedBalance):C} to move.");
        }

        if (reason.Length == 0)
        {
            throw new InvalidOperationException("Say why the money is being moved.");
        }

        ScoutEntity? toScout = null;
        if (!to.IsGeneralFund)
        {
            if (to.ScoutId == scout.ScoutId)
            {
                throw new InvalidOperationException("Pick a different scout to move the money to.");
            }

            toScout = await scoutRepository.GetByIdAsync(to.ScoutId!)
                ?? throw new InvalidOperationException("The scout to move the money to isn't on the list any more.");
        }

        var transferId = Guid.NewGuid().ToString("N");
        var now = timeProvider.GetUtcNow();
        await ledgerRepository.AppendEntryAsync(new ScoutLedgerEntryEntity
        {
            PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scout.ScoutId),
            RowKey = ScoutLedgerEntryEntity.NewRowKey(),
            EntryTypeValue = LedgerEntryType.Debit,
            SourceTypeValue = LedgerSourceType.Transfer,
            SourceId = transferId,
            Amount = amount,
            Description = $"{(toScout is null ? "To the troop general fund" : $"To {toScout.FullName}")}: {reason}",
            CreatedUtc = now,
            CreatedBy = by,
        });

        if (toScout is not null)
        {
            await ledgerRepository.AppendEntryAsync(new ScoutLedgerEntryEntity
            {
                PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(toScout.ScoutId),
                RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                EntryTypeValue = LedgerEntryType.Credit,
                SourceTypeValue = LedgerSourceType.Transfer,
                SourceId = transferId,
                Amount = amount,
                Description = $"From {scout.FullName}: {reason}",
                CreatedUtc = now,
                CreatedBy = by,
            });
        }

        await auditRepository.AddAsync(FinanceAuditEntityType.Scout, scout.ScoutId, "Funds Transferred", by,
            details: $"Moved {amount:C} from {scout.FullName} {DescribeDestination(to, toScout?.FullName)}: {reason}");
    }

    private async Task<string?> DestinationNameAsync(BalanceDestination to) =>
        to.IsGeneralFund ? null : (await scoutRepository.GetByIdAsync(to.ScoutId!))?.FullName;

    private static string DescribeDestination(BalanceDestination to, string? scoutName) =>
        to.IsGeneralFund ? "to the troop general fund" : $"to {scoutName ?? "another scout"}";

    public async Task<ScoutStatementModel?> GetScoutStatementAsync(string scoutId)
    {
        var scout = await scoutRepository.GetByIdAsync(scoutId);
        if (scout is null)
        {
            return null;
        }

        var ledgerEntries = await ledgerRepository.GetForScoutAsync(scoutId);
        var invoices = await invoiceRepository.GetByScoutAsync(scoutId);
        return new ScoutStatementModel(scout, ledgerEntries, invoices);
    }

    public async Task DeleteScoutAsync(string scoutId, string deletedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var scout = await scoutRepository.GetByIdAsync(scoutId)
                ?? throw new InvalidOperationException($"Scout '{scoutId}' not found.");

            var invoices = await invoiceRepository.GetByScoutAsync(scoutId);
            var ledgerEntries = await ledgerRepository.GetForScoutAsync(scoutId);
            if (invoices.Count > 0 || ledgerEntries.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{scout.FullName}' has invoice or ledger activity and can't be deleted.");
            }

            var guardianLinks = await linkRepository.GetGuardiansForScoutAsync(scoutId);
            foreach (var link in guardianLinks)
            {
                await linkRepository.RemoveLinkAsync(link.GuardianId, scoutId);
            }

            await auditRepository.AddAsync(FinanceAuditEntityType.Scout, scoutId, "Deleted", deletedBy,
                details: $"Deleted scout {scout.FullName}; no invoice or ledger activity on record.");
            await scoutRepository.DeleteAsync(scout);
        });
    }
}
