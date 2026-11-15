using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class ScoutsPageTests : AdminPageTestBase
{
    private readonly IScoutRepository _scouts;
    private readonly ScoutEntity _alex = new() { RowKey = "scout1", FullName = "Alex Rivera", CachedBalance = 40m, IsActive = true };

    public ScoutsPageTests()
    {
        _scouts = Fake<IScoutRepository>();
        _scouts.GetAllAsync().Returns([_alex]);
        _invoices = Fake<IInvoiceRepository>();
        _invoices.GetOutstandingForScoutsAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns(
        [
            new InvoiceEntity { ScoutId = "scout1", StatusValue = InvoiceStatus.Outstanding, AmountDueFromFamily = 150m },
            new InvoiceEntity { ScoutId = "scout1", StatusValue = InvoiceStatus.Outstanding, AmountDueFromFamily = 25m },
        ]);
    }

    private readonly IInvoiceRepository _invoices;

    [Fact]
    public void The_scout_account_and_what_the_family_owes_are_separate_columns()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<Scouts>();

        Assert.Equal(["Name", "In scout account", "Family owes", "Status", ""], page.FindAll("thead th").Select(h => h.TextContent.Trim()));
        var cells = page.FindAll("tbody tr td").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(["$40.00", "$175.00"], cells.Skip(1).Take(2));
    }

    [Fact]
    public void Writer_gets_statement_and_deactivate_in_the_row_menu()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Scouts>();

        var name = page.Find("tbody td:first-child a");
        Assert.Equal("Alex Rivera", name.TextContent);
        Assert.Equal("/admin/scouts/scout1", name.GetAttribute("href"));

        var items = page.FindAll("tbody .ellipsis-menu-panel .ellipsis-menu-item");
        Assert.Equal(["Statement", "Deactivate"], items.Select(i => i.TextContent.Trim()));
        Assert.Equal("/admin/scouts/scout1", items[0].GetAttribute("href"));
        Assert.Empty(page.FindAll("tbody .btn"));
    }

    [Fact]
    public void Deactivate_with_money_on_the_account_opens_the_statement_to_decide_what_happens_to_it()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Scouts>();
        page.Find("tbody button.ellipsis-menu-item").Click();

        _scouts.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        Assert.EndsWith("/admin/scouts/scout1?deactivate=true", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Deactivate_with_nothing_on_the_account_saves_the_scout_as_inactive()
    {
        _alex.CachedBalance = 0;
        SignInAs(AdminRole.Writer);

        var page = Render<Scouts>();
        page.Find("tbody button.ellipsis-menu-item").Click();

        _scouts.Received(1).UpdateAsync(Arg.Is<ScoutEntity>(s => s.ScoutId == "scout1" && !s.IsActive));

        // The roster shows active scouts by default, so Alex drops off it; the Inactive filter
        // shows them with Reactivate instead.
        Assert.Contains("No scouts match this filter.", page.Markup);
        page.FindAll(".choice-btn").Single(b => b.TextContent.Trim() == "Inactive").Click();
        Assert.Equal("Reactivate", page.Find("tbody button.ellipsis-menu-item").TextContent.Trim());
    }

    [Fact]
    public void Reader_gets_a_plain_statement_link_and_no_menu()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<Scouts>();

        Assert.Empty(page.FindAll(".ellipsis-menu"));
        Assert.Equal("/admin/scouts/scout1", page.Find("tbody a.btn").GetAttribute("href"));
    }
}
