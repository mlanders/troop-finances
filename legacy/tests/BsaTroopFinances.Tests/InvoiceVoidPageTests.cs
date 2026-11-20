using Bunit;
using Bunit.TestDoubles;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class InvoiceVoidPageTests : AdminPageTestBase
{
    private readonly IScoutAccountService _accounts;
    private InvoiceEntity _invoice = Invoice();

    public InvoiceVoidPageTests()
    {
        var invoices = Fake<IInvoiceRepository>();
        invoices.GetByIdAsync("scout1", "inv1").Returns(_ => _invoice);
        invoices.GetByInvoiceIdAsync("inv1").Returns(_ => _invoice);
        _accounts = Fake<IScoutAccountService>();
    }

    private IRenderedComponent<InvoiceVoid> RenderPage() =>
        Render<InvoiceVoid>(p => p.Add(x => x.Id, "inv1"));

    [Fact]
    public void Void_needs_a_reason_then_a_confirmation_then_returns_to_the_invoice()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = RenderPage();

        Assert.True(page.FindAll("button").Single(b => b.TextContent.Trim() == "Void invoice").HasAttribute("disabled"));

        page.Find("#voidReason").Input("Entered twice");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Void invoice").Click();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Confirm void").Click();

        _accounts.Received(1).VoidInvoiceAsync("scout1", "inv1", "Entered twice", "Pat Treasurer");
        Assert.EndsWith("/admin/invoices/inv1", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Cancelling_the_confirmation_voids_nothing()
    {
        SignInAs(AdminRole.Writer);
        var page = RenderPage();

        page.Find("#voidReason").Input("Entered twice");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Void invoice").Click();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        _accounts.DidNotReceiveWithAnyArgs().VoidInvoiceAsync(default!, default!, default!, default!);
    }

    [Fact]
    public void Shows_the_account_credit_back_when_money_was_applied()
    {
        SignInAs(AdminRole.Writer);

        var page = RenderPage();

        Assert.Contains("credited back to Alex Rivera's account", page.Markup);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Voided)]
    public void Closed_invoices_cannot_be_voided(string status)
    {
        _invoice = Invoice(status: status, due: 0);
        SignInAs(AdminRole.Writer);

        var page = RenderPage();

        Assert.Contains("can't be voided", page.Markup);
        Assert.Empty(page.FindAll("#voidReason"));
    }
}
