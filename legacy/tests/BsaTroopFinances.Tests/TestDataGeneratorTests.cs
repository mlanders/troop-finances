using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The test-data generator run through the real services against a real SQL database.</summary>
public class TestDataGeneratorTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Generated_activity_spans_the_last_year_and_keeps_balances_in_step_with_the_ledger()
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();

        var clock = new SettableClock(Now);
        var scouts = new ScoutRepository(sql.Db, clock);
        var guardians = new GuardianRepository(sql.Db);
        var links = new GuardianScoutLinkRepository(sql.Db);
        var fundraisers = new FundraiserEventRepository(sql.Db);
        var requests = new ReimbursementRequestRepository(sql.Db);
        var audit = new FinanceAuditRepository(sql.Db, clock);
        var notifications = Substitute.For<INotificationService>();
        var receipts = Substitute.For<IReceiptStorageService>();
        receipts.UploadAsync(default!, default!, default!, default!).ReturnsForAnyArgs(call => $"{call.ArgAt<string>(0)}/{call.ArgAt<string>(1)}.pdf");

        foreach (var name in new[] { "Alex Rivera", "Jordan Lee", "Sam Patel" })
        {
            await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = name.Split(' ')[0].ToLowerInvariant(), FullName = name });
        }
        await guardians.AddAsync(new GuardianEntity
        {
            PartitionKey = GuardianEntity.PartitionKeyValue, RowKey = "maria@example.com", GuardianId = "maria",
            FullName = "Maria Rivera", Email = "maria@example.com", IsActive = true, CreatedUtc = Now,
        });
        await links.AddLinkAsync(new GuardianScoutLinkEntity
        {
            PartitionKey = "maria", RowKey = GuardianScoutLinkEntity.BuildRowKey("alex"), GuardianId = "maria", ScoutId = "alex", ScoutName = "Alex Rivera", CreatedUtc = Now,
        });

        var generator = new TestDataGenerator(scouts, guardians, links, fundraisers,
            new ScoutAccountService(scouts, new ScoutLedgerRepository(sql.Db, scouts), new InvoiceRepository(sql.Db), new InvoicePaymentRepository(sql.Db),
                fundraisers, links, guardians, requests, audit, notifications, clock, sql.Db),
            new ReimbursementService(requests, receipts, audit, notifications, clock, sql.Db),
            clock);

        var summary = await generator.GenerateAsync(new TestDataOptions(Invoices: 80, Reimbursements: 40, Fundraisers: 2));

        await using var db = sql.NewContext();
        var invoices = await db.Invoices.AsNoTracking().ToListAsync();
        var reimbursements = await db.ReimbursementRequests.AsNoTracking().ToListAsync();
        Assert.Equal(80, invoices.Count);
        Assert.Equal(40, reimbursements.Count);
        Assert.Equal(2, await db.FundraiserEvents.CountAsync());
        Assert.Equal(summary.InvoicePayments, await db.InvoicePayments.CountAsync());

        // Spread over the year, never in the future, and in every status.
        var yearAgo = Now.AddMonths(-12);
        Assert.All(invoices, i => Assert.InRange(i.IssuedUtc, yearAgo, Now));
        Assert.All(reimbursements, r => Assert.InRange(r.SubmittedUtc, yearAgo, Now));
        Assert.True(invoices.Select(i => i.IssuedUtc.ToString("yyyy-MM")).Distinct().Count() >= 10);
        Assert.True(reimbursements.Select(r => r.SubmittedUtc.ToString("yyyy-MM")).Distinct().Count() >= 10);
        Assert.Equal([InvoiceStatus.Outstanding, InvoiceStatus.Paid, InvoiceStatus.Voided], invoices.Select(i => i.StatusValue).Distinct().Order());
        Assert.Equal(4, reimbursements.Select(r => r.StatusValue).Distinct().Count());
        Assert.All(invoices.Where(i => i.PaidUtc is not null), i => Assert.InRange(i.PaidUtc!.Value, i.IssuedUtc, Now));
        Assert.Equal(0, await db.FinanceAudit.CountAsync(a => a.OccurredUtc > Now));

        var ledger = await db.ScoutLedger.AsNoTracking().ToListAsync();
        foreach (var scout in await db.Scouts.AsNoTracking().ToListAsync())
        {
            Assert.Equal(ledger.Where(e => e.PartitionKey == scout.RowKey).Sum(e => e.SignedAmount), scout.CachedBalance);
            Assert.True(scout.CachedBalance >= 0);
        }

        await notifications.DidNotReceiveWithAnyArgs().SendLoginCodeAsync(default!, default!);
        Assert.Equal(Now, clock.Now);
    }
}
