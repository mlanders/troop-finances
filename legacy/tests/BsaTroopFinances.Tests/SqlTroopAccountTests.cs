using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BsaTroopFinances.Tests;

/// <summary>The real TroopAccountService and its repositories against a real SQL database (LocalDB):
/// accounts, the register, transfers, deposits, reimbursement payouts and reconciliation.</summary>
public class SqlTroopAccountTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static readonly DateOnly Opening = new(2026, 1, 1);
    private readonly IReceiptStorageService _receipts = Substitute.For<IReceiptStorageService>();

    private TroopBankRepository Bank => new(sql.Db);

    private async Task<TroopAccountService> ServiceAsync(IReimbursementService? reimbursements = null)
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();

        var requests = new ReimbursementRequestRepository(sql.Db);
        var audit = new FinanceAuditRepository(sql.Db, TimeProvider.System);
        reimbursements ??= new ReimbursementService(requests, _receipts, audit, Substitute.For<INotificationService>(), TimeProvider.System, sql.Db);
        return new TroopAccountService(Bank, new InvoiceRepository(sql.Db), new InvoicePaymentRepository(sql.Db), new DonationRepository(sql.Db),
            requests, reimbursements, _receipts, audit, TimeProvider.System, sql.Db);
    }

    private static DateTimeOffset At(int month, int day) => new(2026, month, day, 17, 0, 0, TimeSpan.Zero);

    private async Task<decimal> BalanceAsync(TroopAccountService service, string accountId) =>
        (await service.GetSummariesAsync()).Single(s => s.Account.AccountId == accountId).Balance;

    private async Task AddPaymentAsync(string invoiceId, string rowKey, decimal amount, string method, DateTimeOffset paid, string groupId = "")
    {
        var invoices = new InvoiceRepository(sql.Db);
        if (await invoices.GetByInvoiceIdAsync(invoiceId) is null)
        {
            await invoices.AddAsync(new InvoiceEntity
            {
                PartitionKey = "alex", RowKey = invoiceId, InvoiceId = invoiceId, ScoutId = "alex", ScoutName = "Alex Rivera",
                Description = $"Invoice {invoiceId}", TotalAmount = 500, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = At(1, 2),
            });
        }

        await new InvoicePaymentRepository(sql.Db).AddAsync(new InvoicePaymentEntity
        {
            PartitionKey = invoiceId, RowKey = rowKey, InvoiceId = invoiceId, ScoutId = "alex", Amount = amount, Method = method,
            Reference = "1042", PaidUtc = paid, PaymentGroupId = groupId,
        });
    }

    private async Task AddDonationAsync(string id, decimal amount, string method, DateTimeOffset received) =>
        await new DonationRepository(sql.Db).AddAsync(new DonationEntity
        {
            PartitionKey = DonationEntity.PartitionKeyValue, RowKey = id, DonorName = $"Donor {id}", Amount = amount, Method = method,
            Reference = method == DonationMethod.InKind ? "Tents" : "", ReceivedDate = received,
        });

    [SkippableFact]
    public async Task Expenses_and_income_move_the_balance_and_a_transfer_moves_money_between_accounts()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        var venmo = await service.AddAccountAsync("Venmo", TroopAccountKind.Venmo, 50, Opening, "Pat");

        await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Expense, new(2026, 2, 1), 120, "Scout Shop", "Rank patches", "301", null, "Pat");
        await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Interest, new(2026, 2, 28), 1.25m, "", "February interest", "", null, "Pat");
        await service.TransferAsync(venmo.AccountId, checking.AccountId, new(2026, 3, 1), 40, "", "Pat");

        Assert.Equal(921.25m, await BalanceAsync(service, checking.AccountId));
        Assert.Equal(10m, await BalanceAsync(service, venmo.AccountId));
        var halves = (await Bank.GetAllTransactionsAsync()).Where(t => t.SourceType == TroopTransactionSource.Transfer).ToList();
        Assert.Equal(2, halves.Count);
        Assert.Single(halves.Select(t => t.SourceId).Distinct());

        // Deleting either half removes the whole transfer.
        await service.DeleteEntryAsync(checking.AccountId, halves.Single(h => h.AccountId == checking.AccountId).RowKey, "Pat");
        Assert.Equal(50m, await BalanceAsync(service, venmo.AccountId));
    }

    [SkippableFact]
    public async Task An_account_with_entries_cant_be_deleted_and_names_are_unique()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 0, Opening, "Pat");
        await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.BankFee, new(2026, 2, 1), 5, "", "Monthly fee", "", null, "Pat");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAccountAsync(checking.AccountId, "Pat"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAccountAsync("checking", TroopAccountKind.Bank, 0, Opening, "Pat"));
    }

    [SkippableFact]
    public async Task Undeposited_holds_money_received_since_the_opening_date_and_not_scout_funds_or_goods()
    {
        var service = await ServiceAsync();
        await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 0, Opening, "Pat");
        await AddPaymentAsync("inv1", "p-check", 100, InvoicePaymentMethod.Check, At(2, 1));
        await AddPaymentAsync("inv1", "p-funds", 25, InvoicePaymentMethod.ScoutFunds, At(2, 1));
        await AddPaymentAsync("inv1", "p-old", 60, InvoicePaymentMethod.Cash, new DateTimeOffset(2025, 12, 1, 17, 0, 0, TimeSpan.Zero));
        // One family check split across two invoices is one item.
        await AddPaymentAsync("inv2", "p-g1", 30, InvoicePaymentMethod.Check, At(2, 3), groupId: "group1");
        await AddPaymentAsync("inv3", "p-g2", 45, InvoicePaymentMethod.Check, At(2, 3), groupId: "group1");
        await AddDonationAsync("d-money", 200, InvoicePaymentMethod.Venmo, At(2, 5));
        await AddDonationAsync("d-goods", 180, DonationMethod.InKind, At(2, 5));

        var items = await service.GetUndepositedAsync();

        Assert.Equal(["p-check", "group1", "d-money"], items.Select(i => i.SourceId));
        Assert.Equal(75m, items[1].Amount);
        Assert.Equal("2 invoices", items[1].For);
    }

    [SkippableFact]
    public async Task A_deposit_takes_its_items_off_undeposited_and_can_be_undone()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 0, Opening, "Pat");
        await AddPaymentAsync("inv1", "p-check", 100, InvoicePaymentMethod.Check, At(2, 1));
        await AddPaymentAsync("inv1", "p-cash", 35, InvoicePaymentMethod.Cash, At(2, 1));

        var request = new DepositRequest(checking.AccountId, new(2026, 2, 2),
            [(DepositLineKind.InvoicePayment, "p-check"), (DepositLineKind.InvoicePayment, "p-cash")],
            [new OtherIncomeLine("Car wash", 80, InvoicePaymentMethod.Cash)],
            new Dictionary<decimal, int> { [20m] = 5, [10m] = 1, [5m] = 1 }, "Pat", "Sam");
        var deposit = await service.CreateDepositAsync(request, "Pat");

        Assert.Equal((215m, 100m, 115m), (deposit.Total, deposit.CheckTotal, deposit.CashTotal));
        Assert.Empty(await service.GetUndepositedAsync());
        Assert.Equal(215m, await BalanceAsync(service, checking.AccountId));
        Assert.Equal(3, (await Bank.GetDepositLinesAsync(deposit.DepositId)).Count);

        // The same check can't go on a second deposit.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDepositAsync(request with { Other = [] }, "Pat"));

        await service.DeleteDepositAsync(deposit.DepositId, "Pat");
        Assert.Equal(2, (await service.GetUndepositedAsync()).Count);
        Assert.Equal(0m, await BalanceAsync(service, checking.AccountId));
    }

    [SkippableFact]
    public async Task Cash_must_match_the_count_and_be_counted_by_two_people()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 0, Opening, "Pat");
        var cash = new DepositRequest(checking.AccountId, new(2026, 2, 2), [], [new OtherIncomeLine("Car wash", 80, InvoicePaymentMethod.Cash)],
            new Dictionary<decimal, int> { [20m] = 3 }, "Pat", "Sam");

        var wrongCount = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDepositAsync(cash, "Pat"));
        Assert.Contains("$60.00", wrongCount.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDepositAsync(cash with { CashCount = new Dictionary<decimal, int> { [20m] = 4 }, CountedBy2 = "" }, "Pat"));
    }

    [SkippableFact]
    public async Task Paying_a_reimbursement_marks_it_paid_and_takes_the_money_out_together()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 500, Opening, "Pat");
        await new ReimbursementRequestRepository(sql.Db).AddAsync(new ReimbursementRequestEntity
        {
            PartitionKey = "maria", RowKey = "req1", RequestId = "req1", SubmitterGuardianId = "maria", SubmitterName = "Maria Rivera",
            AmountRequested = 64.50m, Description = "Campout food", StatusValue = ReimbursementStatus.Approved, SubmittedUtc = At(2, 1),
        });

        await service.PayReimbursementAsync("maria", "req1", checking.AccountId, new(2026, 2, 10), "1043", "Pat");

        var request = await new ReimbursementRequestRepository(sql.Db).GetByIdAsync("maria", "req1");
        Assert.Equal((ReimbursementStatus.Paid, "Check #1043 from Checking"), (request!.StatusValue, request.PaymentMethodNote));
        var entry = Assert.Single(await Bank.GetTransactionsAsync(checking.AccountId));
        Assert.Equal((-64.50m, "Maria Rivera", "1043"), (entry.Amount, entry.Payee, entry.Reference));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteEntryAsync(checking.AccountId, entry.RowKey, "Pat"));
    }

    [SkippableFact]
    public async Task If_marking_the_reimbursement_paid_fails_no_money_leaves_the_account()
    {
        var failing = Substitute.For<IReimbursementService>();
        failing.MarkPaidAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));
        var service = await ServiceAsync(failing);
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 500, Opening, "Pat");
        await new ReimbursementRequestRepository(sql.Db).AddAsync(new ReimbursementRequestEntity
        {
            PartitionKey = "maria", RowKey = "req1", RequestId = "req1", SubmitterGuardianId = "maria", SubmitterName = "Maria Rivera",
            AmountRequested = 64.50m, StatusValue = ReimbursementStatus.Approved, SubmittedUtc = At(2, 1),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayReimbursementAsync("maria", "req1", checking.AccountId, new(2026, 2, 10), "", "Pat"));

        Assert.Empty(await Bank.GetTransactionsAsync(checking.AccountId));
    }

    [SkippableFact]
    public async Task A_reconciliation_finishes_only_when_it_balances_and_then_locks_its_entries()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        var fee = await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.BankFee, new(2026, 1, 31), 5, "", "Monthly fee", "", null, "Pat");
        var check = await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Expense, new(2026, 1, 20), 120, "Scout Shop", "Patches", "301", null, "Pat");
        await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Expense, new(2026, 2, 3), 40, "Council", "Training", "302", null, "Pat");

        // The January statement shows the fee but not check 301 yet.
        var reconciliation = await service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 31), 995, "Pat");
        var view = await service.GetReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId);
        Assert.Equal(2, view.Candidates.Count);       // the February check isn't on a January statement
        Assert.Equal(-5m, view.Difference);

        await service.SetClearedAsync(checking.AccountId, reconciliation.ReconciliationId, new HashSet<string> { check.RowKey });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinishReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId, "Pat"));

        await service.SetClearedAsync(checking.AccountId, reconciliation.ReconciliationId, new HashSet<string> { fee.RowKey });
        Assert.Equal(0m, (await service.GetReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId)).Difference);
        await service.FinishReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId, "Pat");

        var summary = (await service.GetSummariesAsync()).Single();
        Assert.Equal((995m, 2, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero)), (summary.ClearedBalance, summary.UnclearedCount, summary.LastReconciled!.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteEntryAsync(checking.AccountId, fee.RowKey, "Pat"));
        // The next statement must come after this one.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 15), 0, "Pat"));

        await service.ReopenLastReconciliationAsync(checking.AccountId, "Pat");
        Assert.Equal(1000m, (await service.GetSummariesAsync()).Single().ClearedBalance);
        var reopened = await service.GetReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId);
        Assert.Equal(0m, reopened.Difference);   // still ticked, ready to finish again
        await service.DeleteEntryAsync(checking.AccountId, fee.RowKey, "Pat");
    }

    [SkippableFact]
    public async Task Editing_an_entry_keeps_which_way_the_money_went_and_moves_the_balance()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        var expense = await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Expense, new(2026, 2, 1), 120, "Scout Shop", "Patches", "301", null, "Pat");
        var income = await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.OtherIncome, new(2026, 2, 2), 50, "Council", "Refund", "", null, "Pat");

        await service.UpdateEntryAsync(checking.AccountId, expense.RowKey, new(2026, 2, 3), 95, " Scout Shop ", "Rank patches", "302", null, "Pat");
        await service.UpdateEntryAsync(checking.AccountId, income.RowKey, new(2026, 2, 2), 60, "Council", "Refund", "", null, "Pat");

        var edited = (await Bank.GetTransactionAsync(checking.AccountId, expense.RowKey))!;
        Assert.Equal((-95m, "Scout Shop", "Rank patches", "302", new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero)),
            (edited.Amount, edited.Payee, edited.Description, edited.Reference, edited.Date));
        Assert.Equal(60m, (await Bank.GetTransactionAsync(checking.AccountId, income.RowKey))!.Amount);
        Assert.Equal(965m, await BalanceAsync(service, checking.AccountId));
    }

    [SkippableFact]
    public async Task A_transfer_cant_be_edited_and_a_reconciled_entry_cant_be_edited()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        var venmo = await service.AddAccountAsync("Venmo", TroopAccountKind.Venmo, 50, Opening, "Pat");
        await service.TransferAsync(venmo.AccountId, checking.AccountId, new(2026, 1, 5), 40, "", "Pat");
        var half = (await Bank.GetTransactionsAsync(checking.AccountId)).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateEntryAsync(checking.AccountId, half.RowKey, new(2026, 1, 5), 400, "", "Transfer", "", null, "Pat"));

        var fee = await service.RecordEntryAsync(checking.AccountId, TroopTransactionKind.BankFee, new(2026, 1, 31), 5, "", "Monthly fee", "", null, "Pat");
        var reconciliation = await service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 31), 1035, "Pat");
        await service.SetClearedAsync(checking.AccountId, reconciliation.ReconciliationId, new HashSet<string> { half.RowKey, fee.RowKey });
        await service.FinishReconciliationAsync(checking.AccountId, reconciliation.ReconciliationId, "Pat");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateEntryAsync(checking.AccountId, fee.RowKey, new(2026, 1, 31), 50, "", "Monthly fee", "", null, "Pat"));
        Assert.Equal(-5m, (await Bank.GetTransactionAsync(checking.AccountId, fee.RowKey))!.Amount);
        Assert.Equal(40m, (await Bank.GetTransactionAsync(checking.AccountId, half.RowKey))!.Amount);
    }

    [SkippableFact]
    public async Task An_account_can_be_renamed_and_made_inactive_but_not_given_another_accounts_name()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        await service.AddAccountAsync("Savings", TroopAccountKind.Bank, 0, Opening, "Pat");

        var account = (await Bank.GetAccountAsync(checking.AccountId))!;
        account.Name = "  Main checking ";
        account.IsActive = false;
        await service.UpdateAccountAsync(account, "Pat");
        var saved = (await Bank.GetAccountAsync(checking.AccountId))!;
        Assert.Equal(("Main checking", false), (saved.Name, saved.IsActive));

        saved.Name = "SAVINGS";
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAccountAsync(saved, "Pat"));
        Assert.Contains("already an account called", refused.Message);
        Assert.Equal("Main checking", (await Bank.GetAccountAsync(checking.AccountId))!.Name);
    }

    [SkippableFact]
    public async Task Cancelling_a_reconciliation_removes_it_so_another_can_start_and_a_finished_one_cant_be_cancelled()
    {
        var service = await ServiceAsync();
        var checking = await service.AddAccountAsync("Checking", TroopAccountKind.Bank, 1000, Opening, "Pat");
        var first = await service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 31), 1000, "Pat");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 31), 1000, "Pat"));

        await service.CancelReconciliationAsync(checking.AccountId, first.ReconciliationId, "Pat");

        Assert.Empty(await Bank.GetReconciliationsAsync(checking.AccountId));
        var second = await service.StartReconciliationAsync(checking.AccountId, new(2026, 1, 31), 1000, "Pat");
        await service.FinishReconciliationAsync(checking.AccountId, second.ReconciliationId, "Pat");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelReconciliationAsync(checking.AccountId, second.ReconciliationId, "Pat"));
        Assert.Equal(ReconciliationStatus.Complete, Assert.Single(await Bank.GetReconciliationsAsync(checking.AccountId)).Status);
    }
}
