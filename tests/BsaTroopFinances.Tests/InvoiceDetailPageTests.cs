using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class InvoiceDetailPageTests : AdminPageTestBase
{
    private readonly IInvoiceRepository _invoices;
    private readonly IScoutAccountService _accounts;
    private InvoiceEntity _invoice = Invoice();

    public InvoiceDetailPageTests()
    {
        _invoices = Fake<IInvoiceRepository>();
        _invoices.GetByIdAsync("scout1", "inv1").Returns(_ => _invoice);
        _invoices.GetByInvoiceIdAsync("inv1").Returns(_ => _invoice);
        Fake<IInvoicePaymentRepository>().GetByInvoiceAsync("inv1").Returns(new List<InvoicePaymentEntity>());
        Fake<IScoutRepository>().GetByIdAsync("scout1").Returns(new ScoutEntity { RowKey = "scout1", CachedBalance = 40 });
        Fake<IFinanceAuditRepository>().GetForEntityAsync(FinanceAuditEntityType.Invoice, "inv1").Returns(new List<FinanceAuditEntryEntity>());
        _accounts = Fake<IScoutAccountService>();

        var links = Fake<IGuardianScoutLinkRepository>();
        links.GetGuardiansForScoutAsync(Arg.Any<string>()).Returns(new List<GuardianScoutLinkEntity>());
        links.GetGuardiansForScoutAsync("scout1").Returns(new List<GuardianScoutLinkEntity> { new() { GuardianId = "maria" }, new() { GuardianId = "luis" }, new() { GuardianId = "gone" } });
        var guardians = Fake<IGuardianRepository>();
        guardians.GetByIdAsync("maria").Returns(new GuardianEntity { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true });
        guardians.GetByIdAsync("luis").Returns(new GuardianEntity { GuardianId = "luis", FullName = "Luis Rivera", IsActive = true });
        guardians.GetByIdAsync("gone").Returns(new GuardianEntity { GuardianId = "gone", FullName = "Aaron Former", IsActive = false });
    }

    [Fact]
    public void An_outstanding_invoice_offers_the_family_invoice_for_the_scouts_guardian()
    {
        SignInAs(AdminRole.Reader);

        var link = RenderPage().Find("#printFamilyInvoice");

        // The first active guardian by name; the inactive one is skipped.
        Assert.Equal("/admin/invoices/family-payment/print?guardianId=luis", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
    }

    [Fact]
    public void An_adult_invoice_offers_that_adults_family_invoice()
    {
        SignInAs(AdminRole.Writer);
        _invoice = new InvoiceEntity
        {
            PartitionKey = "maria", RowKey = "inv1", InvoiceId = "inv1", GuardianId = "maria", GuardianName = "Maria Rivera",
            Description = "Summer camp, adult", TotalAmount = 320, AmountDueFromFamily = 320, StatusValue = InvoiceStatus.Outstanding,
        };

        var page = RenderPage();

        Assert.Equal("/admin/invoices/family-payment/print?guardianId=maria", page.Find("#printFamilyInvoice").GetAttribute("href"));
        Assert.DoesNotContain("Scout funds", page.Find("#paymentMethod").TextContent);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "scout1")]            // nothing outstanding to put on a family invoice
    [InlineData(InvoiceStatus.Outstanding, "orphan")]     // no guardian linked to the scout
    public void No_family_invoice_link_without_something_to_print_or_someone_to_bill(string status, string scoutId)
    {
        SignInAs(AdminRole.Writer);
        _invoice = Invoice(scoutId: scoutId, status: status);

        Assert.Empty(RenderPage().FindAll("#printFamilyInvoice"));
    }

    [Fact]
    public void Arriving_from_issuing_confirms_and_points_to_the_family_bill()
    {
        SignInAs(AdminRole.Writer);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/invoices/inv1?scoutId=scout1&issued=true");

        var notice = RenderPage().Find("#issuedNotice");

        Assert.Contains("Print family bill", notice.TextContent);
        Assert.Equal("/admin/invoices/new", notice.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void No_issued_confirmation_on_an_ordinary_visit()
    {
        SignInAs(AdminRole.Writer);

        Assert.Empty(RenderPage().FindAll("#issuedNotice"));
    }

    private IRenderedComponent<InvoiceDetail> RenderPage() =>
        Render<InvoiceDetail>(p => p.Add(x => x.Id, "inv1"));

    [Fact]
    public void Writer_on_outstanding_invoice_gets_payment_and_void_sections()
    {
        SignInAs(AdminRole.Writer);

        var page = RenderPage();

        Assert.Equal("Edit Invoice", page.Find("h1").TextContent);
        Assert.Contains("Record payment", page.Markup);

        // Void lives on its own page now, reached from the action bar.
        var voidLink = page.Find(".action-bar a.btn-ghost-danger");
        Assert.Equal("/admin/invoices/inv1/void?scoutId=scout1", voidLink.GetAttribute("href"));
        Assert.Empty(page.FindAll("#voidReason"));

        // Details and Record payment share the side-by-side row.
        var sideBySide = page.Find(".side-by-side");
        Assert.Equal(2, sideBySide.Children.Length);
        Assert.Contains("Record payment", sideBySide.Children[1].TextContent);
        Assert.Contains("Payment history", page.Markup);
        Assert.Contains("Action history", page.Markup);
    }

    [Fact]
    public void Reader_gets_a_read_only_page()
    {
        SignInAs(AdminRole.Reader);

        var page = RenderPage();

        Assert.Equal("Invoice", page.Find("h1").TextContent);
        Assert.DoesNotContain("Record payment", page.Markup);
        Assert.DoesNotContain("Void invoice", page.Markup);
        Assert.Contains("Action history", page.Markup);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Voided)]
    public void Closed_invoices_have_no_edit_actions_even_for_writers(string status)
    {
        _invoice = Invoice(status: status, due: 0);
        SignInAs(AdminRole.Writer);

        var page = RenderPage();

        Assert.Equal("Invoice", page.Find("h1").TextContent);
        Assert.DoesNotContain("Record payment", page.Markup);
        Assert.DoesNotContain("Void invoice", page.Markup);
    }

    [Fact]
    public void Payment_methods_offered_are_check_cash_venmo_other_and_scout_funds()
    {
        SignInAs(AdminRole.Writer);

        var page = RenderPage();

        var methods = page.FindAll("#paymentMethod option").Select(o => o.TextContent).ToList();
        Assert.Equal(["Check", "Cash", "Venmo", "Other", "Scout funds"], methods);
    }

    [Fact]
    public void Cash_payment_passes_cash_through_with_an_optional_note()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = RenderPage();

        page.Find("#paymentMethod").Change(InvoicePaymentMethod.Cash);
        Assert.Equal("Note (optional)", page.Find("label[for=reference]").TextContent);
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Record payment").Click();

        _accounts.Received(1).RecordInvoicePaymentAsync("scout1", "inv1", 175m, InvoicePaymentMethod.Cash, "", "Pat Treasurer");
    }

    [Theory]
    [InlineData(AdminRole.Writer)]
    [InlineData(AdminRole.Reader)]
    public void Everyone_can_print_the_invoice_in_a_new_tab(AdminRole role)
    {
        SignInAs(role);

        var page = RenderPage();

        var print = page.FindAll(".action-bar a").Single(a => a.TextContent.Contains("Print invoice"));
        Assert.Equal("/admin/invoices/inv1/print?scoutId=scout1", print.GetAttribute("href"));
        Assert.Equal("_blank", print.GetAttribute("target"));
    }

    [Fact]
    public void Recording_a_check_payment_calls_the_service_with_the_amount_and_reference()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = RenderPage();

        // Money inputs fill from the cents like a cash register: "50" would be $0.50.
        page.Find("#paymentAmount").Input("50.00");
        page.Find("#reference").Input("Check 1042");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Record payment").Click();

        _accounts.Received(1).RecordInvoicePaymentAsync("scout1", "inv1", 50m, InvoicePaymentMethod.Check, "Check 1042", "Pat Treasurer");        Assert.Contains("Payment of $50.00 recorded.", page.Markup);
    }
}
