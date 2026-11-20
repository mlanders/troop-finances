using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The Families page: picking families and the print links it builds for them.</summary>
public class FamiliesPageTests : AdminPageTestBase
{
    private static readonly string Today = DateTime.Now.ToString("yyyy-MM-dd");
    private static readonly string YearAgo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-12).ToString("yyyy-MM-dd");

    public FamiliesPageTests()
    {
        SignInAs(AdminRole.Reader);
        Fake<IFamilyStatusService>().GetFamiliesAsync(false).Returns(new List<FamilyRow>
        {
            Row("lee", "Lee Leader", [], funds: 0, owed: 320, adultCharges: true),
            Row("maria", "Luis Rivera and Maria Rivera", [Scout("alex", "Alex Rivera"), Scout("casey", "Casey Rivera", active: false)], funds: 95, owed: 0),
            Row("new", "New Parent", [], funds: 0, owed: 0),
        });
    }

    private static ScoutEntity Scout(string id, string name, bool active = true) => new() { RowKey = id, FullName = name, IsActive = active };

    private static FamilyRow Row(string id, string name, IReadOnlyList<ScoutEntity> scouts, decimal funds, decimal owed, bool adultCharges = false) =>
        new(new Household(name, [new GuardianEntity { GuardianId = id, FullName = name, IsActive = true }], scouts.Select(s => s.ScoutId).ToList()), scouts, funds, owed, adultCharges);

    private static IReadOnlyList<string> Ticked(IRenderedComponent<Families> page) =>
        page.FindAll("tbody tr").Where(r => r.QuerySelector("input")!.HasAttribute("checked")).Select(r => r.Children[1].TextContent.Trim()).ToList();

    [Fact]
    public void Families_with_something_on_file_start_selected_and_the_links_print_them()
    {
        var page = Render<Families>();

        Assert.Equal(3, page.FindAll("tbody tr").Count);
        Assert.Contains("Adult charges only", page.Find("tbody tr:first-child").TextContent);
        Assert.Contains("Inactive", page.FindAll("tbody tr")[1].TextContent);
        Assert.Contains("Nothing on file", page.FindAll("tbody tr")[2].TextContent);

        Assert.Equal(2, Ticked(page).Count);
        Assert.Equal($"/admin/families/status-sheets?g=lee&g=maria&from={YearAgo}&to={Today}", page.Find("#printStatusSheets").GetAttribute("href"));
        // Only Lee owes anything, so only Lee's invoices print.
        Assert.Equal("/admin/families/invoices?g=lee", page.Find("#printInvoices").GetAttribute("href"));
        Assert.Contains("(1)", page.Find("#printInvoices").TextContent);
    }

    [Fact]
    public void Owing_money_selects_only_families_that_owe_and_none_disables_the_links()
    {
        var page = Render<Families>();

        page.FindAll("button.choice-btn").Single(b => b.TextContent == "Owing money").Click();
        Assert.Equal(["Lee Leader"], Ticked(page).Select(n => n[..10]));

        page.FindAll("button.choice-btn").Single(b => b.TextContent == "None").Click();
        Assert.Empty(Ticked(page));
        Assert.True(page.Find("#printStatusSheets").HasAttribute("disabled"));
        Assert.True(page.Find("#printInvoices").HasAttribute("disabled"));
    }

    [Fact]
    public void Arriving_from_a_guardian_selects_just_their_family()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/families?g=maria");

        var page = Render<Families>();

        Assert.Equal($"/admin/families/status-sheets?g=maria&from={YearAgo}&to={Today}", page.Find("#printStatusSheets").GetAttribute("href"));
        Assert.True(page.Find("#printInvoices").HasAttribute("disabled"));
    }

    [Fact]
    public void A_familys_name_opens_just_their_status_sheet_for_the_picked_dates()
    {
        var page = Render<Families>();

        var link = page.FindAll("tbody tr")[1].QuerySelector("a.family-sheet-link")!;
        Assert.Equal("Luis Rivera and Maria Rivera", link.TextContent.Trim());
        Assert.Equal($"/admin/families/status-sheets?g=maria&from={YearAgo}&to={Today}", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));

        page.Find("#fromDate").Change("2026-01-01");
        Assert.Equal($"/admin/families/status-sheets?g=maria&from=2026-01-01&to={Today}",
            page.FindAll("tbody tr")[1].QuerySelector("a.family-sheet-link")!.GetAttribute("href"));
    }

    [Fact]
    public void A_viewers_family_menu_has_the_status_sheet_and_family_invoice()
    {
        var page = Render<Families>();

        var items = page.FindAll("tbody tr")[0].QuerySelectorAll(".ellipsis-menu-item");
        Assert.Equal(["Print status sheet", "Print family bill"], items.Select(i => i.TextContent.Trim()));
        Assert.Equal($"/admin/families/status-sheets?g=lee&from={YearAgo}&to={Today}", items[0].GetAttribute("href"));
        Assert.Equal("/admin/families/invoices?g=lee", items[1].GetAttribute("href"));
        // The Riveras owe nothing, so there's no invoice to print.
        Assert.Equal(["Print status sheet"], page.FindAll("tbody tr")[1].QuerySelectorAll(".ellipsis-menu-item").Select(i => i.TextContent.Trim()));
    }

    [Fact]
    public void An_editor_can_record_a_payment_for_a_family_that_owes_and_invoice_each_adult()
    {
        SignInAs(AdminRole.Writer);

        var page = Render<Families>();

        var rows = page.FindAll("tbody tr");
        Assert.Equal(["Print status sheet", "Print family bill", "Record family payment", "New invoice for Lee Leader"],
            rows[0].QuerySelectorAll(".ellipsis-menu-item").Select(i => i.TextContent.Trim()));
        Assert.Equal("/admin/invoices/family-payment?guardianId=lee", rows[0].QuerySelectorAll(".ellipsis-menu-item")[2].GetAttribute("href"));
        // The Riveras owe nothing, so there's no payment to record.
        Assert.Equal(["Print status sheet", "New invoice for Luis Rivera and Maria Rivera"],
            rows[1].QuerySelectorAll(".ellipsis-menu-item").Select(i => i.TextContent.Trim()));
    }

    [Fact]
    public void Ticking_a_family_adds_it_to_the_print_links()
    {
        var page = Render<Families>();

        page.FindAll("tbody tr")[2].QuerySelector("input")!.Change(true);

        Assert.StartsWith("/admin/families/status-sheets?g=lee&g=maria&g=new&", page.Find("#printStatusSheets").GetAttribute("href"));
    }
}

/// <summary>The Guardians page's row menu: everyone can open a family's status sheet; only
/// writers get the actions that change things.</summary>
public class GuardiansMenuTests : AdminPageTestBase
{
    public GuardiansMenuTests()
    {
        Fake<IGuardianRepository>().GetAllAsync().Returns(new List<GuardianEntity> { new() { GuardianId = "maria", FullName = "Maria Rivera", Email = "maria@example.com", IsActive = true } });
        Fake<IGuardianScoutLinkRepository>().GetScoutsForGuardianAsync("maria").Returns(new List<GuardianScoutLinkEntity>());
        Fake<IScoutRepository>().GetActiveAsync().Returns(new List<ScoutEntity>());
        Fake<IReimbursementRequestRepository>().GetBySubmitterAsync("maria").Returns(new List<ReimbursementRequestEntity>());
        Fake<IFinanceAuditRepository>();
        Fake<IInvoiceRepository>().GetGuardianIdsWithInvoicesAsync().Returns(new HashSet<string>());
        _authenticators = Fake<IGuardianAuthenticatorRepository>();
        _authenticators.GetGuardianIdsWithAuthenticatorAsync().Returns(new HashSet<string>());
        _authenticatorService = Fake<IGuardianAuthenticatorService>();
    }

    private readonly IGuardianAuthenticatorRepository _authenticators;
    private readonly IGuardianAuthenticatorService _authenticatorService;

    [Fact]
    public void A_guardian_with_an_authenticator_app_is_marked_and_a_writer_can_remove_it()
    {
        SignInAs(AdminRole.Writer);
        _authenticators.GetGuardianIdsWithAuthenticatorAsync().Returns(new HashSet<string> { "maria" });

        var page = Render<Guardians>();

        Assert.Contains("Authenticator app", page.Find(".guardian-card-header .badge").TextContent);
        page.FindAll(".ellipsis-menu-item").Single(i => i.TextContent.Trim() == "Remove authenticator app").Click();
        page.FindAll("button").Single(b => b.TextContent == "Confirm remove").Click();

        _authenticatorService.Received(1).RemoveAsync(Arg.Is<GuardianEntity>(g => g.GuardianId == "maria"));
    }

    [Fact]
    public void A_writer_can_show_the_qr_code_in_person_and_turn_it_on_with_the_code_read_back()
    {
        SignInAs(AdminRole.Writer);
        _authenticatorService.BeginSetup("maria@example.com")
            .Returns(new AuthenticatorSetup("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", "otpauth://totp/x", "data:image/png;base64,AAAA"));
        _authenticatorService.ConfirmSetupAsync(Arg.Any<GuardianEntity>(), "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", "123456").Returns(true);

        var page = Render<Guardians>();
        page.FindAll(".ellipsis-menu-item").Single(i => i.TextContent.Trim() == "Set up authenticator app").Click();

        Assert.Contains("Set up Maria Rivera's authenticator app", page.Find("[role=dialog] h2").TextContent);
        Assert.Equal("data:image/png;base64,AAAA", page.Find("[role=dialog] img.authenticator-qr").GetAttribute("src"));
        Assert.Contains("JBSW Y3DP", page.Find(".authenticator-key code").TextContent);
        Assert.Empty(page.FindAll("[role=dialog] a[href^='otpauth:']"));

        _authenticators.GetGuardianIdsWithAuthenticatorAsync().Returns(new HashSet<string> { "maria" });
        page.Find(".authenticator-confirm input").Change("123456");
        page.Find(".authenticator-confirm").Submit();

        Assert.Empty(page.FindAll("[role=dialog]"));
        Assert.Contains("Authenticator app", page.Find(".guardian-card-header .badge").TextContent);
    }

    [Theory]
    [InlineData(AdminRole.Reader, new[] { "Open family" })]
    [InlineData(AdminRole.Writer, new[] { "Open family", "Deactivate", "Set up authenticator app", "Delete" })]
    public void The_menu_opens_the_family_for_everyone_and_money_actions_live_there(AdminRole role, string[] items)
    {
        SignInAs(role);

        var page = Render<Guardians>();

        var menu = page.FindAll(".ellipsis-menu-panel .ellipsis-menu-item");
        Assert.Equal(items, menu.Select(i => i.TextContent.Trim()));
        Assert.Equal("/admin/families?g=maria", menu[0].GetAttribute("href"));
    }
}
