using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class BulkInvoicesPageTests : AdminPageTestBase
{
    private readonly IScoutAccountService _accounts;

    public BulkInvoicesPageTests()
    {
        Fake<IScoutRepository>().GetActiveAsync().Returns(new List<ScoutEntity>
        {
            new() { RowKey = "sam", FullName = "Sam Patel" },
            new() { RowKey = "alex", FullName = "Alex Rivera" },
            new() { RowKey = "jordan", FullName = "Jordan Lee" },
        });
        _accounts = Fake<IScoutAccountService>();
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
    }

    private static List<string> Names(IRenderedComponent<BulkInvoices> page) =>
        page.FindAll(".check-list-item").Select(l => l.TextContent.Trim()).ToList();

    [Fact]
    public void Every_active_scout_is_listed_by_name_and_ticked_to_start_with()
    {
        var page = Render<BulkInvoices>();

        Assert.Equal(["Alex Rivera", "Jordan Lee", "Sam Patel"], Names(page));
        Assert.All(page.FindAll(".check-list input"), box => Assert.True(box.HasAttribute("checked")));
        Assert.Contains("Scouts to invoice (3 of 3)", page.Find("legend").TextContent);
        Assert.Contains("to 3 scouts", page.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void An_unticked_scout_is_left_out_of_the_invoices()
    {
        var page = Render<BulkInvoices>();

        page.FindAll(".check-list input")[1].Change(false);       // Jordan Lee
        Assert.Contains("to 2 scouts", page.Find("button.btn-primary").TextContent);
        page.Find("button.btn-primary").Click();

        _accounts.Received(1).IssueInvoiceAsync("alex", 120m, 0, Arg.Any<DateTimeOffset>(), "Yearly dues", "Pat Treasurer");
        _accounts.Received(1).IssueInvoiceAsync("sam", 120m, 0, Arg.Any<DateTimeOffset>(), "Yearly dues", "Pat Treasurer");
        _accounts.DidNotReceive().IssueInvoiceAsync("jordan", Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.Contains("to 2 scouts; 1 left out.", page.Find(".alert-success").TextContent);
    }

    [Fact]
    public void Select_none_blocks_issuing_and_select_all_brings_everyone_back()
    {
        var page = Render<BulkInvoices>();

        page.FindAll("fieldset button").Single(b => b.TextContent == "Select none").Click();
        Assert.True(page.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.Contains("(0 of 3)", page.Find("legend").TextContent);

        page.FindAll("fieldset button").Single(b => b.TextContent == "Select all").Click();
        Assert.False(page.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.All(page.FindAll(".check-list input"), box => Assert.True(box.HasAttribute("checked")));
    }
}
