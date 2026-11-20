using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The sign-in app's roster, trips and payments, kept in memory for the tests.</summary>
internal sealed class InMemorySignInAppRepository : ISignInAppRepository
{
    public Dictionary<string, SignInPersonEntity> People { get; } = [];
    public Dictionary<string, TripImportEntity> Trips { get; } = [];
    public Dictionary<(string, string), TripImportLineEntity> Lines { get; } = [];
    public Dictionary<(string, string), TripPaymentEntity> Payments { get; } = [];

    public Task<IReadOnlyList<SignInPersonEntity>> GetPeopleAsync() => Task.FromResult<IReadOnlyList<SignInPersonEntity>>(People.Values.ToList());
    public Task<SignInPersonEntity?> GetPersonAsync(string personId) => Task.FromResult(People.GetValueOrDefault(personId));

    public Task SavePersonAsync(SignInPersonEntity person)
    {
        People[person.PersonId] = person;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TripImportEntity>> GetTripsAsync() =>
        Task.FromResult<IReadOnlyList<TripImportEntity>>(Trips.Values.OrderBy(t => t.StartDate).ToList());

    public Task<TripImportEntity?> GetTripAsync(string signUpId) => Task.FromResult(Trips.GetValueOrDefault(signUpId));

    public Task SaveTripAsync(TripImportEntity trip)
    {
        Trips[trip.SignUpId] = trip;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TripImportLineEntity>> GetLinesAsync(string signUpId) =>
        Task.FromResult<IReadOnlyList<TripImportLineEntity>>(Lines.Values.Where(l => l.SignUpId == signUpId).ToList());

    public Task<IReadOnlyList<TripImportLineEntity>> GetAllLinesAsync() => Task.FromResult<IReadOnlyList<TripImportLineEntity>>(Lines.Values.ToList());

    public Task<TripImportLineEntity?> GetLineAsync(string signUpId, string entryId) => Task.FromResult(Lines.GetValueOrDefault((signUpId, entryId)));

    public Task SaveLineAsync(TripImportLineEntity line)
    {
        Lines[(line.SignUpId, line.EntryId)] = line;
        return Task.CompletedTask;
    }

    public Task<TripPaymentEntity?> GetPaymentAsync(string signUpId, string paymentId) => Task.FromResult(Payments.GetValueOrDefault((signUpId, paymentId)));

    public Task AddPaymentAsync(TripPaymentEntity payment)
    {
        Payments[(payment.SignUpId, payment.PaymentId)] = payment;
        return Task.CompletedTask;
    }
}

public class SignInAppServiceTests
{
    private readonly InMemorySignInAppRepository _repository = new();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IScoutAccountService _accounts = Substitute.For<IScoutAccountService>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly Dictionary<string, InvoiceEntity> _issued = [];
    private int _nextInvoice;

    private static readonly List<ScoutEntity> Scouts =
    [
        new() { RowKey = "s-sam", FullName = "Sam O'Brien" },
        new() { RowKey = "s-alex1", FullName = "Alex Kim" },
        new() { RowKey = "s-alex2", FullName = "Alex Kim" },
        new() { RowKey = "s-jo", FullName = "Jo Park" },
    ];

    private static readonly List<GuardianEntity> Guardians = [new() { RowKey = "pat@x", GuardianId = "g-pat", FullName = "Pat Lee" }];

    public SignInAppServiceTests()
    {
        _scouts.GetAllAsync().Returns(Scouts);
        _scouts.GetByIdAsync(Arg.Any<string>()).Returns(c => Scouts.FirstOrDefault(s => s.ScoutId == c.Arg<string>()));
        _guardians.GetAllAsync().Returns(Guardians);
        _guardians.GetByIdAsync(Arg.Any<string>()).Returns(c => Guardians.FirstOrDefault(g => g.GuardianId == c.Arg<string>()));
        _invoices.GetByIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(c => _issued.GetValueOrDefault(c.ArgAt<string>(1)));
        _accounts.IssueInvoiceAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => Issue(c.ArgAt<string>(0), c.ArgAt<decimal>(1)));
        _accounts.IssueGuardianInvoiceAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => Issue(c.ArgAt<string>(0), c.ArgAt<decimal>(1)));
        _accounts.When(a => a.VoidInvoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
            .Do(c => _issued[c.ArgAt<string>(1)].StatusValue = InvoiceStatus.Voided);
    }

    private InvoiceEntity Issue(string ownerId, decimal amount)
    {
        var id = $"inv{++_nextInvoice}";
        return _issued[id] = new InvoiceEntity
        {
            PartitionKey = ownerId, RowKey = id, InvoiceId = id, TotalAmount = amount, AmountDueFromFamily = amount, StatusValue = InvoiceStatus.Outstanding,
        };
    }

    private SignInAppService Service() =>
        new(_repository, _scouts, _guardians, _invoices, _accounts, _audit, new PassThroughUnitOfWork(), TimeProvider.System);

    private async Task<SignInAppService> WithRosterAsync()
    {
        var service = Service();
        await service.ReceiveRosterAsync(
        [
            new("p-sam", "Sam OBrien", SignInPersonType.Scout, true),
            new("p-alex", "Alex Kim", SignInPersonType.Scout, true),
            new("p-jo", "Jo Park", SignInPersonType.Scout, true),
            new("p-pat", "Pat Lee", SignInPersonType.Adult, true),
        ]);
        return service;
    }

    private static TripUpload Trip(params TripUploadLine[] lines) =>
        new("Winter Campout", "Fri, Dec 4 – Sun, Dec 6", new DateOnly(2026, 12, 4), new DateOnly(2026, 12, 6), new DateOnly(2026, 11, 27), "Pat Lee", lines);

    private static TripUploadLine Line(string entryId, string name, string? personId, decimal amount, string type = SignInPersonType.Scout) =>
        new(entryId, name, personId, type, amount, string.Empty);

    // ---- Name suggestions

    [Fact]
    public void A_unique_match_after_ignoring_case_and_punctuation_is_suggested() =>
        Assert.Equal("s-sam", NameMatch.Suggest("sam  obrien", Scouts.Select(s => (s.ScoutId, s.FullName)), new HashSet<string>()));

    [Fact]
    public void Two_people_with_the_same_name_get_no_suggestion() =>
        Assert.Null(NameMatch.Suggest("Alex Kim", Scouts.Select(s => (s.ScoutId, s.FullName)), new HashSet<string>()));

    [Fact]
    public void Someone_already_linked_isnt_suggested_again() =>
        Assert.Null(NameMatch.Suggest("Jo Park", Scouts.Select(s => (s.ScoutId, s.FullName)), new HashSet<string> { "s-jo" }));

    [Fact]
    public async Task The_links_page_suggests_matches_and_counts_who_still_needs_linking()
    {
        var service = await WithRosterAsync();

        var rows = (await service.GetPeopleAsync()).ToDictionary(r => r.Person.PersonId);

        Assert.Equal("s-sam", rows["p-sam"].SuggestedId);
        Assert.Null(rows["p-alex"].SuggestedId);
        Assert.Equal("g-pat", rows["p-pat"].SuggestedId);
        Assert.Equal(4, await service.CountUnlinkedAsync());
    }

    // ---- Links

    [Fact]
    public async Task One_finance_scout_cant_be_linked_to_two_sign_in_people()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync("p-jo", "s-sam", notBilled: false, "Treasurer"));

        Assert.Contains("already linked to Sam OBrien", refused.Message);
        Assert.Equal(3, await service.CountUnlinkedAsync());
    }

    [Fact]
    public async Task A_rename_after_linking_keeps_the_link_and_the_trip_still_matches()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");

        await service.ReceiveRosterAsync([new("p-sam", "Samuel O'Brien", SignInPersonType.Scout, true)]);
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Samuel O'Brien", "p-sam", 40)));

        var line = Assert.Single((await service.GetReviewAsync("t1"))!.Lines);
        Assert.Equal(("s-sam", TripLineAction.Issue), (line.MatchId, line.Action));
    }

    [Fact]
    public async Task Linking_and_not_billing_are_in_the_activity_history()
    {
        var service = await WithRosterAsync();

        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.LinkAsync("p-jo", null, notBilled: true, "Treasurer");

        await _audit.Received(1).AddAsync(FinanceAuditEntityType.SignInLink, "p-sam", "Linked", "Treasurer", Arg.Any<string?>(), Arg.Any<string?>());
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.SignInLink, "p-jo", "Marked not billed", "Treasurer", Arg.Any<string?>(), Arg.Any<string?>());
    }

    // ---- Reviewing and applying a trip

    [Fact]
    public async Task Nothing_is_invoiced_until_the_treasurer_applies_the_trip()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");

        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40)));

        await _accounts.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default!, default, default, default, default!, default!);
        Assert.Equal(1, (await service.GetReviewAsync("t1"))!.Changes);
    }

    [Fact]
    public async Task Applying_issues_a_scout_invoice_and_a_guardian_invoice_for_an_adult()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.LinkAsync("p-pat", "g-pat", notBilled: false, "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40), Line("e2", "Pat Lee", "p-pat", 15, SignInPersonType.Adult)));

        Assert.Equal(2, await service.ApplyAsync("t1", "Treasurer"));

        await _accounts.Received(1).IssueInvoiceAsync("s-sam", 40, 0, Arg.Any<DateTimeOffset>(), "Winter Campout (Fri, Dec 4 – Sun, Dec 6)", "Treasurer");
        await _accounts.Received(1).IssueGuardianInvoiceAsync("g-pat", 15, Arg.Any<DateTimeOffset>(), Arg.Any<string>(), "Treasurer");
        Assert.All((await service.GetReviewAsync("t1"))!.Lines, l => Assert.Equal(TripLineAction.UpToDate, l.Action));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.TripImport, "t1", "Invoices updated", "Treasurer", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Sending_again_shows_whos_new_whose_cost_changed_and_who_dropped_out()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.LinkAsync("p-jo", "s-jo", notBilled: false, "Treasurer");
        await service.LinkAsync("p-pat", "g-pat", notBilled: false, "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40), Line("e2", "Jo Park", "p-jo", 40)));
        await service.ApplyAsync("t1", "Treasurer");

        // Jo drops out, Sam's cost changes, and Pat joins.
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 45), Line("e3", "Pat Lee", "p-pat", 15, SignInPersonType.Adult)));

        var actions = (await service.GetReviewAsync("t1"))!.Lines.ToDictionary(l => l.Line.EntryId, l => l.Action);
        Assert.Equal(TripLineAction.Reissue, actions["e1"]);
        Assert.Equal(TripLineAction.Void, actions["e2"]);
        Assert.Equal(TripLineAction.Issue, actions["e3"]);
    }

    [Fact]
    public async Task A_change_after_money_was_paid_is_left_for_the_treasurer()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40)));
        await service.ApplyAsync("t1", "Treasurer");
        _issued.Values.Single().AmountDueFromFamily = 20;

        await service.ReceiveTripAsync("t1", Trip());

        var line = Assert.Single((await service.GetReviewAsync("t1"))!.Lines);
        Assert.Equal(TripLineAction.NeedsAttention, line.Action);
        Assert.Equal(0, await service.ApplyAsync("t1", "Treasurer"));
        await _accounts.DidNotReceiveWithAnyArgs().VoidInvoiceAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task A_typed_in_name_waits_for_a_match_by_hand_which_is_recorded()
    {
        var service = await WithRosterAsync();
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Jo P.", null, 40)));
        Assert.Equal(TripLineAction.NeedsMatch, Assert.Single((await service.GetReviewAsync("t1"))!.Lines).Action);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetManualMatchAsync("t1", "e1", "nobody", "Treasurer"));
        await service.SetManualMatchAsync("t1", "e1", "s-jo", "Treasurer");

        var line = Assert.Single((await service.GetReviewAsync("t1"))!.Lines);
        Assert.Equal(("s-jo", true, TripLineAction.Issue), (line.MatchId, line.MatchIsManual, line.Action));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.TripImport, "t1", "Matched by hand", "Treasurer", Arg.Any<string?>(), Arg.Any<string?>());
        // A one-off: it never becomes a roster link.
        Assert.DoesNotContain(_repository.People.Values, p => p.LinkedScoutId == "s-jo");
    }

    [Fact]
    public async Task Trips_with_work_waiting_are_counted_for_the_dashboard()
    {
        var service = await WithRosterAsync();
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40)));

        Assert.Equal(1, await service.CountTripsNeedingReviewAsync());
    }

    // ---- Payments

    [Fact]
    public async Task A_payment_before_the_invoice_is_issued_is_turned_back()
    {
        var service = await WithRosterAsync();
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40)));

        var result = await service.RecordPaymentAsync("t1", new TripPaymentUpload("pay1", "e1", 20, InvoicePaymentMethod.Cash, null, "Pat Lee"));

        Assert.Equal(TripPaymentOutcome.NotInvoiced, result.Outcome);
    }

    [Fact]
    public async Task The_same_payment_sent_twice_is_recorded_once()
    {
        var service = await WithRosterAsync();
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Line("e1", "Sam OBrien", "p-sam", 40)));
        await service.ApplyAsync("t1", "Treasurer");
        var payment = new TripPaymentUpload("pay1", "e1", 20, InvoicePaymentMethod.Check, "1234", "Pat Lee (trip sign-up)");

        Assert.Equal(TripPaymentOutcome.Recorded, (await service.RecordPaymentAsync("t1", payment)).Outcome);
        Assert.Equal(TripPaymentOutcome.AlreadyRecorded, (await service.RecordPaymentAsync("t1", payment)).Outcome);

        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv1", 20, InvoicePaymentMethod.Check, "1234", "Pat Lee (trip sign-up)");
    }

    [Fact]
    public void The_api_key_is_checked_as_a_bearer_token()
    {
        const string key = "0123456789abcdef0123456789abcdef";

        Assert.True(SignInAppApi.IsAuthorized($"Bearer {key}", key));
        Assert.False(SignInAppApi.IsAuthorized($"Bearer {key}x", key));
        Assert.False(SignInAppApi.IsAuthorized(key, key));
        Assert.False(SignInAppApi.IsAuthorized(null, key));
    }
}
