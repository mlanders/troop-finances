namespace BsaTroopFinances.Models;

/// <summary>Dashboard/reports summary numbers -- built by IReportingService by scanning/filtering
/// the relevant tables in memory (see that service's doc comment for the scan-cost tradeoff).</summary>
public record ReportSummaryModel(
    int PendingReimbursementCount,
    decimal PendingReimbursementTotal,
    int OutstandingInvoiceCount,
    decimal OutstandingInvoiceTotal,
    IReadOnlyList<(string ScoutId, string ScoutName, decimal Balance)> ScoutBalances,
    IReadOnlyList<(string EventId, string EventName, DateTimeOffset EventDate, decimal? TotalRaised, decimal? TotalExpenses, decimal AllocatedTotal)> FundraiserTotals,
    DonationSummary Donations);

/// <summary>Donations received over the last 12 months. Money totals split by whether the donor
/// restricted the gift; goods are counted separately at their estimated value.</summary>
public record DonationSummary(
    int Count,
    decimal Total,
    decimal UnrestrictedTotal,
    decimal RestrictedTotal,
    decimal InKindTotal,
    decimal CreditedToScouts,
    int ThankYousOwed);
