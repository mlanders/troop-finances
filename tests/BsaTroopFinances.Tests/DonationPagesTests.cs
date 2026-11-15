using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class DonationsPageTests : AdminPageTestBase
{
    private readonly IDonationService _service;

    public DonationsPageTests()
    {
        Fake<IDonationRepository>().GetAllAsync().Returns(new List<DonationEntity>
        {
            new() { RowKey = "big", DonorName = "Main Street Hardware", Amount = 300, Method = InvoicePaymentMethod.Check, ReceivedDate = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero) },
            new() { RowKey = "camp", DonorName = "The Nguyen family", Amount = 200, Method = InvoicePaymentMethod.Venmo, IsRestricted = true, Purpose = "Camp scholarship", ReceivedDate = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), AcknowledgedUtc = DateTimeOffset.UtcNow },
            new() { RowKey = "tents", DonorName = "Pat Morgan", Amount = 180, Method = DonationMethod.InKind, Reference = "Tents", ReceivedDate = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) },
        });
        Fake<IScoutLedgerRepository>().GetTotalsBySourceAsync(LedgerSourceType.Donation, Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new Dictionary<string, decimal> { ["camp"] = 200m });
        _service = Fake<IDonationService>();
    }

    private static IEnumerable<string> Donors(IRenderedComponent<Donations> page) =>
        page.FindAll("tbody tr td:nth-child(2) a").Select(a => a.TextContent);

    [Fact]
    public void A_reader_sees_the_gifts_but_cannot_record_or_credit()
    {
        SignInAs(AdminRole.Reader);
        var page = Render<Donations>();

        Assert.Equal(["Main Street Hardware", "The Nguyen family", "Pat Morgan"], Donors(page));
        Assert.DoesNotContain("Record a donation", page.Markup);
        Assert.DoesNotContain("Credit to scouts", page.Markup);
        Assert.DoesNotContain("Mark thank-you sent", page.Markup);
    }

    [Fact]
    public void Filters_show_gifts_needing_a_thank_you_or_restricted()
    {
        SignInAs(AdminRole.Writer);
        var page = Render<Donations>();

        page.FindAll("button.choice-btn").Single(b => b.TextContent == "Needs thank-you").Click();
        Assert.Equal(["Main Street Hardware", "Pat Morgan"], Donors(page));

        page.FindAll("button.choice-btn").Single(b => b.TextContent == "Restricted").Click();
        Assert.Equal(["The Nguyen family"], Donors(page));
    }

    [Fact]
    public void A_writer_can_mark_a_thank_you_sent_from_the_menu()
    {
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
        var page = Render<Donations>();

        page.FindAll("button.ellipsis-menu-item").First(b => b.TextContent.Contains("Mark thank-you sent")).Click();

        _service.Received(1).SetAcknowledgedAsync("big", true, "Pat Treasurer");
    }

    [Fact]
    public void Credit_to_scouts_is_offered_only_for_money_with_some_left()
    {
        SignInAs(AdminRole.Writer);
        var page = Render<Donations>();

        var creditLinks = page.FindAll("a.ellipsis-menu-item").Where(a => a.TextContent.Contains("Credit to scouts"))
            .Select(a => a.GetAttribute("href"));
        Assert.Equal(["/admin/donations/big/credit"], creditLinks);   // camp is fully credited; tents are goods
    }
}

public class DonationEditPageTests : AdminPageTestBase
{
    private readonly IDonationService _service;

    public DonationEditPageTests()
    {
        Fake<IDonationRepository>();
        _service = Fake<IDonationService>();
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
    }

    [Fact]
    public void Recording_a_restricted_gift_saves_what_was_entered()
    {
        var page = Render<DonationEdit>();

        page.Find("#donorName").Input("The Nguyen family");
        page.Find("#received").Change("2026-09-10");
        page.Find("#method").Change(InvoicePaymentMethod.Venmo);
        page.Find("#reference").Input("@nguyen-fam");
        page.Find("#amount").Change("200.00");
        page.FindAll("input[type=checkbox]")[1].Change(true);
        page.Find("#purpose").Input("Camp scholarship");
        page.FindAll("button").Single(b => b.TextContent == "Save").Click();

        _service.Received(1).RecordAsync(Arg.Is<DonationEntity>(d =>
            d.DonorName == "The Nguyen family" && d.Amount == 200m && d.Method == InvoicePaymentMethod.Venmo &&
            d.Reference == "@nguyen-fam" && d.IsRestricted && d.Purpose == "Camp scholarship" &&
            d.ReceivedDate == new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)), "Pat Treasurer");
    }

    [Fact]
    public void A_rule_the_service_rejects_is_shown()
    {
        _service.RecordAsync(default!, default!).ReturnsForAnyArgs<Task<DonationEntity>>(_ => throw new InvalidOperationException("Enter who the gift is from."));
        var page = Render<DonationEdit>();

        page.FindAll("button").Single(b => b.TextContent == "Save").Click();

        Assert.Contains("Enter who the gift is from.", page.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Goods_ask_for_an_estimated_value_and_cannot_be_credited()
    {
        var page = Render<DonationEdit>();

        page.Find("#method").Change(DonationMethod.InKind);

        Assert.Equal("Estimated value", page.Find("label[for=amount]").TextContent);
        Assert.Equal("What was given", page.Find("label[for=reference]").TextContent);
        Assert.DoesNotContain("Save and credit scouts", page.Markup);
    }
}

public class DonationCreditPageTests : AdminPageTestBase
{
    private readonly IDonationService _service;

    public DonationCreditPageTests()
    {
        Fake<IDonationRepository>().GetByIdAsync("gift").Returns(new DonationEntity
        {
            RowKey = "gift", DonorName = "Main Street Hardware", Amount = 100, Method = InvoicePaymentMethod.Check,
        });
        Fake<IScoutRepository>().GetActiveAsync().Returns(new List<ScoutEntity>
        {
            new() { RowKey = "alex", FullName = "Alex Rivera" },
            new() { RowKey = "casey", FullName = "Casey Rivera" },
            new() { RowKey = "sam", FullName = "Sam Patel" },
        });
        _service = Fake<IDonationService>();
        _service.GetCreditsAsync("gift").Returns(new List<ScoutLedgerEntryEntity> { new() { Amount = 10 } });
        SignInAs(AdminRole.Writer, name: "Pat Treasurer");
    }

    [Fact]
    public void An_even_split_of_what_is_left_credits_each_chosen_scout()
    {
        var page = Render<DonationCredit>(p => p.Add(c => c.Id, "gift"));
        Assert.Contains("$90.00", page.Markup);   // $100 less $10 already credited

        page.FindAll("input[type=checkbox]")[0].Change(true);
        page.FindAll("input[type=checkbox]")[1].Change(true);
        page.FindAll("button").Single(b => b.TextContent.StartsWith("Use all")).Click();
        page.FindAll("button").Single(b => b.TextContent == "Credit selected scouts").Click();

        _service.Received(1).CreditScoutsAsync("gift",
            Arg.Is<IReadOnlyList<DonationCreditShare>>(s => s.SequenceEqual(new DonationCreditShare[] { new("alex", 45m), new("casey", 45m) })),
            "", "Pat Treasurer");
    }

    [Fact]
    public void Entered_amounts_over_what_is_left_cannot_be_submitted()
    {
        var page = Render<DonationCredit>(p => p.Add(c => c.Id, "gift"));

        page.Find("#splitMode").Change("Amounts");
        page.FindAll("input[type=checkbox]")[2].Change(true);
        page.Find("input[aria-label='Amount for Sam Patel']").Change("95.00");

        Assert.Contains("more than the $90.00 left", page.Markup);
        Assert.True(page.FindAll("button").Single(b => b.TextContent == "Credit selected scouts").HasAttribute("disabled"));
    }
}

public class ShareSplitterTests
{
    [Fact]
    public void An_even_split_hands_out_leftover_cents_by_name()
    {
        var shares = ShareSplitter.Split(["Casey", "Alex", "Sam"], [1m, 1m, 1m], 100m, name => name);

        Assert.Equal([("Casey", 33.33m), ("Alex", 33.34m), ("Sam", 33.33m)], shares);
    }

    [Fact]
    public void A_weighted_split_is_proportional_and_adds_up()
    {
        var shares = ShareSplitter.Split(["Alex", "Jordan"], [6m, 4m], 250m, name => name);

        Assert.Equal([("Alex", 150m), ("Jordan", 100m)], shares);
    }

    [Fact]
    public void Nothing_to_split_or_a_zero_weight_gives_no_shares()
    {
        Assert.Empty(ShareSplitter.Split(["Alex"], [1m], 0m, name => name));
        Assert.Empty(ShareSplitter.Split(["Alex", "Sam"], [1m, 0m], 10m, name => name));
    }

    [Fact]
    public void A_negative_total_or_weight_or_mismatched_lists_give_no_shares()
    {
        Assert.Empty(ShareSplitter.Split(["Alex"], [1m], -5m, name => name));
        Assert.Empty(ShareSplitter.Split(["Alex", "Sam"], [1m, -1m], 10m, name => name));
        Assert.Empty(ShareSplitter.Split(["Alex", "Sam"], [1m], 10m, name => name));
        Assert.Empty(ShareSplitter.Split(Array.Empty<string>(), Array.Empty<decimal>(), 10m, name => name));
    }

    [Fact]
    public void Fewer_cents_than_people_go_to_the_first_names()
    {
        var shares = ShareSplitter.Split(["Sam", "Alex", "Casey"], [1m, 1m, 1m], 0.02m, name => name);

        Assert.Equal([("Sam", 0m), ("Alex", 0.01m), ("Casey", 0.01m)], shares);
    }

    [Fact]
    public void The_largest_rounding_losses_get_the_leftover_cents_first()
    {
        // Raw cents: 100 * 1/7 = 14.29, * 2/7 = 28.57, * 4/7 = 57.14 -> floors 14 + 28 + 57 = 99;
        // the 1 cent left goes to the biggest fraction (Jordan's .57), not alphabetical order.
        var shares = ShareSplitter.Split(["Alex", "Jordan", "Sam"], [1m, 2m, 4m], 1m, name => name);

        Assert.Equal([("Alex", 0.14m), ("Jordan", 0.29m), ("Sam", 0.57m)], shares);
    }

    [Theory]
    [InlineData("100", 3)]
    [InlineData("0.01", 4)]
    [InlineData("1234.57", 7)]
    [InlineData("999999.99", 13)]
    public void Shares_always_add_up_to_the_total_to_the_cent(string totalText, int people)
    {
        var total = decimal.Parse(totalText, System.Globalization.CultureInfo.InvariantCulture);
        var names = Enumerable.Range(1, people).Select(i => $"Scout {i}").ToList();
        var weights = Enumerable.Range(1, people).Select(i => (decimal)i * 1.5m).ToList();

        var shares = ShareSplitter.Split(names, weights, total, name => name);

        Assert.Equal(total, shares.Sum(s => s.Amount));
        Assert.All(shares, s => Assert.Equal(s.Amount, decimal.Round(s.Amount, 2)));
        Assert.All(shares, s => Assert.True(s.Amount >= 0));
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_split_whatever_the_order()
    {
        var first = ShareSplitter.Split(["Casey", "Alex", "Sam"], [1m, 1m, 1m], 100m, name => name);
        var reordered = ShareSplitter.Split(["Sam", "Casey", "Alex"], [1m, 1m, 1m], 100m, name => name);

        Assert.Equal(first.OrderBy(s => s.Item), reordered.OrderBy(s => s.Item));
    }
}
