using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using BsaTroopFinances.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using AdminIndex = BsaTroopFinances.Components.Pages.Admin.Index;

namespace BsaTroopFinances.Tests;

/// <summary>The dashboard, with the meeting sign-in app never used and with work from it waiting.</summary>
public class DashboardPageTests : AdminPageTestBase
{
    private readonly InMemorySignInAppRepository _signIn = new();

    public DashboardPageTests()
    {
        Fake<IReportingService>().GetSummaryAsync().Returns(new ReportSummaryModel(
            2, 85m, 3, 410m, [], [], new DonationSummary(0, 0, 0, 0, 0, 0, 0)));
        var accounts = Fake<ITroopAccountService>();
        accounts.GetSummariesAsync().Returns([]);
        Fake<IFundraiserEventRepository>().GetAllAsync().Returns([]);
        var scouts = Fake<IScoutRepository>();
        scouts.GetActiveAsync().Returns([new ScoutEntity { RowKey = "s-sam", FullName = "Sam Patel" }]);
        scouts.GetAllAsync().Returns([new ScoutEntity { RowKey = "s-sam", FullName = "Sam Patel" }]);
        var guardians = Fake<IGuardianRepository>();
        guardians.GetAllAsync().Returns([]);
        Fake<IFinanceAuditRepository>().GetRecentAsync(Arg.Any<int>()).Returns([]);
        Services.AddSingleton(new SignInAppService(_signIn, scouts, guardians, Substitute.For<IInvoiceRepository>(),
            Substitute.For<IScoutAccountService>(), Substitute.For<IFinanceAuditRepository>(), new PassThroughUnitOfWork(), TimeProvider.System));
    }

    [Fact]
    public void With_the_sign_in_app_never_used_the_dashboard_shows_the_finances_and_no_sign_in_notice()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<AdminIndex>();

        page.WaitForAssertion(() => Assert.DoesNotContain("Loading", page.Markup));
        Assert.Contains("$410.00", page.Markup);
        Assert.Empty(page.FindAll(".dashboard-signin-alert"));
        Assert.DoesNotContain("not linked", page.Markup);
        Assert.DoesNotContain("to review", page.Markup);
    }

    [Fact]
    public void People_to_link_and_trips_to_review_are_shown_with_links_to_their_pages()
    {
        SignInAs(AdminRole.Reader);
        _signIn.People["p-sam"] = new SignInPersonEntity
        {
            PartitionKey = SignInPersonEntity.PartitionKeyValue, RowKey = "p-sam", FullName = "Sam Patel", PersonType = SignInPersonType.Scout, IsActiveInSignIn = true,
        };
        _signIn.People["p-jo"] = new SignInPersonEntity
        {
            PartitionKey = SignInPersonEntity.PartitionKeyValue, RowKey = "p-jo", FullName = "Jo Park", PersonType = SignInPersonType.Scout, IsActiveInSignIn = true,
        };
        _signIn.Trips["t1"] = new TripImportEntity
        {
            PartitionKey = TripImportEntity.PartitionKeyValue, RowKey = "t1", Title = "Campout",
            StartDate = new DateTimeOffset(2099, 12, 4, 0, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2099, 12, 6, 0, 0, 0, TimeSpan.Zero),
        };
        _signIn.Lines[("t1", "e1")] = new TripImportLineEntity { PartitionKey = "t1", RowKey = "e1", Name = "Jo P.", Amount = 40 };

        var page = Render<AdminIndex>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".dashboard-signin-alert")));
        var links = page.FindAll(".dashboard-signin-alert a").ToDictionary(a => a.GetAttribute("href")!, a => a.TextContent.Trim());
        Assert.Equal("2 sign-in app people not linked", links["/admin/families/signin-links"]);
        Assert.Equal("1 trip to review", links["/admin/invoices/trips"]);
    }

    [Fact]
    public void Once_everyone_is_linked_and_nothing_is_waiting_the_notice_goes_away()
    {
        SignInAs(AdminRole.Writer);
        _signIn.People["p-sam"] = new SignInPersonEntity
        {
            PartitionKey = SignInPersonEntity.PartitionKeyValue, RowKey = "p-sam", FullName = "Sam Patel", PersonType = SignInPersonType.Scout,
            IsActiveInSignIn = true, LinkedScoutId = "s-sam",
        };

        var page = Render<AdminIndex>();

        page.WaitForAssertion(() => Assert.DoesNotContain("Loading", page.Markup));
        Assert.Empty(page.FindAll(".dashboard-signin-alert"));
    }
}
