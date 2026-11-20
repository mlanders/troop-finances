using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Tests;

/// <summary>The real DonationService and DonationRepository against a real SQL database (LocalDB).</summary>
public class SqlDonationTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    private async Task<DonationService> ServiceAsync()
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();

        var scouts = new ScoutRepository(sql.Db, TimeProvider.System);
        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 10m });
        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "casey", FullName = "Casey Rivera" });
        return new DonationService(new DonationRepository(sql.Db), scouts, new ScoutLedgerRepository(sql.Db, scouts),
            new FinanceAuditRepository(sql.Db, TimeProvider.System), TimeProvider.System, sql.Db);
    }

    private static DonationEntity Check(decimal amount = 300, string donor = "Main Street Hardware") => new()
    {
        DonorName = donor, ReceivedDate = Day(9, 15), Amount = amount, Method = InvoicePaymentMethod.Check, Reference = "5521",
    };

    private async Task<decimal> BalanceAsync(string scoutId)
    {
        await using var db = sql.NewContext();
        return (await db.Scouts.AsNoTracking().SingleAsync(s => s.RowKey == scoutId)).CachedBalance;
    }

    [SkippableFact]
    public async Task Recording_saves_the_gift_and_audits_it()
    {
        var service = await ServiceAsync();

        var saved = await service.RecordAsync(Check(), "Pat");

        var reread = await new DonationRepository(sql.Db).GetByIdAsync(saved.DonationId);
        Assert.Equal(("Main Street Hardware", 300m, "Pat"), (reread!.DonorName, reread.Amount, reread.CreatedBy));
        Assert.True(reread.NeedsAcknowledgment);
        var audit = await new FinanceAuditRepository(sql.Db, TimeProvider.System).GetForEntityAsync(FinanceAuditEntityType.Donation, saved.DonationId);
        Assert.Equal("Recorded", Assert.Single(audit).Action);
    }

    [SkippableFact]
    public async Task Received_between_uses_calendar_dates()
    {
        var service = await ServiceAsync();
        foreach (var (day, amount) in new[] { (Day(8, 31), 1m), (Day(9, 1), 2m), (Day(9, 30), 3m), (Day(10, 1), 4m) })
        {
            var gift = Check(amount);
            gift.ReceivedDate = day;
            await service.RecordAsync(gift, "Pat");
        }

        var september = await new DonationRepository(sql.Db).GetReceivedBetweenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

        Assert.Equal([2m, 3m], september.Select(d => d.Amount).Order());
    }

    [SkippableFact]
    public async Task Crediting_scouts_adds_to_their_balances_and_shows_what_is_left()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(Check(300), "Pat");

        await service.CreditScoutsAsync(gift.DonationId, [new("alex", 100m), new("casey", 50m)], "", "Pat");

        Assert.Equal(110m, await BalanceAsync("alex"));
        Assert.Equal(50m, await BalanceAsync("casey"));
        var credits = await service.GetCreditsAsync(gift.DonationId);
        Assert.Equal(150m, credits.Sum(c => c.Amount));
        Assert.All(credits, c => Assert.Equal((LedgerSourceType.Donation, "Donation from Main Street Hardware"), (c.SourceTypeValue, c.Description)));
    }

    [SkippableFact]
    public async Task An_anonymous_gift_is_described_without_the_donor_name()
    {
        var service = await ServiceAsync();
        var anonymous = Check(200, "The Nguyen family");
        anonymous.IsAnonymous = true;
        var gift = await service.RecordAsync(anonymous, "Pat");

        await service.CreditScoutsAsync(gift.DonationId, [new("alex", 200m)], " ", "Pat");

        Assert.Equal("Anonymous donation", Assert.Single(await service.GetCreditsAsync(gift.DonationId)).Description);
    }

    [SkippableFact]
    public async Task Crediting_more_than_is_left_saves_nothing()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(Check(100), "Pat");
        await service.CreditScoutsAsync(gift.DonationId, [new("alex", 60m)], "", "Pat");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreditScoutsAsync(gift.DonationId, [new("alex", 20m), new("casey", 30m)], "", "Pat"));

        Assert.Contains("$40.00", ex.Message);
        Assert.Equal(70m, await BalanceAsync("alex"));
        Assert.Equal(0m, await BalanceAsync("casey"));
    }

    [SkippableFact]
    public async Task Goods_cannot_be_credited_to_scouts()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(new DonationEntity
        {
            DonorName = "Pat Morgan", ReceivedDate = Day(9, 1), Amount = 180, Method = DonationMethod.InKind, Reference = "Two tents",
        }, "Pat");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreditScoutsAsync(gift.DonationId, [new("alex", 10m)], "", "Pat"));
        Assert.Equal(10m, await BalanceAsync("alex"));
    }

    [SkippableFact]
    public async Task A_credited_gift_cannot_shrink_below_its_credits_or_be_deleted()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(Check(100), "Pat");
        await service.CreditScoutsAsync(gift.DonationId, [new("alex", 80m)], "", "Pat");

        var edited = (await new DonationRepository(sql.Db).GetByIdAsync(gift.DonationId))!;
        edited.Amount = 50;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(edited, "Pat"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(gift.DonationId, "Pat"));

        edited.Amount = 80;
        await service.UpdateAsync(edited, "Pat");
        Assert.Equal(80m, (await new DonationRepository(sql.Db).GetByIdAsync(gift.DonationId))!.Amount);
    }

    [SkippableFact]
    public async Task An_uncredited_gift_can_be_deleted()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(Check(), "Pat");

        await service.DeleteAsync(gift.DonationId, "Pat");

        Assert.Null(await new DonationRepository(sql.Db).GetByIdAsync(gift.DonationId));
    }

    [SkippableFact]
    public async Task Thank_you_can_be_marked_sent_and_undone()
    {
        var service = await ServiceAsync();
        var gift = await service.RecordAsync(Check(), "Pat");
        var donations = new DonationRepository(sql.Db);

        await service.SetAcknowledgedAsync(gift.DonationId, true, "Jordan");
        var sent = (await donations.GetByIdAsync(gift.DonationId))!;
        Assert.Equal(("Jordan", false), (sent.AcknowledgedBy, sent.NeedsAcknowledgment));

        await service.SetAcknowledgedAsync(gift.DonationId, false, "Jordan");
        Assert.True((await donations.GetByIdAsync(gift.DonationId))!.NeedsAcknowledgment);
    }

    [SkippableFact]
    public async Task A_restricted_gift_needs_a_purpose()
    {
        var service = await ServiceAsync();
        var gift = Check();
        gift.IsRestricted = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(gift, "Pat"));
        Assert.Contains("used for", ex.Message);
    }
}
