using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>How guardians are grouped into families, and what goes on a family's status sheet.</summary>
public class FamilyStatusServiceTests
{
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IGuardianScoutLinkRepository _links = Substitute.For<IGuardianScoutLinkRepository>();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IScoutLedgerRepository _ledger = Substitute.For<IScoutLedgerRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly FamilyStatusService _service;

    private static readonly GuardianEntity Maria = Guardian("maria", "Maria Rivera");
    private static readonly GuardianEntity Luis = Guardian("luis", "Luis Rivera");
    private static readonly ScoutEntity Alex = new() { RowKey = "alex", FullName = "Alex Rivera", IsActive = true, CachedBalance = 70 };
    private static readonly ScoutEntity Casey = new() { RowKey = "casey", FullName = "Casey Rivera", IsActive = false, CachedBalance = 25 };

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    public FamilyStatusServiceTests()
    {
        _service = new FamilyStatusService(_guardians, _links, _scouts, _ledger, _invoices, _payments);
        _links.GetScoutsForGuardianAsync(Arg.Any<string>()).Returns(new List<GuardianScoutLinkEntity>());
        _ledger.GetForScoutAsync(Arg.Any<string>()).Returns(new List<ScoutLedgerEntryEntity>());
        _invoices.GetByScoutAsync(Arg.Any<string>()).Returns(new List<InvoiceEntity>());
        _invoices.GetByGuardianAsync(Arg.Any<string>()).Returns(new List<InvoiceEntity>());
        _invoices.GetGuardianIdsWithInvoicesAsync().Returns(new HashSet<string>());
        _payments.GetByInvoiceAsync(Arg.Any<string>()).Returns(new List<InvoicePaymentEntity>());
        _scouts.GetByIdAsync("alex").Returns(Alex);
        _scouts.GetByIdAsync("casey").Returns(Casey);
        _scouts.GetAllAsync().Returns(new List<ScoutEntity> { Alex, Casey });
    }

    private static GuardianEntity Guardian(string id, string name, bool active = true) => new() { GuardianId = id, FullName = name, IsActive = active };

    private static List<GuardianScoutLinkEntity> Links(params string[] scoutIds) =>
        scoutIds.Select(id => new GuardianScoutLinkEntity { ScoutId = id }).ToList();

    private static DateTimeOffset On(int year, int month, int day) => new(year, month, day, 17, 0, 0, TimeSpan.Zero);

    private static ScoutLedgerEntryEntity Entry(DateTimeOffset when, decimal amount, string description = "Popcorn sale", string? source = null) => new()
    {
        RowKey = $"{when.UtcTicks:D19}-{description}", CreatedUtc = when, Description = description,
        EntryTypeValue = amount >= 0 ? LedgerEntryType.Credit : LedgerEntryType.Debit, Amount = Math.Abs(amount),
        SourceTypeValue = source ?? LedgerSourceType.FundraiserAllocation,
    };

    private static InvoiceEntity Invoice(string id, DateTimeOffset issued, decimal due, string status = InvoiceStatus.Outstanding, DateTimeOffset? paid = null) => new()
    {
        PartitionKey = "alex", RowKey = id, InvoiceId = id, ScoutId = "alex", ScoutName = "Alex Rivera", Description = $"Invoice {id}",
        TotalAmount = 100, AmountDueFromFamily = due, StatusValue = status, IssuedUtc = issued, PaidUtc = paid,
    };

    private static Household Riveras(params string[] scoutIds) => new("Maria Rivera", [Maria], scoutIds);

    [Fact]
    public async Task Parents_who_share_a_scout_are_one_household_and_a_chain_of_shared_scouts_joins_up()
    {
        // Maria has Alex; Luis has Alex and Casey; Pat has Casey (a step-family chain); Lee has no scouts.
        var pat = Guardian("pat", "Pat Morgan");
        var lee = Guardian("lee", "Lee Leader");
        _links.GetScoutsForGuardianAsync("maria").Returns(Links("alex"));
        _links.GetScoutsForGuardianAsync("luis").Returns(Links("alex", "casey"));
        _links.GetScoutsForGuardianAsync("pat").Returns(Links("casey"));

        var households = await Households.BuildAsync([Maria, lee, pat, Luis], _links);

        Assert.Equal(["Lee Leader", "Luis Rivera and Maria Rivera and Pat Morgan"], households.Select(h => h.Name).Order());
        Assert.Equal(["alex", "casey"], households.Single(h => h.Guardians.Count == 3).ScoutIds.Order());
        Assert.Empty(households.Single(h => h.Name == "Lee Leader").ScoutIds);
    }

    [Fact]
    public async Task The_families_list_totals_funds_and_amounts_owed_and_flags_adults_with_nothing_on_file()
    {
        _guardians.GetAllAsync().Returns(new List<GuardianEntity> { Maria, Luis, Guardian("lee", "Lee Leader"), Guardian("new", "New Parent"), Guardian("old", "Old Parent", active: false) });
        _links.GetScoutsForGuardianAsync("maria").Returns(Links("alex", "casey"));
        _links.GetScoutsForGuardianAsync("luis").Returns(Links("alex"));
        _invoices.GetGuardianIdsWithInvoicesAsync().Returns(new HashSet<string> { "lee" });
        var adult = new InvoiceEntity { InvoiceId = "a1", GuardianId = "lee", GuardianName = "Lee Leader", AmountDueFromFamily = 40, StatusValue = InvoiceStatus.Outstanding };
        var scout = Invoice("s1", On(2026, 9, 1), 175);
        _invoices.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, int.MaxValue).Returns(new PagedResult<InvoiceEntity>([adult, scout], 2));

        var families = await _service.GetFamiliesAsync(includeInactive: false);

        Assert.Equal(["Lee Leader", "Luis Rivera and Maria Rivera", "New Parent"], families.Select(f => f.Family.Name));
        var riveras = families[1];
        Assert.Equal(["Alex Rivera", "Casey Rivera"], riveras.Scouts.Select(s => s.FullName));
        Assert.Equal(95m, riveras.ScoutFunds);
        Assert.Equal(175m, riveras.AmountOwed);
        Assert.True(families[0] is { HasAdultCharges: true, HasActivity: true, AmountOwed: 40m });
        Assert.False(families[2].HasActivity);

        Assert.Contains("Old Parent", (await _service.GetFamiliesAsync(includeInactive: true)).Select(f => f.Family.Name));
    }

    [Fact]
    public async Task Picked_guardians_resolve_to_their_households_once_even_when_inactive()
    {
        var old = Guardian("old", "Old Parent", active: false);
        _guardians.GetAllAsync().Returns(new List<GuardianEntity> { Maria, Luis, old, Guardian("pat", "Pat Morgan") });
        _links.GetScoutsForGuardianAsync("maria").Returns(Links("alex"));
        _links.GetScoutsForGuardianAsync("luis").Returns(Links("alex"));

        var households = await _service.ResolveAsync(["maria", "luis", "old"]);

        Assert.Equal(["Luis Rivera and Maria Rivera", "Old Parent"], households.Select(h => h.Name));
    }

    [Fact]
    public async Task A_scout_fund_opens_with_earlier_activity_and_lists_only_the_periods_entries()
    {
        _ledger.GetForScoutAsync("alex").Returns(new List<ScoutLedgerEntryEntity>
        {
            Entry(On(2025, 11, 1), 50, "Wreath sale"),
            Entry(On(2025, 12, 1), -20, "Campout"),
            Entry(On(2026, 3, 1), 0, "Renamed", LedgerSourceType.NameChange),
            Entry(On(2026, 4, 1), 80, "Popcorn sale"),
            Entry(On(2026, 6, 1), -40, "Summer camp"),
            Entry(On(2027, 1, 5), 10, "Next year"),
        });

        var fund = (await _service.GetSheetAsync(Riveras("alex"), From, To)).Scouts.Single();

        Assert.Equal(30m, fund.OpeningBalance);
        Assert.Equal(["Popcorn sale", "Summer camp"], fund.Entries.Select(e => e.Description));
        Assert.Equal((80m, 40m, 70m), (fund.MoneyIn, fund.MoneyOut, fund.ClosingBalance));
    }

    [Fact]
    public async Task Invoices_show_when_open_during_the_period_or_still_outstanding()
    {
        _invoices.GetByScoutAsync("alex").Returns(new List<InvoiceEntity>
        {
            Invoice("old-paid", On(2025, 3, 1), 0, InvoiceStatus.Paid, paid: On(2025, 4, 1)),
            Invoice("old-unpaid", On(2025, 3, 1), 60),
            Invoice("paid-in-period", On(2025, 12, 1), 0, InvoiceStatus.Paid, paid: On(2026, 2, 1)),
            Invoice("issued-in-period", On(2026, 5, 1), 0, InvoiceStatus.Paid, paid: On(2026, 5, 2)),
            Invoice("after-period", On(2027, 2, 1), 0, InvoiceStatus.Paid, paid: On(2027, 2, 2)),
        });

        var sheet = await _service.GetSheetAsync(Riveras("alex"), From, To);

        Assert.Equal(["old-unpaid", "paid-in-period", "issued-in-period"], sheet.Invoices.Select(i => i.InvoiceId));
        Assert.Equal(60m, sheet.AmountDue);
    }

    [Fact]
    public async Task Payments_are_the_familys_own_in_the_period_not_scout_funds()
    {
        _invoices.GetByScoutAsync("alex").Returns(new List<InvoiceEntity> { Invoice("camp", On(2026, 5, 1), 0, InvoiceStatus.Paid, paid: On(2026, 6, 1)) });
        _payments.GetByInvoiceAsync("camp").Returns(new List<InvoicePaymentEntity>
        {
            new() { InvoiceId = "camp", Amount = 30, Method = InvoicePaymentMethod.ScoutFunds, PaidUtc = On(2026, 5, 2) },
            new() { InvoiceId = "camp", Amount = 70, Method = InvoicePaymentMethod.Check, Reference = "1042", PaidUtc = On(2026, 6, 1) },
        });

        var payment = (await _service.GetSheetAsync(Riveras("alex"), From, To)).Payments.Single();

        Assert.Equal(70m, payment.Payment.Amount);
        Assert.Equal("Alex Rivera — Invoice camp", payment.For);
        Assert.Empty((await _service.GetSheetAsync(Riveras("alex"), new DateOnly(2026, 7, 1), To)).Payments);
    }

    [Fact]
    public async Task An_adult_with_no_scouts_gets_just_their_own_invoices()
    {
        var lee = Guardian("lee", "Lee Leader");
        _invoices.GetByGuardianAsync("lee").Returns(new List<InvoiceEntity>
        {
            new() { InvoiceId = "a1", PartitionKey = "lee", GuardianId = "lee", GuardianName = "Lee Leader", AmountDueFromFamily = 320, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = On(2026, 4, 1) },
        });

        var sheet = await _service.GetSheetAsync(new Household("Lee Leader", [lee], []), From, To);

        Assert.Empty(sheet.Scouts);
        Assert.Equal(["a1"], sheet.Invoices.Select(i => i.InvoiceId));
        Assert.Equal(320m, sheet.AdultAmountOwed);
        Assert.Equal(320m, sheet.AmountDue);
    }

    [Fact]
    public async Task A_parents_own_household_is_only_them_and_their_own_scouts()
    {
        _links.GetScoutsForGuardianAsync("maria").Returns(Links("alex"));

        var household = await _service.GetOwnHouseholdAsync(Maria);

        Assert.Equal("Maria Rivera", household.Name);
        Assert.Equal([Maria], household.Guardians);
        Assert.Equal(["alex"], household.ScoutIds);
    }

    [Theory]
    [InlineData(null, null, "2025-10-30", "2026-10-30")]
    [InlineData("2026-01-01", "2026-06-30", "2026-01-01", "2026-06-30")]
    [InlineData("2026-06-30", "2026-01-01", "2026-01-01", "2026-06-30")]
    [InlineData(null, "2026-06-30", "2025-06-30", "2026-06-30")]
    public void The_period_defaults_to_the_last_12_months_and_swaps_reversed_dates(string? from, string? to, string expectedFrom, string expectedTo)
    {
        var period = FamilyStatusService.Period(
            from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), new DateOnly(2026, 10, 30));

        Assert.Equal((DateOnly.Parse(expectedFrom), DateOnly.Parse(expectedTo)), period);
    }
}
