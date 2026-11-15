namespace BsaTroopFinances.Models;

/// <summary>One calendar month (Pacific time) of money in and out, for the monthly detail report.</summary>
public record MonthlyReportRow(
    DateOnly Month,
    int InvoicedCount,
    decimal InvoicedTotal,
    IReadOnlyDictionary<string, decimal> PaymentsByMethod,
    int ReimbursementsSubmittedCount,
    decimal ReimbursementsSubmittedTotal,
    int ReimbursementsPaidCount,
    decimal ReimbursementsPaidTotal,
    int DonationsCount = 0,
    decimal DonationsTotal = 0)
{
    public decimal PaymentsTotal => PaymentsByMethod.Values.Sum();

    public decimal PaymentsFor(string method) => PaymentsByMethod.GetValueOrDefault(method);
}
