using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>
/// The real ScoutAccountService and repositories against a real SQL database, with a failure
/// forced partway through: every money operation must save completely or not at all.
/// </summary>
public class SqlTransactionTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static readonly DateTimeOffset Issued = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    /// <summary>The real audit repository, except it throws for one entity -- a stand-in for any
    /// failure late in an operation, after earlier writes in the same transaction have happened.</summary>
    private sealed class AuditThatFailsFor(IFinanceAuditRepository inner, string failForEntityId) : IFinanceAuditRepository
    {
        public Task AddAsync(string entityType, string entityId, string action, string actor, string? notes = null, string? details = null) =>
            entityId == failForEntityId
                ? throw new InvalidOperationException("simulated failure")
                : inner.AddAsync(entityType, entityId, action, actor, notes, details);

        public Task<IReadOnlyList<FinanceAuditEntryEntity>> GetForEntityAsync(string entityType, string entityId) => inner.GetForEntityAsync(entityType, entityId);

        public Task<IReadOnlyList<FinanceAuditEntryEntity>> GetSinceAsync(DateTimeOffset sinceUtc) => inner.GetSinceAsync(sinceUtc);

        public Task<IReadOnlyList<FinanceAuditEntryEntity>> GetRecentAsync(int count) => inner.GetRecentAsync(count);
    }

    private async Task<ScoutAccountService> ServiceAsync(string? failAuditFor = null)
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();

        var scouts = new ScoutRepository(sql.Db, TimeProvider.System);
        IFinanceAuditRepository audit = new FinanceAuditRepository(sql.Db, TimeProvider.System);
        if (failAuditFor is not null)
        {
            audit = new AuditThatFailsFor(audit, failAuditFor);
        }

        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 100m });
        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "casey", FullName = "Casey Rivera" });
        var invoices = new InvoiceRepository(sql.Db);
        foreach (var (id, scoutId, name, due) in new[] { ("inv-camp", "alex", "Alex Rivera", 175m), ("inv-food", "casey", "Casey Rivera", 45m) })
        {
            await invoices.AddAsync(new InvoiceEntity
            {
                PartitionKey = scoutId, RowKey = id, InvoiceId = id, ScoutId = scoutId, ScoutName = name, Description = id,
                TotalAmount = due, AmountDueFromFamily = due, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = Issued,
            });
        }

        return new ScoutAccountService(scouts, new ScoutLedgerRepository(sql.Db, scouts), invoices, new InvoicePaymentRepository(sql.Db),
            new FundraiserEventRepository(sql.Db), new GuardianScoutLinkRepository(sql.Db), new GuardianRepository(sql.Db), new ReimbursementRequestRepository(sql.Db),
            audit, Substitute.For<INotificationService>(), TimeProvider.System, sql.Db);
    }

    private async Task<(int Payments, int Audit, int Ledger, decimal CampDue, decimal FoodDue, decimal AlexBalance)> SnapshotAsync()
    {
        await using var db = sql.NewContext();
        var invoices = await db.Invoices.AsNoTracking().ToDictionaryAsync(i => i.InvoiceId);
        var alex = await db.Scouts.AsNoTracking().SingleAsync(s => s.RowKey == "alex");
        return (await db.InvoicePayments.CountAsync(), await db.FinanceAudit.CountAsync(), await db.ScoutLedger.CountAsync(),
            invoices["inv-camp"].AmountDueFromFamily, invoices["inv-food"].AmountDueFromFamily, alex.CachedBalance);
    }

    [SkippableFact]
    public async Task A_family_payment_that_fails_partway_saves_nothing()
    {
        // The second invoice's audit entry fails, after the first invoice is fully written.
        var service = await ServiceAsync(failAuditFor: "inv-food");
        var before = await SnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 45m)], InvoicePaymentMethod.Check, "Check 1042", "Pat"));

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(0, before.Payments);
    }

    [SkippableFact]
    public async Task A_family_payment_that_succeeds_saves_everything()
    {
        var service = await ServiceAsync();

        await service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 20m)], InvoicePaymentMethod.Check, "Check 1042", "Pat");

        var after = await SnapshotAsync();
        Assert.Equal(2, after.Payments);
        Assert.Equal(2, after.Audit);
        Assert.Equal(0m, after.CampDue);
        Assert.Equal(25m, after.FoodDue);
    }

    [SkippableFact]
    public async Task A_scout_funds_payment_that_fails_late_leaves_the_balance_and_invoice_untouched()
    {
        // The ledger debit and balance change happen, then the audit write fails.
        var service = await ServiceAsync(failAuditFor: "inv-camp");
        var before = await SnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordInvoicePaymentAsync(
            "alex", "inv-camp", 60m, InvoicePaymentMethod.ScoutFunds, null, "Pat"));

        var after = await SnapshotAsync();
        Assert.Equal(before, after);
        Assert.Equal(100m, after.AlexBalance);
        Assert.Equal(0, after.Ledger);
    }

    [SkippableFact]
    public async Task A_scout_funds_payment_moves_the_balance_and_the_invoice_together()
    {
        var service = await ServiceAsync();

        await service.RecordInvoicePaymentAsync("alex", "inv-camp", 60m, InvoicePaymentMethod.ScoutFunds, null, "Pat");

        var after = await SnapshotAsync();
        Assert.Equal(40m, after.AlexBalance);
        Assert.Equal(115m, after.CampDue);
        Assert.Equal(1, after.Ledger);
        Assert.Equal(1, after.Payments);
    }

    [SkippableFact]
    public async Task Voiding_reverses_the_account_credit_atomically()
    {
        var service = await ServiceAsync();
        var invoice = await service.IssueInvoiceAsync("alex", 80m, 80m, Issued.AddDays(30), "Camp deposit", "Pat");
        Assert.Equal(20m, (await SnapshotAsync()).AlexBalance);

        await service.VoidInvoiceAsync("alex", invoice.InvoiceId, "Entered twice", "Pat");

        var after = await SnapshotAsync();
        Assert.Equal(100m, after.AlexBalance);
        await using var db = sql.NewContext();
        Assert.Equal(InvoiceStatus.Voided, (await db.Invoices.SingleAsync(i => i.InvoiceId == invoice.InvoiceId)).StatusValue);
    }
}
