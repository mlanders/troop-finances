using BsaTroopFinances.Models;

namespace BsaTroopFinances.Services;

public interface IReportingService
{
    Task<ReportSummaryModel> GetSummaryAsync();

    /// <summary>
    /// Month-by-month totals from <paramref name="firstMonth"/> through <paramref name="lastMonth"/>
    /// (inclusive, Pacific-time calendar months; only the year and month are used), newest first,
    /// with a row for every month even when nothing happened:
    ///   invoiced -- invoices issued that month, voided ones excluded;
    ///   payments -- money received against invoices that month, by payment method;
    ///   reimbursements submitted -- requests submitted that month, any status;
    ///   reimbursements paid -- requests marked paid that month.
    /// </summary>
    Task<IReadOnlyList<MonthlyReportRow>> GetMonthlyAsync(DateOnly firstMonth, DateOnly lastMonth);
}
