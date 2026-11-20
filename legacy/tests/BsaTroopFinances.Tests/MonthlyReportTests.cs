using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using BsaTroopFinances.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The real ReportingService.GetMonthlyAsync with faked storage. Dates sit mid-month so
/// the results don't depend on the test machine's time zone.</summary>
public class MonthlyReportServiceTests
{
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly IReimbursementRequestRepository _reimbursements = Substitute.For<IReimbursementRequestRepository>();
    private readonly IDonationRepository _donations = Substitute.For<IDonationRepository>();
    private readonly ReportingService _service;

    private static DateTimeOffset Mid(int year, int month) => new(year, month, 15, 18, 0, 0, TimeSpan.Zero);

    public MonthlyReportServiceTests()
    {
        _service = new ReportingService(_reimbursements, _invoices, Substitute.For<IScoutRepository>(),
            Substitute.For<IScoutLedgerRepository>(), Substitute.For<IFundraiserEventRepository>(), _payments, _donations);

        _invoices.GetAllAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>()).Returns(new List<InvoiceEntity>
        {
            new() { TotalAmount = 275, IssuedUtc = Mid(2026, 9), StatusValue = InvoiceStatus.Outstanding },
            new() { TotalAmount = 45, IssuedUtc = Mid(2026, 9), StatusValue = InvoiceStatus.Paid },
            new() { TotalAmount = 30, IssuedUtc = Mid(2026, 9), StatusValue = InvoiceStatus.Voided },
            new() { TotalAmount = 60, IssuedUtc = Mid(2026, 7), StatusValue = InvoiceStatus.Paid },
        });
        _payments.GetPaidBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>()).Returns(new List<InvoicePaymentEntity>
        {
            new() { Amount = 100, Method = InvoicePaymentMethod.Check, PaidUtc = Mid(2026, 9) },
            new() { Amount = 25, Method = InvoicePaymentMethod.Venmo, PaidUtc = Mid(2026, 9) },
            new() { Amount = 20, Method = InvoicePaymentMethod.Venmo, PaidUtc = Mid(2026, 9) },
            new() { Amount = 40, Method = InvoicePaymentMethod.ScoutFunds, PaidUtc = Mid(2026, 9) },
            new() { Amount = 60, Method = InvoicePaymentMethod.Cash, PaidUtc = Mid(2026, 7) },
        });
        // "Paid in range" now comes from its own query, which the database filters by PaidUtc.
        var paidHistory = new List<ReimbursementRequestEntity>
        {
            new() { AmountRequested = 64.50m, StatusValue = ReimbursementStatus.Paid, SubmittedUtc = Mid(2026, 3), PaidUtc = Mid(2026, 9) },
            new() { AmountRequested = 10, StatusValue = ReimbursementStatus.Paid, SubmittedUtc = Mid(2025, 1), PaidUtc = Mid(2025, 2) },
        };
        _reimbursements.GetPaidBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>()).Returns(call =>
            paidHistory.Where(r => r.PaidUtc >= call.ArgAt<DateTimeOffset>(0) && r.PaidUtc < call.ArgAt<DateTimeOffset>(1)).ToList());
        // Received dates are calendar dates (midnight UTC); Sept 1 belongs to September in every time zone.
        _donations.GetReceivedBetweenAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(new List<DonationEntity>
        {
            new() { Amount = 300, Method = InvoicePaymentMethod.Check, ReceivedDate = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) },
            new() { Amount = 180, Method = DonationMethod.InKind, ReceivedDate = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero) },
            new() { Amount = 50, Method = InvoicePaymentMethod.Cash, ReceivedDate = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero) },
        });
        _reimbursements.GetAllAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>()).Returns(new List<ReimbursementRequestEntity>
        {
            new() { AmountRequested = 19.99m, StatusValue = ReimbursementStatus.Approved, SubmittedUtc = Mid(2026, 9) },
            new() { AmountRequested = 12, StatusValue = ReimbursementStatus.Rejected, SubmittedUtc = Mid(2026, 9) },
            new() { AmountRequested = 42.18m, StatusValue = ReimbursementStatus.Pending, SubmittedUtc = Mid(2026, 8) },
        });
    }

    [Fact]
    public async Task Every_month_in_range_gets_a_row_newest_first()
    {
        var rows = await _service.GetMonthlyAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1));

        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1)],
            rows.Select(r => r.Month));
        var june = rows[3];
        Assert.Equal(0, june.InvoicedCount);
        Assert.Equal(0, june.PaymentsTotal);
    }

    [Fact]
    public async Task Invoiced_excludes_voided_invoices()
    {
        var september = (await _service.GetMonthlyAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1)))[0];

        Assert.Equal(2, september.InvoicedCount);
        Assert.Equal(320m, september.InvoicedTotal);
    }

    [Fact]
    public async Task Payments_are_split_by_method()
    {
        var rows = await _service.GetMonthlyAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1));

        var september = rows[0];
        Assert.Equal(100m, september.PaymentsFor(InvoicePaymentMethod.Check));
        Assert.Equal(45m, september.PaymentsFor(InvoicePaymentMethod.Venmo));
        Assert.Equal(40m, september.PaymentsFor(InvoicePaymentMethod.ScoutFunds));
        Assert.Equal(0m, september.PaymentsFor(InvoicePaymentMethod.Cash));
        Assert.Equal(185m, september.PaymentsTotal);
        Assert.Equal(60m, rows[2].PaymentsFor(InvoicePaymentMethod.Cash));
    }

    [Fact]
    public async Task Reimbursements_submitted_count_every_status_and_paid_go_by_the_month_paid()
    {
        var rows = await _service.GetMonthlyAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1));

        var september = rows[0];
        Assert.Equal(2, september.ReimbursementsSubmittedCount);
        Assert.Equal(31.99m, september.ReimbursementsSubmittedTotal);
        Assert.Equal(1, september.ReimbursementsPaidCount);          // submitted in March, paid in September
        Assert.Equal(64.50m, september.ReimbursementsPaidTotal);
        Assert.Equal(1, rows[1].ReimbursementsSubmittedCount);       // August pending
        Assert.Equal(1, rows.Sum(r => r.ReimbursementsPaidCount));      // the 2025 payment is out of range
    }

    [Fact]
    public async Task Donations_go_by_the_calendar_month_received()
    {
        var rows = await _service.GetMonthlyAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1));

        Assert.Equal(2, rows[0].DonationsCount);
        Assert.Equal(480m, rows[0].DonationsTotal);   // goods count at their estimated value
        Assert.Equal(1, rows[1].DonationsCount);
        Assert.Equal(50m, rows[1].DonationsTotal);
        await _donations.Received(1).GetReceivedBetweenAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task A_backwards_range_is_swapped()
    {
        var rows = await _service.GetMonthlyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 6, 1));

        Assert.Equal(new DateOnly(2026, 9, 1), rows[0].Month);
        Assert.Equal(new DateOnly(2026, 6, 1), rows[^1].Month);
    }
}

public class MonthlyReportPageTests : AdminPageTestBase
{
    private readonly IReportingService _reports;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public MonthlyReportPageTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedTime(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)));
        _reports = Fake<IReportingService>();
        _reports.GetMonthlyAsync(default, default).ReturnsForAnyArgs(call => Rows(call.ArgAt<DateOnly>(0), call.ArgAt<DateOnly>(1)));
        SignInAs(AdminRole.Reader);
    }

    private static IReadOnlyList<MonthlyReportRow> Rows(DateOnly from, DateOnly to)
    {
        var rows = new List<MonthlyReportRow>();
        for (var m = to; m >= from; m = m.AddMonths(-1))
        {
            rows.Add(new MonthlyReportRow(m, 1, 10m, new Dictionary<string, decimal> { [InvoicePaymentMethod.Venmo] = 5m }, 0, 0, 1, 2.5m, 1, 25m));
        }
        return rows;
    }

    [Fact]
    public void Defaults_to_the_last_12_months_including_this_one()
    {
        var page = Render<MonthlyReport>();

        _reports.Received(1).GetMonthlyAsync(new DateOnly(2025, 11, 1), new DateOnly(2026, 10, 1));
        Assert.Equal("2025-11", page.Find("#fromMonth").GetAttribute("value"));
        Assert.Equal("2026-10", page.Find("#toMonth").GetAttribute("value"));
        Assert.Equal(12, page.FindAll("tbody tr").Count);
        Assert.Equal("Oct 2026", page.Find("tbody tr td").TextContent);
    }

    [Fact]
    public void Totals_row_adds_up_the_months()
    {
        var page = Render<MonthlyReport>();

        var total = page.Find("tfoot tr").TextContent;
        Assert.Contains("$120.00", total);   // invoiced 12 x $10
        Assert.Contains("$60.00", total);    // Venmo 12 x $5
        Assert.Contains("$30.00", total);    // reimbursements paid 12 x $2.50
        Assert.Contains("$300.00", total);   // donations 12 x $25
    }

    [Fact]
    public void Changing_the_months_reloads_and_reset_returns_to_the_last_12()
    {
        var page = Render<MonthlyReport>();

        page.Find("#fromMonth").Change("2026-01");
        page.Find("#toMonth").Change("2026-03");

        _reports.Received(1).GetMonthlyAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1));
        Assert.Equal(3, page.FindAll("tbody tr").Count);

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Last 12 months").Click();
        Assert.Equal(12, page.FindAll("tbody tr").Count);
    }

    [Fact]
    public void A_range_over_five_years_is_trimmed_to_the_most_recent_60_months()
    {
        var page = Render<MonthlyReport>();

        page.Find("#fromMonth").Change("2015-01");

        _reports.Received(1).GetMonthlyAsync(new DateOnly(2021, 11, 1), new DateOnly(2026, 10, 1));
        Assert.Contains("most recent 60 months", page.Markup);
    }
}
