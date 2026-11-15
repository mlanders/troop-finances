using AngleSharp.Dom;
using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class FamilyPaymentPageTests : AdminPageTestBase
{
    private readonly IScoutAccountService _accounts;

    public FamilyPaymentPageTests()
    {
        var guardians = Fake<IGuardianRepository>();
        guardians.GetAllAsync().Returns(new List<GuardianEntity>
        {
            new() { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true },
            new() { GuardianId = "chris", FullName = "Chris Lee", IsActive = true },
            new() { GuardianId = "nobody", FullName = "Nothing Owed", IsActive = true },
        });

        var links = Fake<IGuardianScoutLinkRepository>();
        links.GetScoutsForGuardianAsync("maria").Returns(new List<GuardianScoutLinkEntity> { new() { ScoutId = "alex" }, new() { ScoutId = "casey" } });
        links.GetScoutsForGuardianAsync("chris").Returns(new List<GuardianScoutLinkEntity> { new() { ScoutId = "jordan" } });
        links.GetScoutsForGuardianAsync("nobody").Returns(new List<GuardianScoutLinkEntity> { new() { ScoutId = "sam" } });

        var invoices = new List<InvoiceEntity>
        {
            Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", due: 175),
            Invoice("inv-food", "casey", "Casey Rivera", "Campout food", due: 45),
            Invoice("inv-flag", "casey", "Casey Rivera", "Patrol flag", due: 18),
            Invoice("inv-paid", "casey", "Casey Rivera", "Old dues", due: 0, status: InvoiceStatus.Paid),
            Invoice("inv-shirt", "jordan", "Jordan Lee", "Troop T-shirt", due: 20),
        };
        // The page asks the database for outstanding invoices only.
        Fake<IInvoiceRepository>().GetPageAsync(InvoiceStatus.Outstanding, null, null, Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => new PagedResult<InvoiceEntity>(invoices.Where(i => i.StatusValue == InvoiceStatus.Outstanding).ToList(), 0));

        _accounts = Fake<IScoutAccountService>();
        // No scout account money unless a test gives some (see GiveBalances).
        _scoutRepository = Fake<IScoutRepository>();
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
    }

    private readonly IScoutRepository _scoutRepository;

    private void GiveBalances(decimal alex, decimal casey)
    {
        _scoutRepository.GetByIdAsync("alex").Returns(new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = alex });
        _scoutRepository.GetByIdAsync("casey").Returns(new ScoutEntity { RowKey = "casey", FullName = "Casey Rivera", CachedBalance = casey });
    }

    private static IElement Row(IRenderedComponent<FamilyPayment> page, string description) =>
        page.FindAll("tbody tr").Single(r => r.TextContent.Contains(description));

    [Fact]
    public void Scout_account_boxes_appear_only_for_scouts_with_money()
    {
        GiveBalances(alex: 40m, casey: 0m);

        var page = RenderForMaria();

        Assert.NotNull(Row(page, "Summer camp deposit").QuerySelector("input.from-scout-account"));
        Assert.Contains("Alex has $40.00", Row(page, "Summer camp deposit").TextContent);
        Assert.Null(Row(page, "Campout food").QuerySelector("input.from-scout-account"));
    }

    [Fact]
    public void Taking_from_the_scout_account_lowers_what_the_family_pays()
    {
        GiveBalances(alex: 40m, casey: 0m);
        var page = RenderForMaria();

        Row(page, "Summer camp deposit").QuerySelector("input.from-scout-account")!.Input("40.00");

        Assert.Equal("$135.00", Row(page, "Summer camp deposit").QuerySelector("input.family-pays")!.GetAttribute("value"));
        Assert.Equal("$198.00", page.Find("#paymentTotal").TextContent);
        Assert.Equal("$40.00", page.Find("#scoutTotal").TextContent);
    }

    [Fact]
    public void Use_scout_funds_first_fills_each_scout_within_their_own_balance()
    {
        GiveBalances(alex: 40m, casey: 50m);
        var page = RenderForMaria();

        page.Find("#useScoutFunds").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Record family payment")).Click();

        // Casey's $50 covers the $45 food first (earliest due), leaving $5 for the $18 flag.
        _accounts.Received(1).RecordFamilyPaymentAsync(
            Arg.Is<IReadOnlyList<FamilyPaymentLine>>(lines =>
                lines.Single(l => l.InvoiceId == "inv-camp") == new FamilyPaymentLine("alex", "inv-camp", 135m, 40m)
                && lines.Single(l => l.InvoiceId == "inv-food") == new FamilyPaymentLine("casey", "inv-food", 0m, 45m)
                && lines.Single(l => l.InvoiceId == "inv-flag") == new FamilyPaymentLine("casey", "inv-flag", 13m, 5m)),
            InvoicePaymentMethod.Check, Arg.Any<string?>(), "Pat Treasurer");
    }

    [Fact]
    public void More_than_a_scout_has_across_their_invoices_blocks_recording()
    {
        GiveBalances(alex: 0m, casey: 30m);
        var page = RenderForMaria();

        Row(page, "Campout food").QuerySelector("input.from-scout-account")!.Input("20.00");
        Row(page, "Patrol flag").QuerySelector("input.from-scout-account")!.Input("15.00");

        Assert.Contains("More than the $30.00 in Casey's account", Row(page, "Patrol flag").TextContent);
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Contains("Record family payment")).HasAttribute("disabled"));
    }

    // Arrives the way the Guardians menu link does: ?guardianId= in the address.
    private IRenderedComponent<FamilyPayment> RenderForMaria()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/invoices/family-payment?guardianId=maria");
        return Render<FamilyPayment>();
    }

    private static List<string> GuardianOptions(IRenderedComponent<FamilyPayment> page) =>
        page.FindAll("#guardian option").Skip(1).Select(o => o.TextContent).ToList();

    [Fact]
    public void Only_families_with_outstanding_invoices_are_offered()
    {
        var page = Render<FamilyPayment>();

        var options = GuardianOptions(page);
        Assert.Equal(2, options.Count);
        Assert.StartsWith("Chris Lee (1 outstanding, $20.00)", options[0]);
        Assert.StartsWith("Maria Rivera (3 outstanding, $238.00)", options[1]);
    }

    [Fact]
    public void A_family_lists_every_sibling_outstanding_invoice_ticked_at_the_full_amount()
    {
        var page = RenderForMaria();

        var rows = page.FindAll("tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain("Old dues", page.Markup);
        Assert.All(page.FindAll("tbody input[type=checkbox]"), cb => Assert.True(cb.HasAttribute("checked")));
        Assert.Equal("$238.00", page.Find("#paymentTotal").TextContent);
    }

    // Arriving from a scout's statement: ?scoutId= in the address.
    [Theory]
    [InlineData("casey", "maria", 3)]     // opens on the scout's family, siblings' invoices included
    [InlineData("jordan", "chris", 1)]
    public void Arriving_from_a_scout_opens_on_that_scouts_family(string scoutId, string guardianId, int invoices)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/invoices/family-payment?scoutId={scoutId}");

        var page = Render<FamilyPayment>();

        Assert.Equal(guardianId, page.Find("#guardian").GetAttribute("value"));
        Assert.Equal(invoices, page.FindAll("tbody tr").Count);
        Assert.Empty(page.FindAll(".alert-info"));
    }

    [Theory]
    [InlineData("sam")]        // linked to a guardian, nothing outstanding
    [InlineData("stranger")]   // not linked to anyone
    public void Arriving_from_a_scout_with_nothing_to_pay_says_so(string scoutId)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/invoices/family-payment?scoutId={scoutId}");

        var page = Render<FamilyPayment>();

        Assert.Contains("no outstanding invoices", page.Find(".alert-info").TextContent);
        Assert.Empty(page.FindAll("tbody tr"));
    }

    [Fact]
    public void An_invoice_billed_to_the_guardian_is_listed_with_the_family_and_paid_by_its_owner()
    {
        var adult = new InvoiceEntity
        {
            PartitionKey = "maria", RowKey = "inv-adult", InvoiceId = "inv-adult", GuardianId = "maria", GuardianName = "Maria Rivera",
            Description = "Summer camp, adult", TotalAmount = 320, AmountDueFromFamily = 320, StatusValue = InvoiceStatus.Outstanding,
        };
        Services.GetRequiredService<IInvoiceRepository>().GetPageAsync(InvoiceStatus.Outstanding, null, null, Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<InvoiceEntity>([Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", due: 175), adult], 0));

        var page = RenderForMaria();

        Assert.Equal(2, page.FindAll("tbody tr").Count);
        Assert.Contains(page.FindAll("tbody tr"), r => r.TextContent.Contains("Maria Rivera") && r.TextContent.Contains("Summer camp, adult"));
        Assert.Equal("$495.00", page.Find("#paymentTotal").TextContent);

        page.Find("#reference").Input("1042");
        page.FindAll("button").Single(b => b.TextContent.Contains("Record family payment")).Click();

        _accounts.Received(1).RecordFamilyPaymentAsync(
            Arg.Is<IReadOnlyList<FamilyPaymentLine>>(lines => lines.Any(l => l.OwnerId == "maria" && l.InvoiceId == "inv-adult" && l.Amount == 320m)
                && lines.Any(l => l.OwnerId == "alex" && l.InvoiceId == "inv-camp")),
            InvoicePaymentMethod.Check, "1042", "Pat Treasurer");
    }

    [Fact]
    public void Unticking_an_invoice_takes_it_out_of_the_total()
    {
        var page = RenderForMaria();

        page.FindAll("tbody input[type=checkbox]")[2].Change(false);

        Assert.Equal("$220.00", page.Find("#paymentTotal").TextContent);
    }

    [Fact]
    public void An_amount_over_what_is_due_is_flagged_and_blocks_recording()
    {
        var page = RenderForMaria();

        page.FindAll("tbody input[data-money-input]")[1].Input("60.00");

        Assert.Contains("More than the $45.00 due.", page.Markup);
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Trim() == "Record family payment").HasAttribute("disabled"));
    }

    [Fact]
    public void Recording_sends_exactly_the_ticked_invoices_and_amounts()
    {
        var page = RenderForMaria();

        page.FindAll("tbody input[type=checkbox]")[1].Change(false);                // skip Campout food
        page.FindAll("tbody input[data-money-input]")[1].Input("10.00");            // partial on Patrol flag
        page.Find("#reference").Input("Check 1042");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Record family payment").Click();

        _accounts.Received(1).RecordFamilyPaymentAsync(
            Arg.Is<IReadOnlyList<FamilyPaymentLine>>(lines => lines.SequenceEqual(new FamilyPaymentLine[]
            {
                new("alex", "inv-camp", 175m),
                new("casey", "inv-flag", 10m),
            })),
            InvoicePaymentMethod.Check, "Check 1042", "Pat Treasurer");
        Assert.Contains("Recorded $185.00 check payment from Maria Rivera across 2 invoices (Check 1042).", page.Markup);
    }

    [Fact]
    public void A_service_error_is_shown_to_the_treasurer()
    {
        _accounts.RecordFamilyPaymentAsync(default!, default!, default, default!).ReturnsForAnyArgs<Task<string>>(
            _ => throw new InvalidOperationException("Casey Rivera's invoice \"Campout food\" is paid, not outstanding."));
        var page = RenderForMaria();

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Record family payment").Click();

        Assert.Contains("is paid, not outstanding", page.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Venmo_is_recorded_with_its_own_reference_label()
    {
        var page = RenderForMaria();

        page.Find("#paymentMethod").Change(InvoicePaymentMethod.Venmo);
        Assert.Equal("Venmo username or transaction ID", page.Find("label[for=reference]").TextContent);
        page.Find("#reference").Input("@rivera-family");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Record family payment").Click();

        _accounts.Received(1).RecordFamilyPaymentAsync(Arg.Any<IReadOnlyList<FamilyPaymentLine>>(), InvoicePaymentMethod.Venmo, "@rivera-family", "Pat Treasurer");
        Assert.Contains("Recorded $238.00 Venmo payment", page.Markup);
    }

    [Fact]
    public void Other_cannot_be_recorded_until_it_says_how_it_was_paid()
    {
        var page = RenderForMaria();
        IElement RecordButton() => page.FindAll("button").Single(b => b.TextContent.Trim() == "Record family payment");

        page.Find("#paymentMethod").Change(InvoicePaymentMethod.Other);
        Assert.True(RecordButton().HasAttribute("disabled"));

        page.Find("#reference").Input("Zelle");
        Assert.False(RecordButton().HasAttribute("disabled"));
    }

    [Fact]
    public void Print_invoice_link_follows_the_ticked_invoices_and_opens_a_new_tab()
    {
        var page = RenderForMaria();

        page.FindAll("tbody input[type=checkbox]")[1].Change(false);   // untick Campout food

        var print = page.Find("#printFamilyInvoice");
        Assert.Equal("/admin/invoices/family-payment/print?guardianId=maria&invoices=inv-camp%2Cinv-flag", print.GetAttribute("href"));
        Assert.Equal("_blank", print.GetAttribute("target"));
    }

    [Fact]
    public void No_print_link_when_nothing_is_ticked()
    {
        var page = RenderForMaria();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            page.FindAll("tbody input[type=checkbox]").First(cb => cb.HasAttribute("checked")).Change(false);
        }

        Assert.Empty(page.FindAll("#printFamilyInvoice"));
    }

    private static InvoiceEntity Invoice(string id, string scoutId, string scoutName, string description, decimal due,
        string status = InvoiceStatus.Outstanding) => new()
    {
        PartitionKey = scoutId,
        RowKey = id,
        InvoiceId = id,
        ScoutId = scoutId,
        ScoutName = scoutName,
        Description = description,
        TotalAmount = due,
        AmountDueFromFamily = due,
        StatusValue = status,
        DueDate = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
    };
}
