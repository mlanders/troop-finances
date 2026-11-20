using Bunit;
using BsaTroopFinances.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BsaTroopFinances.Tests;

/// <summary>Which header section and tab each admin page belongs to.</summary>
public class AdminSectionsTests
{
    [Theory]
    [InlineData("/admin", "Dashboard", "Dashboard")]
    [InlineData("admin/", "Dashboard", "Dashboard")]
    [InlineData("/admin/reimbursements/abc?guardianId=x", "Reimbursements", "Reimbursements")]
    [InlineData("/admin/invoices", "Invoices", "Invoices")]
    [InlineData("/admin/invoices/abc/void", "Invoices", "Invoices")]
    [InlineData("/admin/invoices/family-payment?guardianId=x", "Invoices", "Record family payment")]
    [InlineData("/admin/invoices/bulk", "Invoices", "Invoice all active scouts")]
    [InlineData("/admin/invoices/trips/abc", "Invoices", "Trip sign-ups")]
    [InlineData("/admin/families/signin-links", "Families", "Sign-in app links")]
    [InlineData("/admin/donations/abc/credit", "Fundraising", "Donations")]
    [InlineData("/admin/fundraisers/new", "Fundraising", "Fundraisers")]
    [InlineData("/admin/scouts/abc", "Families", "Scouts")]
    [InlineData("/admin/guardians/bulk", "Families", "Guardians")]
    [InlineData("/admin/families?g=x", "Families", "Families")]
    [InlineData("/admin/bank", "Bank", "Accounts")]
    [InlineData("/admin/bank/accounts/abc", "Bank", "Accounts")]
    [InlineData("/admin/bank/entries/new?kind=Expense", "Bank", "Accounts")]
    [InlineData("/admin/bank/deposits/abc", "Bank", "Deposits")]
    [InlineData("/admin/bank/reconcile?account=abc", "Bank", "Reconcile")]
    [InlineData("/admin/reports/troop", "Reports", "Troop finances")]
    [InlineData("/admin/reports/monthly", "Reports", "Monthly")]
    [InlineData("/admin/settings/activity", "Reports", "Activity history")]
    public void Each_page_belongs_to_one_section_and_tab(string path, string section, string tab)
    {
        var found = AdminSections.Find(path);

        Assert.Equal((section, tab), (found!.Value.Section.Label, found.Value.Tab.Label));
    }

    [Theory]
    [InlineData("/admin/settings")]
    [InlineData("/admin/settings/access")]
    [InlineData("/admin/reportsx")]
    [InlineData("/portal")]
    public void Pages_outside_the_sections_belong_to_none(string path)
    {
        Assert.Null(AdminSections.Find(path));
    }
}

/// <summary>The tab row drawn above each admin page.</summary>
public class SectionTabsTests : AdminPageTestBase
{
    private IRenderedComponent<SectionTabs> RenderAt(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<SectionTabs>();
    }

    [Fact]
    public void The_current_sections_pages_are_tabs_with_the_current_one_marked()
    {
        SignInAs(AdminRole.Reader);

        var tabs = RenderAt("/admin/scouts/abc").FindAll(".section-tab");

        Assert.Equal(["Families", "Scouts", "Guardians", "Sign-in app links"], tabs.Select(t => t.TextContent));
        Assert.Equal(["/admin/families", "/admin/scouts", "/admin/guardians", "/admin/families/signin-links"], tabs.Select(t => t.GetAttribute("href")));
        Assert.Equal("page", tabs[1].GetAttribute("aria-current"));
        Assert.Null(tabs[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Editors_see_the_editor_only_tabs()
    {
        SignInAs(AdminRole.Writer);

        Assert.Equal(["Invoices", "Record family payment", "Invoice all active scouts", "Trip sign-ups"],
            RenderAt("/admin/invoices").FindAll(".section-tab").Select(t => t.TextContent));
    }

    [Theory]
    [InlineData("/admin/invoices")]        // a viewer can only open the one tab
    [InlineData("/admin/reimbursements")]  // a one-page section
    [InlineData("/admin/settings")]        // outside the sections
    public void The_bar_stays_but_holds_no_tabs_when_there_is_only_one_page_to_go_to(string path)
    {
        SignInAs(AdminRole.Reader);

        var bar = RenderAt(path);

        // Still drawn, at the same height, so the page heading below doesn't move.
        Assert.Single(bar.FindAll(".section-tabs-bar"));
        Assert.Empty(bar.FindAll(".section-tab"));
    }
}
