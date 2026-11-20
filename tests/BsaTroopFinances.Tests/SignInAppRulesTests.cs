using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The sign-in app's roster, link and trip rules, and the API key's length rule.</summary>
public class SignInAppRulesTests
{
    private readonly InMemorySignInAppRepository _repository = new();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();

    private static readonly List<ScoutEntity> Scouts = [new() { RowKey = "s-sam", FullName = "Sam Patel" }];
    private static readonly List<GuardianEntity> Guardians = [new() { RowKey = "pat@x", GuardianId = "g-pat", FullName = "Pat Lee" }];

    public SignInAppRulesTests()
    {
        _scouts.GetAllAsync().Returns(Scouts);
        _scouts.GetByIdAsync(Arg.Any<string>()).Returns(c => Scouts.FirstOrDefault(s => s.ScoutId == c.Arg<string>()));
        _guardians.GetAllAsync().Returns(Guardians);
        _guardians.GetByIdAsync(Arg.Any<string>()).Returns(c => Guardians.FirstOrDefault(g => g.GuardianId == c.Arg<string>()));
    }

    private SignInAppService Service() =>
        new(_repository, _scouts, _guardians, Substitute.For<IInvoiceRepository>(), Substitute.For<IScoutAccountService>(), _audit,
            new PassThroughUnitOfWork(), TimeProvider.System);

    private static TripUpload Trip(params TripUploadLine[] lines) =>
        new("Campout", "Dec 4 – 6", new DateOnly(2099, 12, 4), new DateOnly(2099, 12, 6), null, "Leader", lines);

    private static TripUploadLine Line(string entryId, string? personId, decimal amount, string type = SignInPersonType.Scout) =>
        new(entryId, entryId, personId, type, amount, "");

    private async Task<SignInAppService> WithLinkedSamAsync()
    {
        var service = Service();
        await service.ReceiveRosterAsync([new("p-sam", "Sam Patel", SignInPersonType.Scout, true)]);
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        return service;
    }

    // ---- Roster

    [Fact]
    public async Task Someone_no_longer_sent_is_marked_gone_but_keeps_their_link()
    {
        var service = await WithLinkedSamAsync();

        await service.ReceiveRosterAsync([]);

        var sam = _repository.People["p-sam"];
        Assert.Equal((false, "s-sam"), (sam.IsActiveInSignIn, sam.LinkedScoutId));
        Assert.Equal(0, await service.CountUnlinkedAsync());
    }

    [Fact]
    public async Task Changing_from_scout_to_adult_drops_the_link()
    {
        var service = await WithLinkedSamAsync();

        await service.ReceiveRosterAsync([new("p-sam", "Sam Patel", SignInPersonType.Adult, true)]);

        Assert.False(_repository.People["p-sam"].IsLinked);
    }

    [Fact]
    public async Task Bad_roster_rows_are_skipped()
    {
        await Service().ReceiveRosterAsync(
        [
            new("", "No Id", SignInPersonType.Scout, true),
            new("p1", "Odd Type", "Leader", true),
            new("p2", "Twice", SignInPersonType.Scout, true),
            new("p2", "Twice Again", SignInPersonType.Scout, true),
        ]);

        Assert.Equal(["p2"], _repository.People.Keys);
        Assert.Equal("Twice", _repository.People["p2"].FullName);
    }

    // ---- Links

    [Fact]
    public async Task Unlinking_is_audited_and_linking_someone_unknown_or_to_nobody_real_is_refused()
    {
        var service = await WithLinkedSamAsync();

        await service.LinkAsync("p-sam", null, notBilled: false, "Treasurer");
        Assert.False(_repository.People["p-sam"].IsLinked);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.SignInLink, "p-sam", "Unlinked", "Treasurer", Arg.Any<string?>(), Arg.Any<string?>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync("nobody", "s-sam", notBilled: false, "Treasurer"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync("p-sam", "s-ghost", notBilled: false, "Treasurer"));
    }

    [Fact]
    public async Task An_adult_is_linked_to_a_guardian_not_a_scout()
    {
        var service = Service();
        await service.ReceiveRosterAsync([new("p-pat", "Pat Lee", SignInPersonType.Adult, true)]);

        await service.LinkAsync("p-pat", "g-pat", notBilled: false, "Treasurer");

        Assert.Equal(("g-pat", null), (_repository.People["p-pat"].LinkedGuardianId, _repository.People["p-pat"].LinkedScoutId));
    }

    // ---- Trips

    [Fact]
    public async Task A_trip_with_no_id_a_negative_amount_or_a_bad_type_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveTripAsync("t1", Trip(Line("e1", null, -1))));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveTripAsync("t1", Trip(Line("e1", null, 10, "Leader"))));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveTripAsync(" ", Trip()));
        Assert.Empty(_repository.Trips);
    }

    [Fact]
    public async Task Invoices_fall_due_by_the_drop_out_day_or_else_the_start()
    {
        await Service().ReceiveTripAsync("t1", Trip() with { DueDate = new DateOnly(2099, 11, 27) });
        await Service().ReceiveTripAsync("t2", Trip());

        Assert.Equal(new DateTime(2099, 11, 27), _repository.Trips["t1"].DueDate.UtcDateTime.Date);
        Assert.Equal(new DateTime(2099, 12, 4), _repository.Trips["t2"].DueDate.UtcDateTime.Date);
    }

    // ---- The API key

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("too-short", false)]
    [InlineData("0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("  0123456789abcdef0123456789abcdef  ", true)]
    public void The_api_is_on_only_with_a_long_enough_key(string? key, bool on)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("SignInApp:ApiKey", key)]).Build();

        Assert.Equal(on, SignInAppApi.Key(config) is not null);
    }
}

/// <summary>Shared setup for the sign-in pages: one trip with a typed-in name, with or without the API key.</summary>
public abstract class SignInAppPageFixture : AdminPageTestBase
{
    private protected InMemorySignInAppRepository Repository { get; } = new();

    protected SignInAppPageFixture(bool withKey)
    {
        List<ScoutEntity> scouts = [new() { RowKey = "s-sam", FullName = "Sam Patel" }, new() { RowKey = "s-jo", FullName = "Jo Park" }];
        var scoutRepository = Fake<IScoutRepository>();
        scoutRepository.GetAllAsync().Returns(scouts);
        scoutRepository.GetByIdAsync(Arg.Any<string>()).Returns(c => scouts.FirstOrDefault(s => s.ScoutId == c.Arg<string>()));
        var guardianRepository = Fake<IGuardianRepository>();
        guardianRepository.GetAllAsync().Returns(new List<GuardianEntity>());
        var service = new SignInAppService(Repository, scoutRepository, guardianRepository, Fake<IInvoiceRepository>(),
            Fake<IScoutAccountService>(), Fake<IFinanceAuditRepository>(), new PassThroughUnitOfWork(), TimeProvider.System);
        Services.AddSingleton(service);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(withKey ? [new("SignInApp:ApiKey", "0123456789abcdef0123456789abcdef")] : []).Build());
        service.ReceiveTripAsync("t1", new TripUpload("Campout", "Dec 4 – 6", new DateOnly(2099, 12, 4), new DateOnly(2099, 12, 6), null, "Leader",
            [new("e1", "Jo P.", null, SignInPersonType.Scout, 40, "")])).GetAwaiter().GetResult();
    }
}

/// <summary>The trip page: matching by hand, what viewers see, and a trip that was never sent.</summary>
public sealed class TripImportDetailPageTests() : SignInAppPageFixture(withKey: true)
{
    [Fact]
    public void A_typed_in_name_can_be_matched_by_hand_from_the_trip_page()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = Render<TripImportDetail>(p => p.Add(x => x.Id, "t1"));

        page.Find("tbody select").Change("s-jo");

        Assert.Equal("s-jo", Repository.Lines[("t1", "e1")].ManualMatchId);
        Assert.Contains("Matched Jo P. for this trip.", page.Find(".alert-success").TextContent);
        Assert.Contains("Will issue", page.Markup);
    }

    [Fact]
    public void Viewers_see_the_trip_but_cant_match_or_issue()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<TripImportDetail>(p => p.Add(x => x.Id, "t1"));

        Assert.Empty(page.FindAll("select"));
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Issue"));
        Assert.Contains("Needs a match", page.Markup);
    }

    [Fact]
    public void A_trip_never_sent_says_so()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<TripImportDetail>(p => p.Add(x => x.Id, "nope"));

        Assert.Contains("hasn't been sent from the sign-in app", page.Markup);
    }
}

/// <summary>Both sign-in pages warn when SignInApp:ApiKey isn't set.</summary>
public sealed class SignInAppPagesWithoutKeyTests() : SignInAppPageFixture(withKey: false)
{
    [Fact]
    public void Both_pages_say_the_key_is_missing()
    {
        SignInAs(AdminRole.Writer);

        Assert.Contains("SignInApp:ApiKey", Render<SignInLinks>().Markup);
        Assert.Contains("SignInApp:ApiKey", Render<TripImports>().Markup);
    }
}
