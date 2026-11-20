using System.Xml.Linq;
using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The PLC and committee report: month-end figures rebuilt from the records, grouped
/// without names, and drawn as charts on the page and in the PDF.</summary>
public class TroopReportServiceTests
{
    private readonly ITroopBankRepository _bank = Substitute.For<ITroopBankRepository>();
    private readonly IScoutLedgerRepository _ledger = Substitute.For<IScoutLedgerRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly IReimbursementRequestRepository _reimbursements = Substitute.For<IReimbursementRequestRepository>();
    private readonly TroopReportService _service;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 3, 15);

    public TroopReportServiceTests()
    {
        _service = new TroopReportService(_bank, _ledger, _invoices, _payments, _reimbursements);
        _bank.GetAccountsAsync().Returns(new List<TroopAccountEntity>());
        _bank.GetAllTransactionsAsync().Returns(new List<TroopTransactionEntity>());
        _ledger.GetAllAsync().Returns(new List<ScoutLedgerEntryEntity>());
        _invoices.GetAllAsync().Returns(new List<InvoiceEntity>());
        _payments.GetPaidBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>()).Returns(new List<InvoicePaymentEntity>());
        _reimbursements.GetAllAsync().Returns(new List<ReimbursementRequestEntity>());
    }

    /// <summary>Midday Pacific, so the calendar day is the same whatever the server's zone.</summary>
    private static DateTimeOffset On(int month, int day) => new(2026, month, day, 20, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset CalendarDate(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    private TroopReportPoint At(TroopReport report, int month, int day) => report.Points.Single(p => p.Date == new DateOnly(2026, month, day));

    [Fact]
    public void Points_are_the_start_each_month_end_and_the_end()
    {
        Assert.Equal(
            [new(2026, 1, 1), new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 15)],
            TroopReportService.Dates(From, To));
        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28)], TroopReportService.Dates(new(2026, 1, 31), new(2026, 2, 28)));
        Assert.Equal([new(2026, 5, 5)], TroopReportService.Dates(new(2026, 5, 5), new(2026, 5, 5)));
    }

    [Fact]
    public async Task Bank_balances_start_from_the_opening_balance_and_not_before_the_account_opened()
    {
        _bank.GetAccountsAsync().Returns(new List<TroopAccountEntity>
        {
            new() { RowKey = "chk", Name = "Checking", IsActive = true, OpeningBalance = 1000, OpeningDate = CalendarDate(1, 15) },
            new() { RowKey = "ven", Name = "Venmo", IsActive = true, OpeningBalance = 0, OpeningDate = CalendarDate(2, 10) },
        });
        _bank.GetAllTransactionsAsync().Returns(new List<TroopTransactionEntity>
        {
            new() { PartitionKey = "chk", Date = CalendarDate(1, 20), Amount = -200 },
            new() { PartitionKey = "chk", Date = CalendarDate(3, 1), Amount = 500 },
            new() { PartitionKey = "ven", Date = CalendarDate(2, 11), Amount = 45 },
        });

        var report = await _service.GetAsync(From, To);

        Assert.Null(At(report, 1, 1).Bank);
        Assert.Null(At(report, 1, 1).FreeToSpend);
        Assert.Equal(800, At(report, 1, 31).Bank);
        Assert.False(At(report, 1, 31).AccountBalances.ContainsKey("ven"));
        Assert.Equal(845, At(report, 2, 28).Bank);
        Assert.Equal(1345, report.Current.Bank);
        Assert.Equal(1300, report.Current.AccountBalances["chk"]);
    }

    [Fact]
    public async Task Money_held_for_scouts_adds_credits_and_takes_off_debits_by_date()
    {
        _ledger.GetAllAsync().Returns(new List<ScoutLedgerEntryEntity>
        {
            new() { PartitionKey = "alex", CreatedUtc = On(1, 10), EntryTypeValue = LedgerEntryType.Credit, Amount = 300 },
            new() { PartitionKey = "sam", CreatedUtc = On(2, 5), EntryTypeValue = LedgerEntryType.Credit, Amount = 100 },
            new() { PartitionKey = "alex", CreatedUtc = On(3, 2), EntryTypeValue = LedgerEntryType.Debit, Amount = 120 },
        });

        var report = await _service.GetAsync(From, To);

        Assert.Equal([0, 300, 400, 280], report.Points.Select(p => p.Scouts));
    }

    [Fact]
    public async Task An_invoice_counts_what_was_still_owed_at_each_date()
    {
        _invoices.GetAllAsync().Returns(new List<InvoiceEntity>
        {
            // $100 billed in January, $60 paid in February, $40 still owed.
            new() { InvoiceId = "camp", Description = "Summer camp", IssuedUtc = On(1, 10), AmountDueFromFamily = 40, StatusValue = InvoiceStatus.Outstanding },
            // Voided in February: owed until then, never after.
            new() { InvoiceId = "shirt", Description = "T-shirt", IssuedUtc = On(1, 12), AmountDueFromFamily = 15, StatusValue = InvoiceStatus.Voided, VoidedUtc = On(2, 3) },
            // Paid in full in March.
            new() { InvoiceId = "book", Description = "summer  CAMP", IssuedUtc = On(2, 20), AmountDueFromFamily = 0, StatusValue = InvoiceStatus.Paid, PaidUtc = On(3, 5) },
        });
        _payments.GetPaidBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>()).Returns(new List<InvoicePaymentEntity>
        {
            new() { InvoiceId = "camp", PaidUtc = On(2, 14), Amount = 60 },
            new() { InvoiceId = "book", PaidUtc = On(3, 5), Amount = 25 },
        });

        var report = await _service.GetAsync(From, To);

        Assert.Equal(115, At(report, 1, 31).InvoicesOutstanding);
        Assert.Equal(65, At(report, 2, 28).InvoicesOutstanding);
        Assert.Equal(40, report.Current.InvoicesOutstanding);

        var group = Assert.Single(report.InvoiceGroups);
        Assert.Equal(("Summer camp", 1, 40m), (group.Description, group.Count, group.Amount));
    }

    [Fact]
    public async Task A_reimbursement_is_owed_until_paid_or_turned_down_and_split_by_approval()
    {
        _reimbursements.GetAllAsync().Returns(new List<ReimbursementRequestEntity>
        {
            new() { Description = "Campout food", AmountRequested = 80, SubmittedUtc = On(1, 5), StatusValue = ReimbursementStatus.Paid, ReviewedUtc = On(1, 20), PaidUtc = On(2, 10) },
            new() { Description = "Campout food", AmountRequested = 30, SubmittedUtc = On(2, 1), StatusValue = ReimbursementStatus.Approved, ReviewedUtc = On(3, 1) },
            new() { Description = "Patches", AmountRequested = 12, SubmittedUtc = On(2, 2), StatusValue = ReimbursementStatus.Rejected, ReviewedUtc = On(3, 3) },
            new() { Description = "Propane", AmountRequested = 20, SubmittedUtc = On(3, 10), StatusValue = ReimbursementStatus.Pending },
        });

        var report = await _service.GetAsync(From, To);

        Assert.Equal((0m, 80m), (At(report, 1, 31).AwaitingApproval, At(report, 1, 31).ApprovedUnpaid));
        Assert.Equal((42m, 0m), (At(report, 2, 28).AwaitingApproval, At(report, 2, 28).ApprovedUnpaid));
        Assert.Equal((20m, 30m), (report.Current.AwaitingApproval, report.Current.ApprovedUnpaid));

        Assert.Equal(["Campout food", "Propane"], report.ReimbursementGroups.Select(g => g.Description));
        Assert.Equal((1, 30m, 0m), (report.ReimbursementGroups[0].Count, report.ReimbursementGroups[0].Amount, report.ReimbursementGroups[0].AwaitingApproval));
    }

    [Fact]
    public async Task Free_to_spend_is_the_bank_less_scout_money_and_reimbursements_owed()
    {
        _bank.GetAccountsAsync().Returns(new List<TroopAccountEntity> { new() { RowKey = "chk", Name = "Checking", IsActive = true, OpeningBalance = 2000, OpeningDate = CalendarDate(1, 1) } });
        _ledger.GetAllAsync().Returns(new List<ScoutLedgerEntryEntity> { new() { CreatedUtc = On(1, 2), EntryTypeValue = LedgerEntryType.Credit, Amount = 700 } });
        _reimbursements.GetAllAsync().Returns(new List<ReimbursementRequestEntity> { new() { Description = "Gas", AmountRequested = 50, SubmittedUtc = On(1, 3) } });

        var report = await _service.GetAsync(From, To);

        Assert.Equal(1250, report.Current.FreeToSpend);
        Assert.Equal(["Checking", "In the bank", "Held for scouts", "Reimbursements owed", "Free to spend", "Invoices still to be paid"],
            TroopReportPdf.Lines(report).Select(l => l.Name));
        Assert.Equal(-700, TroopReportPdf.Lines(report).Single(l => l.Name == "Held for scouts").End);
    }

    [Fact]
    public async Task Reversed_dates_are_swapped()
    {
        var report = await _service.GetAsync(To, From);

        Assert.Equal((From, To), (report.From, report.To));
    }
}

public class TrendChartTests
{
    private static readonly IReadOnlyList<DateOnly> Months = [new(2025, 12, 31), new(2026, 1, 31), new(2026, 2, 28)];

    [Fact]
    public void A_line_chart_is_valid_svg_with_a_point_per_value_and_a_legend()
    {
        var svg = TrendChart.Line("Money", Months,
        [
            new("In the bank", "#003F87", [null, 1200m, 1500m]),
            new("Held for scouts", "#E0A100", [300m, 350m, 400m], Dashed: true),
        ]);

        var root = XElement.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(5, root.Descendants(ns + "circle").Count());
        Assert.Equal(2, root.Descendants(ns + "polyline").Count());
        Assert.Contains(root.Descendants(ns + "text"), t => t.Value == "In the bank");
        Assert.Contains(root.Descendants(ns + "text"), t => t.Value == "2026");
    }

    [Fact]
    public void Charts_with_one_date_or_nothing_but_zeros_still_draw()
    {
        XElement.Parse(TrendChart.Line("Empty", [new(2026, 5, 5)], [new("Bank", "#000", [0m])]));
        XElement.Parse(TrendChart.StackedBars("Empty", Months, [new("Checking", "#000", [0m, 0m, null])]));
    }

    [Fact]
    public void Stacked_bars_draw_a_block_per_nonzero_value()
    {
        var svg = TrendChart.StackedBars("Accounts", Months,
        [
            new("Checking", "#003F87", [1000m, 1100m, 1200m]),
            new("Venmo", "#E0A100", [null, 50m, -20m]),
        ]);

        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(5 + 2, XElement.Parse(svg).Descendants(ns + "rect").Count());   // five bars plus two legend keys
    }

    [Fact]
    public void An_interactive_chart_has_a_hover_column_per_date_carrying_its_values()
    {
        ChartSeries[] series =
        [
            new("Troop checking", "#003F87", [1000m, 1100m, 1200.5m]),
            new("Venmo", "#E0A100", [null, 50m, -20m]),
        ];
        XNamespace ns = "http://www.w3.org/2000/svg";

        var columns = XElement.Parse(TrendChart.StackedBars("Accounts", Months, series, interactive: true))
            .Descendants(ns + "g").Where(g => (string?)g.Attribute("class") == "chart-column").ToList();

        Assert.Equal(3, columns.Count);
        Assert.Equal("Feb 28, 2026", (string?)columns[2].Attribute("data-label"));
        var rows = System.Text.Json.JsonDocument.Parse((string)columns[2].Attribute("data-values")!).RootElement;
        Assert.Equal(["Troop checking", "Venmo", "Total"], rows.EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(["$1,200.50", "-$20.00", "$1,180.50"], rows.EnumerateArray().Select(r => r.GetProperty("value").GetString()));
        Assert.Contains("Venmo —", (string?)columns[0].Attribute("aria-label"));      // no data yet that month

        var lineColumns = XElement.Parse(TrendChart.Line("Money", Months, series, interactive: true))
            .Descendants(ns + "g").Where(g => (string?)g.Attribute("class") == "chart-column").ToList();
        Assert.Equal(2, lineColumns[1].Descendants(ns + "circle").Count());            // a highlight dot per line with a value
    }

    [Fact]
    public void The_printed_charts_have_no_hover_columns()
    {
        Assert.DoesNotContain("chart-column", TrendChart.Line("Money", Months, [new("Bank", "#000", [1m, 2m, 3m])]));
        Assert.DoesNotContain("chart-column", TrendChart.StackedBars("Accounts", Months, [new("Bank", "#000", [1m, 2m, 3m])]));
    }

    [Theory]
    [InlineData(0, 1234, 0, 1500, 500)]
    [InlineData(-80, 900, -250, 1000, 250)]
    [InlineData(0, 0, 0, 100, 25)]
    public void The_axis_runs_in_round_steps_and_always_includes_zero(decimal min, decimal max, decimal low, decimal high, decimal step)
    {
        Assert.Equal((low, high, step), TrendChart.Scale(min, max));
    }

    [Theory]
    [InlineData(2500, "$2.5k")]
    [InlineData(-300, "-$300")]
    [InlineData(0, "$0")]
    public void Axis_labels_are_short(decimal value, string label)
    {
        Assert.Equal(label, TrendChart.Dollars(value));
    }
}

public class TroopFinancesPageTests : AdminPageTestBase
{
    private static TroopReport Report()
    {
        var accounts = new Dictionary<string, decimal> { ["chk"] = 2400 };
        return new TroopReport(
            new(2025, 10, 31), new(2026, 10, 31),
            [new("chk", "Troop checking", true)],
            [
                new(new(2025, 10, 31), new Dictionary<string, decimal>(), null, 500, 0, 0, 100),
                new(new(2026, 10, 31), accounts, 2400, 650, 40, 25, 180),
            ],
            [new("Summer camp", 3, 180, new(2026, 9, 1))],
            [new("Campout food", 2, 65, new(2026, 10, 2), 40)]);
    }

    [Fact]
    public void The_report_shows_figures_charts_and_groups_without_names()
    {
        SignInAs(AdminRole.Reader);
        Fake<ITroopReportService>().GetAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>()).Returns(Report());

        var page = Render<TroopFinances>();

        Assert.Contains("$2,400.00", page.Find("#figures").TextContent);
        Assert.Contains("$1,685.00", page.Find("#figures").TextContent);    // 2,400 - 650 - 65
        Assert.NotNull(page.Find("#trendChart svg"));
        Assert.NotNull(page.Find("#accountsChart svg"));
        Assert.Contains("Summer camp", page.Find("#invoiceGroups").TextContent);
        Assert.Contains("$40.00", page.Find("#reimbursementGroups").TextContent);
        Assert.Equal("/admin/reports/troop/print?from=2025-10-31&to=2026-10-31", page.Find("#printReport").GetAttribute("href"));
    }

    [Fact]
    public void Changing_the_dates_reloads_the_report()
    {
        SignInAs(AdminRole.Reader);
        var service = Fake<ITroopReportService>();
        service.GetAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>()).Returns(Report());
        var page = Render<TroopFinances>();

        page.Find("#fromDate").Change("2026-01-01");

        service.Received().GetAsync(new DateOnly(2026, 1, 1), Arg.Any<DateOnly?>());
    }
}

public class TroopReportPdfTests
{
    [Fact]
    public void The_pdf_is_two_pages_with_its_charts()
    {
        var accounts = new Dictionary<string, decimal> { ["chk"] = 2400, ["ven"] = 60 };
        var report = new TroopReport(
            new(2025, 10, 31), new(2026, 10, 31),
            [new("chk", "Troop checking", true), new("ven", "Venmo", true)],
            TroopReportService.Dates(new(2025, 10, 31), new(2026, 10, 31))
                .Select((d, i) => new TroopReportPoint(d, accounts, 2460 + i * 10, 600 + i * 5, 20, 10, 150)).ToList(),
            [new("Summer camp", 3, 180, new(2026, 9, 1)), new("T-shirt", 2, 30, new(2026, 8, 1))],
            [new("Campout food", 2, 30, new(2026, 10, 2), 20)]);

        var pdf = TroopReportPdf.Create(TestTroop.Info, report, new DateTime(2026, 10, 31));

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
#pragma warning disable CA1416 // PDFium (used here only to read the result back) ships for the platforms the tests run on.
        Assert.Equal(2, PDFtoImage.Conversion.GetPageCount(pdf));
#pragma warning restore CA1416
    }
}
