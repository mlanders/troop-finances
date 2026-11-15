using Bunit;
using Bunit.TestDoubles;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class ReimbursementsPageTests : AdminPageTestBase
{
    private readonly IReimbursementRequestRepository _requests;
    private readonly BunitPersistentComponentState _state;
    private readonly IListViewRepository _views;
    private readonly IFinanceAuditRepository _audit;

    public ReimbursementsPageTests()
    {
        _state = AddBunitPersistentComponentState();
        _views = Fake<IListViewRepository>();
        _views.GetForListAsync(Arg.Any<string>()).Returns(new List<ListViewEntity>());
        _audit = Fake<IFinanceAuditRepository>();
        _requests = Fake<IReimbursementRequestRepository>();
        _requests.GetPageAsync(Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new PagedResult<ReimbursementRequestEntity>(new List<ReimbursementRequestEntity>
        {
            new()
            {
                RequestId = "req1",
                SubmitterGuardianId = "maria",
                SubmitterName = "Maria Rivera",
                SubmitterEmail = "maria@example.com",
                ScoutName = "Alex Rivera",
                Description = "Campout groceries for the fall patrol overnighter at the lake, including ice and propane",
                AmountRequested = 42.18m,
                StatusValue = ReimbursementStatus.Pending,
                SubmittedUtc = new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero),
            },
        }, 1));
    }

    [Fact]
    public void Writer_sees_the_edit_pencil_beside_the_status_instead_of_a_review_button()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Reimbursements>();

        Assert.Equal(["Submitted", "From", "Description", "Amount", "Status"], page.FindAll("thead th").Select(th => th.TextContent));
        var link = page.Find("td .status-cell a.icon-link");
        Assert.Equal("/admin/reimbursements/req1?guardianId=maria", link.GetAttribute("href"));
        Assert.NotNull(link.QuerySelector(".fa-pen"));
        Assert.DoesNotContain("Review", page.Find("tbody").TextContent);
    }

    [Fact]
    public void Description_is_cut_to_one_line_with_the_scout_underneath_in_gray()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Reimbursements>();

        var cell = page.FindAll("tbody td")[2];
        var description = cell.QuerySelector(".text-truncate")!;
        Assert.StartsWith("Campout groceries", description.TextContent);
        Assert.Equal(description.TextContent, description.GetAttribute("title"));   // full text on hover
        Assert.Equal("Alex Rivera", cell.QuerySelector(".hint")!.TextContent);
    }

    [Fact]
    public void Reader_sees_the_view_eye()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<Reimbursements>();

        var link = page.Find("td .status-cell a.icon-link");
        Assert.NotNull(link.QuerySelector(".fa-eye"));
        Assert.Null(link.QuerySelector(".fa-pen"));
    }

    [Fact]
    public void First_page_from_prerender_is_reused_and_later_pages_are_fetched_on_demand()
    {
        SignInAs(AdminRole.Writer);
        _state.Persist("reimbursements-first-page", new PagedResult<ReimbursementRequestEntity>(new List<ReimbursementRequestEntity>
        {
            new() { RequestId = "req9", SubmitterGuardianId = "sam", SubmitterName = "Sam Lee", Description = "Prerendered request", StatusValue = ReimbursementStatus.Pending },
        }, 30));

        var page = Render<Reimbursements>();

        Assert.Contains("Prerendered request", page.Find("tbody").TextContent);
        _requests.DidNotReceiveWithAnyArgs().GetPageAsync(default, default, default, default, default);

        page.FindAll(".row-actions button").Single(b => b.TextContent == "Next").Click();

        _requests.Received(1).GetPageAsync(ReimbursementStatus.Pending, null, null, 2, 25);
    }

    [Fact]
    public void Typing_a_scout_or_guardian_name_asks_the_database_for_matching_requests_from_page_one()
    {
        SignInAs(AdminRole.Writer);
        _requests.GetPageAsync(ReimbursementStatus.Pending, null, null, 1, 25, "riv")
            .Returns(new PagedResult<ReimbursementRequestEntity>([new()
            {
                RequestId = "req2", SubmitterGuardianId = "maria", SubmitterName = "Maria Rivera", SubmitterEmail = "maria@example.com",
                Description = "Rivera family tent stakes", AmountRequested = 9.99m, StatusValue = ReimbursementStatus.Pending,
                SubmittedUtc = new DateTimeOffset(2026, 10, 2, 17, 0, 0, TimeSpan.Zero),
            }], 1));
        var page = Render<Reimbursements>();

        page.Find(".filter-toggle").Click();
        page.Find("#nameSearch").Input(" riv ");

        _requests.Received(1).GetPageAsync(ReimbursementStatus.Pending, null, null, 1, 25, "riv");
        Assert.Contains("Rivera family tent stakes", page.Find("tbody").TextContent);
        Assert.Equal("search", page.Find("#nameSearch").GetAttribute("type"));
    }

    [Fact]
    public void A_description_search_narrows_the_list_and_views_are_for_reimbursements()
    {
        SignInAs(AdminRole.Reader);
        var page = Render<Reimbursements>();

        page.Find(".filter-toggle").Click();
        page.Find("#descriptionSearch").Input("propane");

        _requests.Received(1).GetPageAsync(ReimbursementStatus.Pending, null, null, 1, 25, null, "propane");
        Assert.Contains("Submitted", page.Find(".filter-panel").TextContent);
        _views.Received().GetForListAsync(ListViewEntity.Reimbursements);
    }

    [Fact]
    public void Prerender_loads_one_page_and_hands_it_on()
    {
        SignInAs(AdminRole.Writer);

        Render<Reimbursements>();
        _state.TriggerOnPersisting();

        _requests.Received(1).GetPageAsync(ReimbursementStatus.Pending, null, null, 1, 25);
        Assert.True(_state.TryTake<PagedResult<ReimbursementRequestEntity>>("reimbursements-first-page", out var handedOn));
        Assert.Equal("req1", Assert.Single(handedOn!.Items).RequestId);
    }
}
