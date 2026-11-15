using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The parent portal's home page: a parent's scouts, their own invoices as an adult, and
/// the links to print them. (What a parent may print is covered in InvoiceDocumentServiceTests.)</summary>
public class PortalHomePageTests : AdminPageTestBase
{
    private readonly IInvoiceRepository _invoices;

    public PortalHomePageTests()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Maria Rivera");
        auth.SetClaims(new Claim("GuardianId", "maria"));

        Fake<IGuardianRepository>().GetByIdAsync("maria").Returns(new GuardianEntity { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true });
        Fake<IGuardianScoutLinkRepository>().GetScoutsForGuardianAsync("maria").Returns(new List<GuardianScoutLinkEntity> { new() { ScoutId = "alex" } });
        Fake<IScoutRepository>().GetByIdAsync("alex").Returns(new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", IsActive = true });

        _invoices = Fake<IInvoiceRepository>();
        _invoices.GetByScoutAsync("alex").Returns(new List<InvoiceEntity>());
        _invoices.GetByGuardianAsync("maria").Returns(new List<InvoiceEntity>());
    }

    [Fact]
    public void A_parent_who_owes_something_gets_the_family_invoice_and_a_print_link_per_own_invoice()
    {
        _invoices.GetByScoutAsync("alex").Returns(new List<InvoiceEntity>
        {
            new() { InvoiceId = "scout", ScoutId = "alex", ScoutName = "Alex Rivera", AmountDueFromFamily = 175, StatusValue = InvoiceStatus.Outstanding },
        });
        _invoices.GetByGuardianAsync("maria").Returns(new List<InvoiceEntity>
        {
            new() { InvoiceId = "own", GuardianId = "maria", GuardianName = "Maria Rivera", Description = "Adult trip fee", AmountDueFromFamily = 60, StatusValue = InvoiceStatus.Outstanding },
        });

        var page = Render<BsaTroopFinances.Components.Pages.Portal.Index>();

        Assert.Equal("/portal/invoices/family/print", page.Find(".action-bar #printFamilyInvoice").GetAttribute("href"));
        Assert.Equal("/portal/invoices/own/print", page.Find("#ownInvoices tbody a").GetAttribute("href"));
        Assert.Contains("Adult trip fee", page.Find("#ownInvoices").TextContent);
    }

    [Fact]
    public void A_parent_who_owes_nothing_gets_no_print_links()
    {
        var page = Render<BsaTroopFinances.Components.Pages.Portal.Index>();

        Assert.Empty(page.FindAll("#printFamilyInvoice"));
        Assert.Empty(page.FindAll("#ownInvoices"));
        Assert.Contains("Alex Rivera", page.Markup);
    }

    [Fact]
    public void Every_parent_can_open_a_status_sheet_for_the_last_12_months()
    {
        var page = Render<BsaTroopFinances.Components.Pages.Portal.Index>();

        var form = page.Find("#statusSheet form");
        Assert.Equal("/portal/status-sheet", form.GetAttribute("action"));
        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), form.QuerySelector("input[name=to]")!.GetAttribute("value"));
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).AddMonths(-12).ToString("yyyy-MM-dd"), form.QuerySelector("input[name=from]")!.GetAttribute("value"));
    }
}
