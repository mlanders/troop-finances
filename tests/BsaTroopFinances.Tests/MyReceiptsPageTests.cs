using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Components.Pages.Portal;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class MyReceiptsPageTests : AdminPageTestBase
{
    private readonly IReimbursementRequestRepository _requests;

    public MyReceiptsPageTests()
    {
        ComponentFactories.AddStub<AntiforgeryToken>();
        var auth = AddAuthorization();
        auth.SetAuthorized("Maria Rivera");
        auth.SetClaims(new Claim("GuardianId", "maria"));

        _requests = Fake<IReimbursementRequestRepository>();
        Has(pending: 2, approved: 0, paid: 23, rejected: 1);
        _requests.GetSubmitterPageAsync("maria", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<ReimbursementRequestEntity>(
            [
                Request("r4", "Tent repair parts", ReimbursementStatus.Pending, day: 20),
                Request("r2", "Campout groceries", ReimbursementStatus.Pending, day: 5),
            ], 2));
    }

    private void Has(int pending, int approved, int paid, int rejected) =>
        _requests.GetSubmitterStatusCountsAsync("maria").Returns(new Dictionary<string, int>
        {
            [ReimbursementStatus.Pending] = pending, [ReimbursementStatus.Approved] = approved,
            [ReimbursementStatus.Paid] = paid, [ReimbursementStatus.Rejected] = rejected,
        }.Where(c => c.Value > 0).ToDictionary());

    private static ReimbursementRequestEntity Request(string id, string description, string status, int day) => new()
    {
        RequestId = id, RowKey = id, PartitionKey = "maria", SubmitterGuardianId = "maria", Description = description, StatusValue = status,
        AmountRequested = 10m, ReceiptContentType = "application/pdf", SubmittedUtc = new DateTimeOffset(2026, 9, day, 18, 0, 0, TimeSpan.Zero),
    };

    private IRenderedComponent<MyReceipts> RenderAt(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<MyReceipts>();
    }

    [Fact]
    public void Opens_on_the_first_page_of_pending_with_a_count_on_every_status()
    {
        var page = Render<MyReceipts>();

        _requests.Received(1).GetSubmitterPageAsync("maria", ReimbursementStatus.Pending, 1, 10);
        Assert.Equal(2, page.FindAll(".reimbursement-summary").Count);
        var filters = page.FindAll(".filter-bar a.choice-btn");
        Assert.Equal(["Pending (2)", "Approved (0)", "Paid (23)", "Rejected (1)", "All (26)"], filters.Select(a => a.TextContent));
        Assert.Equal("Pending (2)", page.Find(".filter-bar a.selected").TextContent);
        Assert.Equal("/portal/receipts?status=Paid", filters[2].GetAttribute("href"));
        Assert.Empty(page.FindAll("nav.row-actions"));       // 10 or fewer: no paging
    }

    [Fact]
    public void More_than_ten_in_a_status_are_paged_with_links()
    {
        var page = RenderAt("/portal/receipts?status=Paid&page=2");

        _requests.Received(1).GetSubmitterPageAsync("maria", ReimbursementStatus.Paid, 2, 10);
        Assert.Contains("Page 2 of 3 (23 receipts)", page.Find("nav.row-actions").TextContent);
        Assert.Equal(["/portal/receipts?status=Paid", "/portal/receipts?status=Paid&page=3"],
            page.FindAll("nav.row-actions a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void All_statuses_are_asked_for_without_a_status_filter()
    {
        var page = RenderAt("/portal/receipts?status=All");

        _requests.Received(1).GetSubmitterPageAsync("maria", null, 1, 10);
        Assert.Equal(["/portal/receipts?status=All&page=2"], page.FindAll("nav.row-actions a").Select(a => a.GetAttribute("href")));
    }

    [Theory]
    [InlineData("/portal/receipts?status=nonsense", ReimbursementStatus.Pending, 1)]   // unknown status: Pending
    [InlineData("/portal/receipts?status=Paid&page=99", ReimbursementStatus.Paid, 3)]  // past the end: last page
    [InlineData("/portal/receipts?page=0", ReimbursementStatus.Pending, 1)]
    public void A_bad_address_falls_back_to_something_sensible(string address, string status, int pageNumber)
    {
        RenderAt(address);

        _requests.Received(1).GetSubmitterPageAsync("maria", status, pageNumber, 10);
    }

    [Fact]
    public void An_empty_status_says_so_and_no_receipts_at_all_offers_to_submit_one()
    {
        _requests.GetSubmitterPageAsync("maria", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<ReimbursementRequestEntity>([], 0));
        Assert.Contains("No approved receipts.", RenderAt("/portal/receipts?status=Approved").Markup);

        Has(0, 0, 0, 0);
        Assert.Contains("You haven't submitted any receipts yet.", RenderAt("/portal/receipts").Markup);
    }
}
