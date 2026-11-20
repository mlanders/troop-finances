using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;

namespace BsaTroopFinances.Services;

/// <summary>
/// Dashboard and report numbers. Counts and totals are computed by the database (COUNT/SUM over
/// indexed columns) rather than by loading whole tables, so the dashboard stays fast however many
/// years of history accumulate. The monthly report reads only rows inside the chosen months.
/// </summary>
public class ReportingService(
    IReimbursementRequestRepository reimbursementRepository,
    IInvoiceRepository invoiceRepository,
    IScoutRepository scoutRepository,
    IScoutLedgerRepository ledgerRepository,
    IFundraiserEventRepository fundraiserRepository,
    IInvoicePaymentRepository invoicePaymentRepository,
    IDonationRepository donationRepository) : IReportingService
{
    public async Task<IReadOnlyList<MonthlyReportRow>> GetMonthlyAsync(DateOnly firstMonth, DateOnly lastMonth)
    {
        var first = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var last = new DateOnly(lastMonth.Year, lastMonth.Month, 1);
        if (last < first)
        {
            (first, last) = (last, first);
        }

        // Month boundaries are local (Pacific) midnights, matching how every date in the app is shown.
        var from = LocalMidnight(first);
        var to = LocalMidnight(last.AddMonths(1));
        static DateOnly MonthOf(DateTimeOffset when)
        {
            var local = when.ToLocalTime();
            return new DateOnly(local.Year, local.Month, 1);
        }

        var invoiced = (await invoiceRepository.GetAllAsync(from, to.AddTicks(-1)))
            .Where(i => i.StatusValue != InvoiceStatus.Voided)
            .ToLookup(i => MonthOf(i.IssuedUtc));
        var payments = (await invoicePaymentRepository.GetPaidBetweenAsync(from, to))
            .ToLookup(p => MonthOf(p.PaidUtc));
        var submitted = (await reimbursementRepository.GetAllAsync(from, to.AddTicks(-1)))
            .ToLookup(r => MonthOf(r.SubmittedUtc));
        var paid = (await reimbursementRepository.GetPaidBetweenAsync(from, to))
            .ToLookup(r => MonthOf(r.PaidUtc!.Value));
        // A donation's date is a calendar date (midnight UTC), so its month is the UTC month.
        var donations = (await donationRepository.GetReceivedBetweenAsync(first, last.AddMonths(1)))
            .ToLookup(d => new DateOnly(d.ReceivedDate.UtcDateTime.Year, d.ReceivedDate.UtcDateTime.Month, 1));

        var rows = new List<MonthlyReportRow>();
        for (var month = last; month >= first; month = month.AddMonths(-1))
        {
            rows.Add(new MonthlyReportRow(
                month,
                invoiced[month].Count(),
                invoiced[month].Sum(i => i.TotalAmount),
                payments[month]
                    .GroupBy(p => InvoicePaymentMethod.IsValid(p.Method) ? p.Method : InvoicePaymentMethod.Other)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount)),
                submitted[month].Count(),
                submitted[month].Sum(r => r.AmountRequested),
                paid[month].Count(),
                paid[month].Sum(r => r.AmountRequested),
                donations[month].Count(),
                donations[month].Sum(d => d.Amount)));
        }

        return rows;
    }

    private static DateTimeOffset LocalMidnight(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
    }

    public async Task<ReportSummaryModel> GetSummaryAsync()
    {
        var (pendingCount, pendingTotal) = await reimbursementRepository.GetStatusSummaryAsync(ReimbursementStatus.Pending);
        var (outstandingCount, outstandingTotal) = await invoiceRepository.GetOutstandingSummaryAsync();

        var scouts = await scoutRepository.GetActiveAsync();
        var scoutBalances = scouts
            .Select(s => (s.ScoutId, s.FullName, Balance: s.CachedBalance))
            .OrderByDescending(s => s.Balance)
            .ToList();

        var fundraiserCutoff = DateTimeOffset.UtcNow.AddYears(-1);
        var recentEvents = (await fundraiserRepository.GetAllAsync())
            .Where(e => e.EventDate >= fundraiserCutoff)
            .ToList();
        var allocated = await ledgerRepository.GetTotalsBySourceAsync(
            LedgerSourceType.FundraiserAllocation, recentEvents.Select(e => e.EventId).ToList());
        var fundraiserTotals = recentEvents
            .Select(e => (e.EventId, e.Name, e.EventDate, e.TotalRaised, e.TotalExpenses, Total: allocated.GetValueOrDefault(e.EventId)))
            .OrderByDescending(e => e.EventDate)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var recentDonations = await donationRepository.GetReceivedBetweenAsync(today.AddYears(-1).AddDays(1), today.AddDays(1));
        var money = recentDonations.Where(d => !d.IsInKind).ToList();
        var credited = await ledgerRepository.GetTotalsBySourceAsync(
            LedgerSourceType.Donation, money.Select(d => d.DonationId).ToList());
        var donationSummary = new DonationSummary(
            recentDonations.Count,
            recentDonations.Sum(d => d.Amount),
            money.Where(d => !d.IsRestricted).Sum(d => d.Amount),
            money.Where(d => d.IsRestricted).Sum(d => d.Amount),
            recentDonations.Where(d => d.IsInKind).Sum(d => d.Amount),
            credited.Values.Sum(),
            recentDonations.Count(d => d.NeedsAcknowledgment));

        return new ReportSummaryModel(pendingCount, pendingTotal, outstandingCount, outstandingTotal, scoutBalances, fundraiserTotals, donationSummary);
    }
}
