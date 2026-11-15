using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class ScoutDetailPageTests : AdminPageTestBase
{
    private readonly IScoutAccountService _accounts;
    private readonly ScoutEntity _alex = new() { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 120m, IsActive = true };
    private readonly ScoutEntity _casey = new() { RowKey = "casey", FullName = "Casey Rivera", IsActive = true };

    public ScoutDetailPageTests()
    {
        _accounts = Fake<IScoutAccountService>();
        _accounts.GetScoutStatementAsync("alex").Returns(_ => new ScoutStatementModel(_alex, [], []));
        Fake<IScoutRepository>().GetActiveAsync().Returns([_alex, _casey]);
    }

    private IRenderedComponent<ScoutDetail> RenderAlex(string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/scouts/alex{query}");
        return Render<ScoutDetail>(p => p.Add(x => x.Id, "alex"));
    }

    [Fact]
    public void Coming_from_the_list_opens_the_choice_of_where_the_money_goes()
    {
        SignInAs(AdminRole.Writer);

        var page = RenderAlex("?deactivate=true");

        var options = page.FindAll("#deactivateMoveTo option").Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal(["Leave it on their account", "Move it to the troop general fund", "Move it to Casey Rivera"], options);
    }

    [Fact]
    public void Moving_the_balance_needs_a_reason_then_deactivates_with_it()
    {
        SignInAs(AdminRole.Writer);
        var page = RenderAlex("?deactivate=true");

        page.Find("#deactivateMoveTo").Change("general");
        var deactivate = page.FindAll("#deactivatePanel button").Single(b => b.TextContent.Trim() == "Deactivate");
        Assert.True(deactivate.HasAttribute("disabled"));

        page.Find("#deactivateReason").Input("Aged out");
        page.FindAll("#deactivatePanel button").Single(b => b.TextContent.Trim() == "Deactivate").Click();

        _accounts.Received(1).DeactivateAsync("alex", BalanceDestination.GeneralFund, "Aged out", Arg.Any<string>());
    }

    [Fact]
    public void Leaving_the_money_deactivates_with_no_transfer()
    {
        SignInAs(AdminRole.Writer);
        var page = RenderAlex();

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Deactivate scout").Click();
        page.FindAll("#deactivatePanel button").Single(b => b.TextContent.Trim() == "Deactivate").Click();

        _accounts.Received(1).DeactivateAsync("alex", null, Arg.Any<string?>(), Arg.Any<string>());
    }

    [Fact]
    public void Transfer_funds_is_offered_when_there_is_money()
    {
        SignInAs(AdminRole.Writer);
        Assert.Contains(RenderAlex().FindAll("a"), a => a.GetAttribute("href") == "/admin/scouts/alex/transfer");
    }

    [Fact]
    public void Readers_get_no_actions()
    {
        SignInAs(AdminRole.Reader);

        var page = RenderAlex("?deactivate=true");

        Assert.Empty(page.FindAll(".action-bar"));
        Assert.Empty(page.FindAll("#deactivatePanel"));
    }

    private static InvoiceEntity Invoice(string id, string status, decimal total, decimal applied, decimal stillOwed, int day) => new()
    {
        PartitionKey = "alex", RowKey = id, InvoiceId = id, ScoutId = "alex", Description = id,
        TotalAmount = total, AmountAppliedFromAccount = applied, AmountDueFromFamily = stillOwed, StatusValue = status,
        IssuedUtc = new DateTimeOffset(2026, 10, day, 17, 0, 0, TimeSpan.Zero),
    };

    private static ScoutLedgerEntryEntity Entry(string rowKey, string type, string source, decimal amount) => new()
    {
        PartitionKey = "alex", RowKey = rowKey, EntryTypeValue = type, SourceTypeValue = source, Amount = amount,
        Description = source, CreatedUtc = new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero),
    };

    private void GiveAlex(IReadOnlyList<InvoiceEntity> invoices, IReadOnlyList<ScoutLedgerEntryEntity>? entries = null) =>
        _accounts.GetScoutStatementAsync("alex").Returns(_ => new ScoutStatementModel(_alex, entries ?? [], invoices));

    [Fact]
    public void The_scout_account_and_what_the_family_owes_are_shown_separately()
    {
        GiveAlex(
        [
            Invoice("Camp", InvoiceStatus.Outstanding, 200m, 50m, 150m, 1),
            Invoice("Food", InvoiceStatus.Outstanding, 40m, 0m, 25m, 2),
            Invoice("Hike", InvoiceStatus.Paid, 30m, 0m, 0m, 3),
            Invoice("Trip", InvoiceStatus.Voided, 90m, 0m, 90m, 4),
        ]);
        SignInAs(AdminRole.Reader);

        var page = RenderAlex();

        Assert.Contains("$120.00", page.Find("#accountCard").TextContent);
        var owed = page.Find("#owedCard").TextContent;
        Assert.Contains("$175.00", owed);
        Assert.Contains("2 unpaid invoices", owed);
        Assert.Contains("$175.00", page.Find("tfoot").TextContent);
    }

    [Fact]
    public void Unpaid_invoices_come_first_and_settled_ones_owe_nothing()
    {
        GiveAlex(
        [
            Invoice("Hike", InvoiceStatus.Paid, 30m, 30m, 0m, 9),
            Invoice("Camp", InvoiceStatus.Outstanding, 200m, 0m, 200m, 1),
        ]);
        SignInAs(AdminRole.Reader);

        var rows = RenderAlex().FindAll("table.report-table tbody tr").Take(2).Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList()).ToList();

        Assert.Equal(["Camp", "$200.00", "—", "$200.00"], rows[0].Skip(1).Take(4));
        Assert.Equal(["Hike", "$30.00", "$30.00", "—"], rows[1].Skip(1).Take(4));
    }

    [Fact]
    public void Invoices_and_invoice_activity_link_to_the_invoice()
    {
        var applied = Entry("1", LedgerEntryType.Debit, LedgerSourceType.InvoiceApplied, 50m);
        applied.SourceId = "Camp";
        applied.Description = "Applied to Camp";
        GiveAlex([Invoice("Camp", InvoiceStatus.Outstanding, 200m, 50m, 150m, 1)], [applied]);
        SignInAs(AdminRole.Reader);

        var links = RenderAlex().FindAll("a").Where(a => a.GetAttribute("href") == "/admin/invoices/Camp?scoutId=alex").Select(a => a.TextContent).ToList();

        Assert.Equal(["Camp", "Applied to Camp"], links);
    }

    [Fact]
    public void An_overdrawn_account_says_so()
    {
        _alex.CachedBalance = -15m;
        SignInAs(AdminRole.Reader);

        Assert.Contains("Overdrawn", RenderAlex().Find("#accountCard").TextContent);
    }

    [Fact]
    public void Activity_uses_plain_names_and_a_running_balance_ending_at_the_account()
    {
        GiveAlex([],
        [
            Entry("1", LedgerEntryType.Credit, LedgerSourceType.FundraiserAllocation, 150m),
            Entry("2", LedgerEntryType.Debit, LedgerSourceType.InvoiceApplied, 50m),
            Entry("3", LedgerEntryType.Credit, LedgerSourceType.ManualAdjustment, 20m),
        ]);
        SignInAs(AdminRole.Reader);

        var rows = RenderAlex().FindAll("#activity tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList()).ToList();

        // Newest first; the top balance is what's in the account now ($120).
        Assert.Equal(["Adjustment", "Paid toward invoice", "Fundraiser share"], rows.Select(r => r[1]));
        Assert.Equal(["$120.00", "$100.00", "$150.00"], rows.Select(r => r[3]));
    }
}
