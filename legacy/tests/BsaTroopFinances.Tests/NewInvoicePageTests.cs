using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class NewInvoicePageTests : AdminPageTestBase
{
    private readonly IScoutAccountService _accounts;

    public NewInvoicePageTests()
    {
        Fake<IScoutRepository>().GetActiveAsync().Returns(new List<ScoutEntity> { new() { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 40m } });
        Fake<IGuardianRepository>().GetAllAsync().Returns(new List<GuardianEntity>
        {
            new() { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true },
            new() { GuardianId = "gone", FullName = "Former Guardian", IsActive = false },
        });
        _accounts = Fake<IScoutAccountService>();
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
    }

    private static void FillIn(IRenderedComponent<NewInvoice> page)
    {
        page.Find("#description").Change("Summer camp, adult");
        page.Find("#totalAmount").Change("320.00");
    }

    [Fact]
    public void Billing_an_adult_issues_a_guardian_invoice_with_no_scout_fund_option()
    {
        var page = Render<NewInvoice>();

        page.Find("#billAdult").Click();
        Assert.Equal(["Maria Rivera"], page.FindAll("#guardian option").Skip(1).Select(o => o.TextContent));   // active guardians only
        Assert.Empty(page.FindAll("#scout"));
        page.Find("#guardian").Change("maria");
        FillIn(page);
        Assert.Empty(page.FindAll("#applyFromAccount"));
        page.Find("button.btn-primary").Click();

        _accounts.Received(1).IssueGuardianInvoiceAsync("maria", 320m, Arg.Any<DateTimeOffset>(), "Summer camp, adult", "Pat Treasurer");
        _accounts.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default!, default, default, default, default!, default!);
    }

    [Fact]
    public void Billing_a_scout_still_works_as_before()
    {
        var page = Render<NewInvoice>();

        page.Find("#scout").Change("alex");
        FillIn(page);
        Assert.Single(page.FindAll("#applyFromAccount"));
        page.Find("button.btn-primary").Click();

        _accounts.Received(1).IssueInvoiceAsync("alex", 320m, 0m, Arg.Any<DateTimeOffset>(), "Summer camp, adult", "Pat Treasurer");
        _accounts.DidNotReceiveWithAnyArgs().IssueGuardianInvoiceAsync(default!, default, default, default!, default!);
    }

    [Fact]
    public void Issuing_lands_on_the_new_invoice_with_a_confirmation()
    {
        _accounts.IssueInvoiceAsync("alex", 320m, 0m, Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new InvoiceEntity { PartitionKey = "alex", InvoiceId = "inv9" });
        var page = Render<NewInvoice>();

        page.Find("#scout").Change("alex");
        FillIn(page);
        page.Find("button.btn-primary").Click();

        Assert.EndsWith("/admin/invoices/inv9?scoutId=alex&issued=true", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Arriving_from_a_guardian_opens_ready_to_bill_that_adult()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/invoices/new?guardianId=maria");

        var page = Render<NewInvoice>();

        Assert.Contains("selected", page.Find("#billAdult").ClassList);
        Assert.Equal("maria", page.Find("#guardian").GetAttribute("value"));
    }
}
