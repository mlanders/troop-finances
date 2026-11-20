using System.Globalization;
using System.Text.Json;
using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>An account with its balances: everything in the register, and what the bank has
/// confirmed (reconciled) so far.</summary>
public record BankAccountSummary(TroopAccountEntity Account, decimal Balance, decimal ClearedBalance, int UnclearedCount, DateTimeOffset? LastReconciled);

/// <summary>Money received but not yet deposited: an invoice payment (or a family's one payment
/// across several invoices) or a money donation.</summary>
public record UndepositedItem(string Kind, string SourceId, DateTimeOffset Date, string Payer, string For, string Method, string Reference, decimal Amount);

/// <summary>Income on a deposit that the app has no other record of, e.g. a car wash's cash.</summary>
public record OtherIncomeLine(string Description, decimal Amount, string Method, string Payer = "", string Reference = "", string? FundraiserId = null);

public record DepositRequest(
    string AccountId, DateOnly Date, IReadOnlyList<(string Kind, string SourceId)> Items, IReadOnlyList<OtherIncomeLine> Other,
    IReadOnlyDictionary<decimal, int> CashCount, string CountedBy1, string CountedBy2);

/// <summary>A reconciliation with the entries it can tick and where the sums stand.</summary>
public record ReconciliationView(
    ReconciliationEntity Reconciliation, IReadOnlyList<TroopTransactionEntity> Candidates, decimal PreviouslyCleared, decimal TickedTotal)
{
    public decimal ClearedBalance => PreviouslyCleared + TickedTotal;
    public decimal Difference => Reconciliation.StatementBalance - ClearedBalance;
}

public interface ITroopAccountService
{
    Task<IReadOnlyList<BankAccountSummary>> GetSummariesAsync();
    Task<TroopAccountEntity> AddAccountAsync(string name, string kind, decimal openingBalance, DateOnly openingDate, string addedBy);
    Task UpdateAccountAsync(TroopAccountEntity account, string changedBy);
    Task DeleteAccountAsync(string accountId, string deletedBy);

    /// <summary>Records an entry typed into the register (an expense, bank fee, interest, other
    /// income). <paramref name="amount"/> is positive; the kind decides whether it's in or out.</summary>
    Task<TroopTransactionEntity> RecordEntryAsync(string accountId, string kind, DateOnly date, decimal amount, string payee,
        string description, string reference, ReceiptUpload? receipt, string recordedBy);

    /// <summary>Changes an entry. A typed-in entry can change anything but its account; a
    /// reimbursement payout only its date and check number. Deposits and transfers are changed by
    /// deleting and making them again.</summary>
    Task UpdateEntryAsync(string accountId, string rowKey, DateOnly date, decimal amount, string payee, string description,
        string reference, ReceiptUpload? receipt, string changedBy);

    Task DeleteEntryAsync(string accountId, string rowKey, string deletedBy);

    Task TransferAsync(string fromAccountId, string toAccountId, DateOnly date, decimal amount, string description, string recordedBy);

    Task<IReadOnlyList<UndepositedItem>> GetUndepositedAsync();
    Task<DepositEntity> CreateDepositAsync(DepositRequest request, string recordedBy);
    Task DeleteDepositAsync(string depositId, string deletedBy);

    /// <summary>Marks an approved reimbursement paid and takes the money out of the account, together.</summary>
    Task PayReimbursementAsync(string guardianId, string requestId, string accountId, DateOnly paidOn, string checkNumber, string paidBy);

    Task<ReconciliationEntity> StartReconciliationAsync(string accountId, DateOnly statementDate, decimal statementBalance, string startedBy);
    Task<ReconciliationView> GetReconciliationAsync(string accountId, string reconciliationId);
    Task SetClearedAsync(string accountId, string reconciliationId, IReadOnlySet<string> clearedRowKeys);
    Task FinishReconciliationAsync(string accountId, string reconciliationId, string finishedBy);
    Task CancelReconciliationAsync(string accountId, string reconciliationId, string cancelledBy);

    /// <summary>Opens the account's latest finished reconciliation again, unlocking its entries.</summary>
    Task ReopenLastReconciliationAsync(string accountId, string reopenedBy);
}

/// <summary>An uploaded receipt for a register entry.</summary>
public record ReceiptUpload(Stream Content, string ContentType, string FileName, long SizeBytes);

/// <summary>
/// The troop's own money: accounts, their registers, deposits, reimbursement payouts and
/// reconciliations. Every change saves its records and an audit entry in one transaction.
/// Anything the treasurer did wrong throws InvalidOperationException with a message for them.
/// </summary>
public class TroopAccountService(
    ITroopBankRepository bankRepository,
    IInvoiceRepository invoiceRepository,
    IInvoicePaymentRepository paymentRepository,
    IDonationRepository donationRepository,
    IReimbursementRequestRepository reimbursementRepository,
    IReimbursementService reimbursementService,
    IReceiptStorageService receiptStorage,
    IFinanceAuditRepository auditRepository,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ITroopAccountService
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private static readonly HashSet<string> ReceiptTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "application/pdf" };
    private const long MaxReceiptBytes = 10 * 1024 * 1024;

    /// <summary>Folder in the receipts container for register entries' receipts.</summary>
    public const string ReceiptFolder = "expenses";

    public async Task<IReadOnlyList<BankAccountSummary>> GetSummariesAsync()
    {
        var accounts = await bankRepository.GetAccountsAsync();
        var transactions = (await bankRepository.GetAllTransactionsAsync()).ToLookup(t => t.AccountId);
        var summaries = new List<BankAccountSummary>();
        foreach (var account in accounts)
        {
            var entries = transactions[account.AccountId].ToList();
            var lastReconciled = (await bankRepository.GetReconciliationsAsync(account.AccountId))
                .FirstOrDefault(r => r.Status == ReconciliationStatus.Complete)?.StatementDate;
            summaries.Add(new BankAccountSummary(account,
                account.OpeningBalance + entries.Sum(e => e.Amount),
                account.OpeningBalance + entries.Where(e => e.IsCleared).Sum(e => e.Amount),
                entries.Count(e => !e.IsCleared),
                lastReconciled));
        }

        return summaries.OrderByDescending(s => s.Account.IsActive).ThenBy(s => s.Account.Name).ToList();
    }

    public async Task<TroopAccountEntity> AddAccountAsync(string name, string kind, decimal openingBalance, DateOnly openingDate, string addedBy)
    {
        var account = new TroopAccountEntity
        {
            RowKey = TroopAccountEntity.NewRowKey(),
            Name = name.Trim(),
            Kind = kind,
            OpeningBalance = openingBalance,
            OpeningDate = Midnight(openingDate),
            CreatedUtc = timeProvider.GetUtcNow(),
            CreatedBy = addedBy,
        };
        await ValidateAccountAsync(account);
        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.AddAccountAsync(account);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, account.AccountId, "Added", addedBy,
                details: $"Added {account.Kind.ToLowerInvariant()} account {account.Name}, opening balance {Money(openingBalance)} on {Day(account.OpeningDate)}.");
        });
        return account;
    }

    public async Task UpdateAccountAsync(TroopAccountEntity account, string changedBy)
    {
        account.Name = account.Name.Trim();
        await ValidateAccountAsync(account);
        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.UpdateAccountAsync(account);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, account.AccountId, "Edited", changedBy,
                details: $"{account.Name}: opening balance {Money(account.OpeningBalance)} on {Day(account.OpeningDate)}, {(account.IsActive ? "active" : "inactive")}.");
        });
    }

    public async Task DeleteAccountAsync(string accountId, string deletedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var account = await bankRepository.GetAccountAsync(accountId) ?? throw new InvalidOperationException("Account not found.");
            if ((await bankRepository.GetTransactionsAsync(accountId)).Count > 0)
            {
                throw new InvalidOperationException($"{account.Name} has entries, so it can't be deleted. Mark it inactive instead.");
            }

            await bankRepository.DeleteAccountAsync(accountId);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, "Deleted", deletedBy, details: $"Deleted account {account.Name}.");
        });
    }

    public async Task<TroopTransactionEntity> RecordEntryAsync(string accountId, string kind, DateOnly date, decimal amount, string payee,
        string description, string reference, ReceiptUpload? receipt, string recordedBy)
    {
        var moneyIn = TroopTransactionKind.Manual.FirstOrDefault(m => m.Kind == kind) is { Kind: not null } manual
            ? manual.MoneyIn
            : throw new InvalidOperationException("Choose what kind of entry this is.");
        ValidateEntry(amount, payee, description, kind);
        ValidateReceipt(receipt);
        var account = await ActiveAccountAsync(accountId);

        var entry = new TroopTransactionEntity
        {
            PartitionKey = accountId,
            RowKey = TroopTransactionEntity.NewRowKey(),
            Date = Midnight(date),
            Amount = moneyIn ? amount : -amount,
            Kind = kind,
            Payee = payee.Trim(),
            Description = description.Trim(),
            Reference = reference.Trim(),
            CreatedUtc = timeProvider.GetUtcNow(),
            CreatedBy = recordedBy,
        };

        await AttachReceiptAsync(entry, receipt);
        try
        {
            await unitOfWork.InTransactionAsync(async () =>
            {
                await bankRepository.AddTransactionAsync(entry);
                await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, $"Recorded {kind.ToLowerInvariant()}", recordedBy,
                    details: $"{Describe(entry)} in {account.Name}.");
            });
        }
        catch
        {
            await DeleteReceiptAsync(entry.ReceiptBlobPath);
            throw;
        }

        return entry;
    }

    public async Task UpdateEntryAsync(string accountId, string rowKey, DateOnly date, decimal amount, string payee, string description,
        string reference, ReceiptUpload? receipt, string changedBy)
    {
        var entry = await bankRepository.GetTransactionAsync(accountId, rowKey) ?? throw new InvalidOperationException("Entry not found.");
        EnsureUnlocked(entry);
        ValidateReceipt(receipt);
        if (entry.Kind == TroopTransactionKind.ReimbursementPayout)
        {
            // The amount and payee belong to the reimbursement; only how it was paid can change.
            entry.Date = Midnight(date);
            entry.Reference = reference.Trim();
        }
        else if (TroopTransactionKind.IsManual(entry.Kind))
        {
            ValidateEntry(amount, payee, description, entry.Kind);
            entry.Date = Midnight(date);
            entry.Amount = entry.Amount >= 0 ? amount : -amount;
            entry.Payee = payee.Trim();
            entry.Description = description.Trim();
            entry.Reference = reference.Trim();
        }
        else
        {
            throw new InvalidOperationException($"A {entry.Kind.ToLowerInvariant()} can't be edited. Delete it and make it again.");
        }

        var oldReceipt = entry.ReceiptBlobPath;
        if (receipt is not null)
        {
            await AttachReceiptAsync(entry, receipt);
        }

        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.UpdateTransactionAsync(entry);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, $"Edited {entry.Kind.ToLowerInvariant()}", changedBy,
                details: $"Now {Describe(entry)}.");
        });
        if (receipt is not null && oldReceipt != entry.ReceiptBlobPath)
        {
            await DeleteReceiptAsync(oldReceipt);
        }
    }

    public async Task DeleteEntryAsync(string accountId, string rowKey, string deletedBy)
    {
        var entry = await bankRepository.GetTransactionAsync(accountId, rowKey) ?? throw new InvalidOperationException("Entry not found.");
        EnsureUnlocked(entry);
        switch (entry.SourceType)
        {
            case TroopTransactionSource.Deposit:
                await DeleteDepositAsync(entry.SourceId, deletedBy);
                return;
            case TroopTransactionSource.Reimbursement:
                throw new InvalidOperationException("This payment came from a reimbursement, so it stays while the reimbursement is marked paid.");
        }

        // A transfer is two entries: both go together.
        var entries = entry.SourceType == TroopTransactionSource.Transfer
            ? await bankRepository.GetTransactionsForSourceAsync(TroopTransactionSource.Transfer, entry.SourceId)
            : [entry];
        foreach (var other in entries)
        {
            EnsureUnlocked(other);
        }

        await unitOfWork.InTransactionAsync(async () =>
        {
            foreach (var other in entries)
            {
                await bankRepository.DeleteTransactionAsync(other.AccountId, other.RowKey);
                await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, other.AccountId, $"Deleted {other.Kind.ToLowerInvariant()}", deletedBy,
                    details: $"Deleted {Describe(other)}.");
            }
        });
        await DeleteReceiptAsync(entry.ReceiptBlobPath);
    }

    public async Task TransferAsync(string fromAccountId, string toAccountId, DateOnly date, decimal amount, string description, string recordedBy)
    {
        if (fromAccountId == toAccountId)
        {
            throw new InvalidOperationException("Choose two different accounts.");
        }
        if (amount <= 0)
        {
            throw new InvalidOperationException("Enter an amount more than $0.00.");
        }

        var from = await ActiveAccountAsync(fromAccountId);
        var to = await ActiveAccountAsync(toAccountId);
        var transferId = Guid.NewGuid().ToString("N");
        var now = timeProvider.GetUtcNow();
        var text = string.IsNullOrWhiteSpace(description) ? $"Transfer from {from.Name} to {to.Name}" : description.Trim();
        TroopTransactionEntity Half(TroopAccountEntity account, decimal signed, string kind, string other) => new()
        {
            PartitionKey = account.AccountId,
            RowKey = TroopTransactionEntity.NewRowKey(),
            Date = Midnight(date),
            Amount = signed,
            Kind = kind,
            Payee = other,
            Description = text,
            SourceType = TroopTransactionSource.Transfer,
            SourceId = transferId,
            CreatedUtc = now,
            CreatedBy = recordedBy,
        };

        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.AddTransactionAsync(Half(from, -amount, TroopTransactionKind.TransferOut, to.Name));
            await bankRepository.AddTransactionAsync(Half(to, amount, TroopTransactionKind.TransferIn, from.Name));
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, from.AccountId, "Transferred", recordedBy,
                details: $"Moved {Money(amount)} from {from.Name} to {to.Name} on {Day(Midnight(date))}.");
        });
    }

    public async Task<IReadOnlyList<UndepositedItem>> GetUndepositedAsync()
    {
        var accounts = await bankRepository.GetAccountsAsync();
        if (accounts.Count == 0)
        {
            return [];
        }

        // Money received before the troop started keeping its accounts here is already in their
        // opening balances.
        var since = accounts.Min(a => a.OpeningDate);
        var deposited = await bankRepository.GetDepositedSourcesAsync();
        var items = new List<UndepositedItem>();

        var payments = (await paymentRepository.GetPaidBetweenAsync(since, DateTimeOffset.MaxValue))
            .Where(p => p.Method != InvoicePaymentMethod.ScoutFunds);
        var invoices = new Dictionary<string, InvoiceEntity?>();
        foreach (var group in payments.GroupBy(p => string.IsNullOrEmpty(p.PaymentGroupId) ? p.RowKey : p.PaymentGroupId))
        {
            if (deposited.Contains((DepositLineKind.InvoicePayment, group.Key)))
            {
                continue;
            }

            var forInvoices = new List<InvoiceEntity>();
            foreach (var payment in group)
            {
                if (!invoices.TryGetValue(payment.InvoiceId, out var invoice))
                {
                    invoices[payment.InvoiceId] = invoice = await invoiceRepository.GetByInvoiceIdAsync(payment.InvoiceId);
                }
                if (invoice is not null)
                {
                    forInvoices.Add(invoice);
                }
            }

            var first = group.First();
            var names = forInvoices.Select(i => i.BilledTo).Distinct().ToList();
            items.Add(new UndepositedItem(DepositLineKind.InvoicePayment, group.Key, first.PaidUtc,
                names.Count == 0 ? "Family payment" : string.Join(", ", names),
                forInvoices.Count == 1 ? forInvoices[0].Description : $"{group.Count()} invoices",
                first.Method, first.Reference, group.Sum(p => p.Amount)));
        }

        foreach (var donation in await donationRepository.GetAllAsync())
        {
            if (!donation.IsInKind && donation.ReceivedDate >= since && !deposited.Contains((DepositLineKind.Donation, donation.DonationId)))
            {
                items.Add(new UndepositedItem(DepositLineKind.Donation, donation.DonationId, donation.ReceivedDate, donation.DonorName,
                    donation.IsRestricted ? $"Donation for {donation.Purpose}" : "Donation", donation.Method, donation.Reference, donation.Amount));
            }
        }

        return items.OrderBy(i => i.Date).ThenBy(i => i.Payer).ToList();
    }

    public async Task<DepositEntity> CreateDepositAsync(DepositRequest request, string recordedBy)
    {
        var account = await ActiveAccountAsync(request.AccountId);
        var waiting = (await GetUndepositedAsync()).ToDictionary(i => (i.Kind, i.SourceId));
        var depositId = DepositEntity.NewRowKey();
        var lines = new List<DepositLineEntity>();
        foreach (var key in request.Items.Distinct())
        {
            var item = waiting.GetValueOrDefault(key)
                ?? throw new InvalidOperationException("One of the items has already been deposited. Reload the page and try again.");
            lines.Add(Line(item.Kind, item.SourceId, item.Payer, item.Method, item.Reference, item.Amount, item.For, null));
        }
        foreach (var other in request.Other)
        {
            if (string.IsNullOrWhiteSpace(other.Description) || other.Amount <= 0)
            {
                throw new InvalidOperationException("Each other income line needs a description and an amount more than $0.00.");
            }
            lines.Add(Line(DepositLineKind.Other, "", other.Payer.Trim(), other.Method, other.Reference.Trim(), other.Amount, other.Description.Trim(), other.FundraiserId));
        }
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one item to deposit.");
        }

        var cash = lines.Where(l => l.Method == InvoicePaymentMethod.Cash).Sum(l => l.Amount);
        var counted = request.CashCount.Sum(c => c.Key * c.Value);
        if (cash != counted)
        {
            throw new InvalidOperationException($"The cash counted ({Money(counted)}) doesn't match the cash being deposited ({Money(cash)}).");
        }
        if (cash > 0 && (string.IsNullOrWhiteSpace(request.CountedBy1) || string.IsNullOrWhiteSpace(request.CountedBy2)))
        {
            throw new InvalidOperationException("Cash should be counted by two people. Enter both names.");
        }

        var checks = lines.Where(l => l.Method == InvoicePaymentMethod.Check).Sum(l => l.Amount);
        var total = lines.Sum(l => l.Amount);
        var now = timeProvider.GetUtcNow();
        var entry = new TroopTransactionEntity
        {
            PartitionKey = account.AccountId,
            RowKey = TroopTransactionEntity.NewRowKey(),
            Date = Midnight(request.Date),
            Amount = total,
            Kind = TroopTransactionKind.Deposit,
            Description = DepositDescription(lines),
            SourceType = TroopTransactionSource.Deposit,
            SourceId = depositId,
            CreatedUtc = now,
            CreatedBy = recordedBy,
        };
        var deposit = new DepositEntity
        {
            RowKey = depositId,
            AccountId = account.AccountId,
            Date = Midnight(request.Date),
            Total = total,
            CheckTotal = checks,
            CashTotal = cash,
            OtherTotal = total - checks - cash,
            CashCount = request.CashCount.Count(c => c.Value > 0) == 0
                ? ""
                : JsonSerializer.Serialize(request.CashCount.Where(c => c.Value > 0).ToDictionary(c => c.Key.ToString(CultureInfo.InvariantCulture), c => c.Value)),
            CountedBy1 = request.CountedBy1.Trim(),
            CountedBy2 = request.CountedBy2.Trim(),
            TransactionRowKey = entry.RowKey,
            CreatedUtc = now,
            CreatedBy = recordedBy,
        };

        await unitOfWork.InTransactionAsync(async () =>
        {
            // Checked again inside the transaction, so two people depositing the same check at
            // once can't both succeed.
            var deposited = await bankRepository.GetDepositedSourcesAsync();
            if (lines.Any(l => l.Kind != DepositLineKind.Other && deposited.Contains((l.Kind, l.SourceId))))
            {
                throw new InvalidOperationException("One of the items has already been deposited. Reload the page and try again.");
            }

            await bankRepository.AddDepositAsync(deposit, lines);
            await bankRepository.AddTransactionAsync(entry);
            await auditRepository.AddAsync(FinanceAuditEntityType.Deposit, depositId, "Deposited", recordedBy,
                details: $"{Money(total)} into {account.Name} on {Day(deposit.Date)}: {entry.Description}.");
        });
        return deposit;

        DepositLineEntity Line(string kind, string sourceId, string payer, string method, string reference, decimal amount, string description, string? fundraiserId) => new()
        {
            PartitionKey = depositId,
            RowKey = (lines.Count + 1).ToString("D3", CultureInfo.InvariantCulture),
            Kind = kind,
            SourceId = sourceId,
            Payer = payer,
            Method = method,
            Reference = reference,
            Amount = amount,
            Description = description,
            FundraiserId = fundraiserId,
        };
    }

    public async Task DeleteDepositAsync(string depositId, string deletedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var deposit = await bankRepository.GetDepositAsync(depositId) ?? throw new InvalidOperationException("Deposit not found.");
            var entry = await bankRepository.GetTransactionAsync(deposit.AccountId, deposit.TransactionRowKey);
            if (entry is not null)
            {
                EnsureUnlocked(entry);
                await bankRepository.DeleteTransactionAsync(entry.AccountId, entry.RowKey);
            }

            await bankRepository.DeleteDepositAsync(depositId);
            await auditRepository.AddAsync(FinanceAuditEntityType.Deposit, depositId, "Deleted", deletedBy,
                details: $"Deleted the {Money(deposit.Total)} deposit of {Day(deposit.Date)}; its items are waiting to be deposited again.");
        });
    }

    public async Task PayReimbursementAsync(string guardianId, string requestId, string accountId, DateOnly paidOn, string checkNumber, string paidBy)
    {
        var request = await reimbursementRepository.GetByIdAsync(guardianId, requestId) ?? throw new InvalidOperationException("Reimbursement not found.");
        if (request.StatusValue != ReimbursementStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved reimbursement can be paid.");
        }

        var account = await ActiveAccountAsync(accountId);
        var reference = checkNumber.Trim();
        var entry = new TroopTransactionEntity
        {
            PartitionKey = account.AccountId,
            RowKey = TroopTransactionEntity.NewRowKey(),
            Date = Midnight(paidOn),
            Amount = -request.AmountRequested,
            Kind = TroopTransactionKind.ReimbursementPayout,
            Payee = request.SubmitterName,
            Description = request.Description,
            Reference = reference,
            SourceType = TroopTransactionSource.Reimbursement,
            SourceId = request.RequestId,
            CreatedUtc = timeProvider.GetUtcNow(),
            CreatedBy = paidBy,
        };
        var note = reference.Length > 0 ? $"Check #{reference} from {account.Name}" : $"Paid from {account.Name}";

        await unitOfWork.InTransactionAsync(async () =>
        {
            // The register entry first: marking paid emails the submitter, which should only
            // happen once everything else has worked.
            await bankRepository.AddTransactionAsync(entry);
            await reimbursementService.MarkPaidAsync(guardianId, requestId, paidBy, note);
        });
    }

    public async Task<ReconciliationEntity> StartReconciliationAsync(string accountId, DateOnly statementDate, decimal statementBalance, string startedBy)
    {
        var account = await bankRepository.GetAccountAsync(accountId) ?? throw new InvalidOperationException("Account not found.");
        var existing = await bankRepository.GetReconciliationsAsync(accountId);
        if (existing.Any(r => r.Status == ReconciliationStatus.InProgress))
        {
            throw new InvalidOperationException($"{account.Name} already has a reconciliation in progress. Finish or cancel it first.");
        }
        if (existing.FirstOrDefault(r => r.Status == ReconciliationStatus.Complete) is { } last && Midnight(statementDate) <= last.StatementDate)
        {
            throw new InvalidOperationException($"The statement date must be after the last reconciled statement ({Day(last.StatementDate)}).");
        }

        var reconciliation = new ReconciliationEntity
        {
            PartitionKey = accountId,
            RowKey = ReconciliationEntity.NewRowKey(),
            StatementDate = Midnight(statementDate),
            StatementBalance = statementBalance,
            CreatedUtc = timeProvider.GetUtcNow(),
            CreatedBy = startedBy,
        };
        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.AddReconciliationAsync(reconciliation);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, "Started reconciliation", startedBy,
                details: $"Statement of {Day(reconciliation.StatementDate)}, ending balance {Money(statementBalance)}.");
        });
        return reconciliation;
    }

    public async Task<ReconciliationView> GetReconciliationAsync(string accountId, string reconciliationId)
    {
        var account = await bankRepository.GetAccountAsync(accountId) ?? throw new InvalidOperationException("Account not found.");
        var reconciliation = await bankRepository.GetReconciliationAsync(accountId, reconciliationId) ?? throw new InvalidOperationException("Reconciliation not found.");
        var entries = await bankRepository.GetTransactionsAsync(accountId);
        var ticked = reconciliation.ClearedIds;

        // A finished one shows what it cleared; one in progress, everything still open up to the statement date.
        var candidates = reconciliation.Status == ReconciliationStatus.Complete
            ? entries.Where(e => e.ReconciliationId == reconciliationId).ToList()
            : entries.Where(e => !e.IsCleared && e.Date <= reconciliation.StatementDate).ToList();
        var previously = account.OpeningBalance + entries.Where(e => e.IsCleared && e.ReconciliationId != reconciliationId).Sum(e => e.Amount);
        var tickedTotal = reconciliation.Status == ReconciliationStatus.Complete
            ? candidates.Sum(e => e.Amount)
            : candidates.Where(e => ticked.Contains(e.RowKey)).Sum(e => e.Amount);
        return new ReconciliationView(reconciliation, candidates, previously, tickedTotal);
    }

    public async Task SetClearedAsync(string accountId, string reconciliationId, IReadOnlySet<string> clearedRowKeys)
    {
        var reconciliation = await InProgressAsync(accountId, reconciliationId);
        reconciliation.Cleared = string.Join(",", clearedRowKeys.Order());
        await bankRepository.UpdateReconciliationAsync(reconciliation);
    }

    public async Task FinishReconciliationAsync(string accountId, string reconciliationId, string finishedBy)
    {
        var view = await GetReconciliationAsync(accountId, reconciliationId);
        if (view.Reconciliation.Status != ReconciliationStatus.InProgress)
        {
            throw new InvalidOperationException("This reconciliation is already finished.");
        }
        if (view.Difference != 0)
        {
            throw new InvalidOperationException($"The cleared balance is {Money(view.Difference)} away from the statement. Tick the entries on the statement until the difference is $0.00.");
        }

        var ticked = view.Reconciliation.ClearedIds;
        var reconciliation = view.Reconciliation;
        reconciliation.Status = ReconciliationStatus.Complete;
        reconciliation.Cleared = "";
        reconciliation.CompletedUtc = timeProvider.GetUtcNow();
        reconciliation.CompletedBy = finishedBy;
        await unitOfWork.InTransactionAsync(async () =>
        {
            foreach (var entry in view.Candidates.Where(e => ticked.Contains(e.RowKey)))
            {
                entry.ReconciliationId = reconciliationId;
                await bankRepository.UpdateTransactionAsync(entry);
            }

            await bankRepository.UpdateReconciliationAsync(reconciliation);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, "Reconciled", finishedBy,
                details: $"Statement of {Day(reconciliation.StatementDate)} balanced at {Money(reconciliation.StatementBalance)}; {ticked.Count} entries cleared.");
        });
    }

    public async Task CancelReconciliationAsync(string accountId, string reconciliationId, string cancelledBy)
    {
        var reconciliation = await InProgressAsync(accountId, reconciliationId);
        await unitOfWork.InTransactionAsync(async () =>
        {
            await bankRepository.DeleteReconciliationAsync(accountId, reconciliationId);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, "Cancelled reconciliation", cancelledBy,
                details: $"Statement of {Day(reconciliation.StatementDate)}.");
        });
    }

    public async Task ReopenLastReconciliationAsync(string accountId, string reopenedBy)
    {
        var all = await bankRepository.GetReconciliationsAsync(accountId);
        if (all.Any(r => r.Status == ReconciliationStatus.InProgress))
        {
            throw new InvalidOperationException("Finish or cancel the reconciliation in progress first.");
        }

        var last = all.FirstOrDefault(r => r.Status == ReconciliationStatus.Complete) ?? throw new InvalidOperationException("There's no finished reconciliation to reopen.");
        var cleared = (await bankRepository.GetTransactionsAsync(accountId)).Where(e => e.ReconciliationId == last.ReconciliationId).ToList();
        last.Status = ReconciliationStatus.InProgress;
        last.Cleared = string.Join(",", cleared.Select(e => e.RowKey).Order());
        last.CompletedUtc = null;
        last.CompletedBy = "";
        await unitOfWork.InTransactionAsync(async () =>
        {
            foreach (var entry in cleared)
            {
                entry.ReconciliationId = null;
                await bankRepository.UpdateTransactionAsync(entry);
            }

            await bankRepository.UpdateReconciliationAsync(last);
            await auditRepository.AddAsync(FinanceAuditEntityType.TroopAccount, accountId, "Reopened reconciliation", reopenedBy,
                details: $"Statement of {Day(last.StatementDate)}; its {cleared.Count} entries can be changed again.");
        });
    }

    private async Task<ReconciliationEntity> InProgressAsync(string accountId, string reconciliationId)
    {
        var reconciliation = await bankRepository.GetReconciliationAsync(accountId, reconciliationId) ?? throw new InvalidOperationException("Reconciliation not found.");
        return reconciliation.Status == ReconciliationStatus.InProgress
            ? reconciliation
            : throw new InvalidOperationException("This reconciliation is already finished.");
    }

    private async Task<TroopAccountEntity> ActiveAccountAsync(string accountId)
    {
        var account = await bankRepository.GetAccountAsync(accountId) ?? throw new InvalidOperationException("Choose an account.");
        return account.IsActive ? account : throw new InvalidOperationException($"{account.Name} is inactive.");
    }

    private async Task ValidateAccountAsync(TroopAccountEntity account)
    {
        if (string.IsNullOrWhiteSpace(account.Name))
        {
            throw new InvalidOperationException("Give the account a name.");
        }
        if (!TroopAccountKind.All.Contains(account.Kind))
        {
            throw new InvalidOperationException("Choose what kind of account this is.");
        }
        if ((await bankRepository.GetAccountsAsync()).Any(a => a.AccountId != account.AccountId && a.Name.Equals(account.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"There's already an account called {account.Name}.");
        }
    }

    private static void ValidateEntry(decimal amount, string payee, string description, string kind)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Enter an amount more than $0.00.");
        }
        if (kind == TroopTransactionKind.Expense && string.IsNullOrWhiteSpace(payee))
        {
            throw new InvalidOperationException("Say who was paid.");
        }
        if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(payee))
        {
            throw new InvalidOperationException("Say what this was for.");
        }
    }

    private static void ValidateReceipt(ReceiptUpload? receipt)
    {
        if (receipt is null)
        {
            return;
        }
        if (!ReceiptTypes.Contains(receipt.ContentType))
        {
            throw new InvalidOperationException("Receipts must be a JPEG, PNG, or PDF file.");
        }
        if (receipt.SizeBytes > MaxReceiptBytes)
        {
            throw new InvalidOperationException("Receipt files must be 10 MB or smaller.");
        }
    }

    private async Task AttachReceiptAsync(TroopTransactionEntity entry, ReceiptUpload? receipt)
    {
        if (receipt is null)
        {
            return;
        }

        // A new name each time, so replacing a receipt never overwrites the old file before the
        // database has moved on to the new one.
        entry.ReceiptBlobPath = await receiptStorage.UploadAsync(ReceiptFolder, $"{entry.RowKey}-{Guid.NewGuid():N}", receipt.Content, receipt.ContentType);
        entry.ReceiptContentType = receipt.ContentType;
        entry.ReceiptFileName = Path.GetFileName(receipt.FileName);
    }

    private async Task DeleteReceiptAsync(string blobPath)
    {
        if (!string.IsNullOrEmpty(blobPath))
        {
            await receiptStorage.DeleteAsync(blobPath);
        }
    }

    private static void EnsureUnlocked(TroopTransactionEntity entry)
    {
        if (entry.IsCleared)
        {
            throw new InvalidOperationException("This entry has been reconciled with a bank statement, so it can't be changed. Reopen that reconciliation first.");
        }
    }

    private static string DepositDescription(IReadOnlyList<DepositLineEntity> lines)
    {
        var parts = new List<string>();
        var checks = lines.Count(l => l.Method == InvoicePaymentMethod.Check);
        if (checks > 0)
        {
            parts.Add(checks == 1 ? "1 check" : $"{checks} checks");
        }
        if (lines.Any(l => l.Method == InvoicePaymentMethod.Cash))
        {
            parts.Add("cash");
        }
        var others = lines.Count(l => l.Method != InvoicePaymentMethod.Check && l.Method != InvoicePaymentMethod.Cash);
        if (others > 0)
        {
            parts.Add(others == 1 ? "1 other item" : $"{others} other items");
        }

        return "Deposit: " + string.Join(", ", parts);
    }

    private static string Describe(TroopTransactionEntity entry) =>
        $"{(entry.Amount >= 0 ? "" : "-")}{Money(Math.Abs(entry.Amount))} {entry.Kind.ToLowerInvariant()}" +
        (entry.Payee.Length > 0 ? $" ({entry.Payee})" : "") + $" on {Day(entry.Date)}";

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static string Day(DateTimeOffset date) => date.UtcDateTime.ToString("MMM d, yyyy", Us);

    private static string Money(decimal amount) => amount.ToString("C", Us);
}
