using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>Families > Sign-in app links and Invoices > Trip sign-ups.</summary>
public class SignInAppPagesTests : AdminPageTestBase
{
    private readonly InMemorySignInAppRepository _repository = new();
    private readonly IScoutAccountService _accounts;
    private readonly IFinanceAuditRepository _audit;
    private readonly SignInAppService _service;

    public SignInAppPagesTests()
    {
        List<ScoutEntity> scouts = [new() { RowKey = "s-sam", FullName = "Sam Patel" }, new() { RowKey = "s-jo", FullName = "Jo Park" }];
        var scoutRepository = Fake<IScoutRepository>();
        scoutRepository.GetAllAsync().Returns(scouts);
        scoutRepository.GetByIdAsync(Arg.Any<string>()).Returns(c => scouts.FirstOrDefault(s => s.ScoutId == c.Arg<string>()));
        var guardianRepository = Fake<IGuardianRepository>();
        guardianRepository.GetAllAsync().Returns(new List<GuardianEntity>());
        _accounts = Fake<IScoutAccountService>();
        _accounts.IssueInvoiceAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => new InvoiceEntity { PartitionKey = c.ArgAt<string>(0), RowKey = "inv1", InvoiceId = "inv1", TotalAmount = c.ArgAt<decimal>(1) });
        _audit = Fake<IFinanceAuditRepository>();
        _service = new SignInAppService(_repository, scoutRepository, guardianRepository,
            Fake<IInvoiceRepository>(), _accounts, _audit, new PassThroughUnitOfWork(), TimeProvider.System);
        Services.AddSingleton(_service);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new("SignInApp:ApiKey", "0123456789abcdef0123456789abcdef")]).Build());
        _service.ReceiveRosterAsync([new("p-sam", "Sam Patel", SignInPersonType.Scout, true), new("p-new", "Riley New", SignInPersonType.Scout, true)])
            .GetAwaiter().GetResult();
    }

    [Fact]
    public void Unlinked_people_are_listed_with_their_suggested_match_picked()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<SignInLinks>();

        Assert.Equal(2, page.FindAll("tbody tr").Count);
        var sam = page.FindAll("tbody tr").Single(r => r.QuerySelector("td")!.TextContent.Contains("Sam Patel"));
        Assert.Equal("s-sam", sam.QuerySelector("select")!.GetAttribute("value"));
        Assert.Contains("Link all 1 suggested", page.Markup);
    }

    [Fact]
    public void Linking_moves_them_off_the_unlinked_list()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = Render<SignInLinks>();

        page.FindAll("tbody tr").Single(r => r.QuerySelector("td")!.TextContent.Contains("Sam Patel")).QuerySelectorAll("button").Single(b => b.TextContent == "Link").Click();

        Assert.Equal("s-sam", _repository.People["p-sam"].LinkedScoutId);
        Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("Linked Sam Patel.", page.Find(".alert-success").TextContent);
    }

    [Fact]
    public void Viewers_see_the_links_but_cant_change_them()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<SignInLinks>();

        Assert.Empty(page.FindAll("tbody button"));
        Assert.Empty(page.FindAll("tbody select"));
    }

    [Fact]
    public async Task A_trip_is_invoiced_only_after_the_treasurer_confirms()
    {
        await _service.LinkAsync("p-sam", "s-sam", notBilled: false, "Pat Treasurer");
        await _service.ReceiveTripAsync("t1", new TripUpload("Winter Campout", "Dec 4 – 6", new DateOnly(2026, 12, 4), new DateOnly(2026, 12, 6), null, "Leader",
            [new("e1", "Sam Patel", "p-sam", SignInPersonType.Scout, 40, "$40.00 fee"), new("e2", "Visiting Kid", null, SignInPersonType.Scout, 40, "")]));
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");

        var page = Render<TripImportDetail>(p => p.Add(x => x.Id, "t1"));

        Assert.Contains("Will issue", page.Markup);
        Assert.Contains("Needs a match", page.Markup);
        page.FindAll("button").Single(b => b.TextContent.Contains("Issue / update 1 invoice")).Click();
        await _accounts.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default!, default, default, default, default!, default!);

        page.FindAll("button").Single(b => b.TextContent.Contains("Yes, update the invoices")).Click();

        await _accounts.Received(1).IssueInvoiceAsync("s-sam", 40, 0, Arg.Any<DateTimeOffset>(), "Winter Campout (Dec 4 – 6)", "Pat Treasurer");
        Assert.Contains("Updated 1 invoice.", page.Find(".alert-success").TextContent);
    }

    [Fact]
    public async Task The_trip_list_shows_what_is_waiting()
    {
        await _service.ReceiveTripAsync("t1", new TripUpload("Winter Campout", "Dec 4 – 6", new DateOnly(2099, 12, 4), new DateOnly(2099, 12, 6), null, "Leader",
            [new("e1", "Sam Patel", "p-sam", SignInPersonType.Scout, 40, "")]));
        SignInAs(AdminRole.Writer);

        var page = Render<TripImports>();

        Assert.Contains("Winter Campout", page.Find("tbody a").TextContent);
        Assert.Contains("1 needs you", page.Markup);
    }
}
