using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>The troop's position at one moment. Bank and FreeToSpend are null before the first
/// account's opening date, when the app doesn't know what was in the bank.</summary>
public record TroopReportPoint(
    DateOnly Date, IReadOnlyDictionary<string, decimal> AccountBalances, decimal? Bank, decimal Scouts,
    decimal AwaitingApproval, decimal ApprovedUnpaid, decimal InvoicesOutstanding)
{
    public decimal ReimbursementsOwed => AwaitingApproval + ApprovedUnpaid;

    /// <summary>What's in the bank that isn't already spoken for: money held for scouts and
    /// reimbursements still to pay out.</summary>
    public decimal? FreeToSpend => Bank - Scouts - ReimbursementsOwed;
}

/// <summary>An account as the report names it. Its balances are keyed by AccountId in each point.</summary>
public record TroopReportAccount(string AccountId, string Name, bool IsActive);

/// <summary>Charges or requests that share a description, without who they're for.</summary>
public record DescriptionGroup(string Description, int Count, decimal Amount, DateOnly Oldest, decimal AwaitingApproval = 0);

/// <summary>
/// The PLC and committee report. Points run from the start date through each month's end to the
/// end date; the last point is the position "now". Nothing in it names a scout or a family.
/// </summary>
public record TroopReport(
    DateOnly From, DateOnly To, IReadOnlyList<TroopReportAccount> Accounts, IReadOnlyList<TroopReportPoint> Points,
    IReadOnlyList<DescriptionGroup> InvoiceGroups, IReadOnlyList<DescriptionGroup> ReimbursementGroups)
{
    public TroopReportPoint Current => Points[^1];

    /// <summary>Accounts worth showing on their own: active ones, and closed ones still holding money.</summary>
    public IEnumerable<TroopReportAccount> ShownAccounts =>
        Accounts.Where(a => a.IsActive || Current.AccountBalances.GetValueOrDefault(a.AccountId) != 0);
}

public interface ITroopReportService
{
    /// <summary>The report for a period: as given, or the last 12 months up to today.</summary>
    Task<TroopReport> GetAsync(DateOnly? from, DateOnly? to);
}

/// <summary>
/// Rebuilds the troop's money at each month-end from the records that already exist: bank
/// registers, the append-only scout ledger, invoices with their payments, and reimbursement
/// requests with their review and payment dates. Each source is read once.
/// </summary>
public class TroopReportService(
    ITroopBankRepository bankRepository,
    IScoutLedgerRepository ledgerRepository,
    IInvoiceRepository invoiceRepository,
    IInvoicePaymentRepository paymentRepository,
    IReimbursementRequestRepository reimbursementRepository) : ITroopReportService
{
    public async Task<TroopReport> GetAsync(DateOnly? from, DateOnly? to)
    {
        var (start, end) = FamilyStatusService.Period(from, to, DateOnly.FromDateTime(DateTime.Now));

        var accounts = (await bankRepository.GetAccountsAsync()).OrderBy(a => a.OpeningDate).ThenBy(a => a.Name).ToList();
        var entries = (await bankRepository.GetAllTransactionsAsync()).ToLookup(t => t.AccountId);
        var ledger = await ledgerRepository.GetAllAsync();
        var invoices = await invoiceRepository.GetAllAsync();
        // Payments made after the start date are the only ones that change what was owed in the period.
        var laterPayments = (await paymentRepository.GetPaidBetweenAsync(LocalMidnight(start), DateTimeOffset.MaxValue))
            .ToLookup(p => p.InvoiceId);
        var requests = await reimbursementRepository.GetAllAsync();

        var points = Dates(start, end).Select(date =>
        {
            var balances = accounts
                .Where(a => CalendarDay(a.OpeningDate) <= date)
                .ToDictionary(a => a.AccountId, a => a.OpeningBalance + entries[a.AccountId].Where(t => CalendarDay(t.Date) <= date).Sum(t => t.Amount));
            var (awaiting, approved) = Owed(requests, date);
            return new TroopReportPoint(
                date,
                balances,
                balances.Count == 0 ? null : balances.Values.Sum(),
                ledger.Where(e => Day(e.CreatedUtc) <= date).Sum(e => e.SignedAmount),
                awaiting.Sum(r => r.AmountRequested),
                approved.Sum(r => r.AmountRequested),
                invoices.Sum(i => OwedOn(i, date, laterPayments[i.InvoiceId])));
        }).ToList();

        var invoiceGroups = invoices
            .Select(i => (Invoice: i, Owed: OwedOn(i, end, laterPayments[i.InvoiceId])))
            .Where(x => x.Owed > 0)
            .GroupBy(x => GroupKey(x.Invoice.Description), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DescriptionGroup(g.Key, g.Count(), g.Sum(x => x.Owed), g.Min(x => Day(x.Invoice.IssuedUtc))))
            .OrderByDescending(g => g.Amount).ThenBy(g => g.Description)
            .ToList();

        var (awaitingNow, approvedNow) = Owed(requests, end);
        var reimbursementGroups = awaitingNow.Select(r => (Request: r, Awaiting: true))
            .Concat(approvedNow.Select(r => (Request: r, Awaiting: false)))
            .GroupBy(x => GroupKey(x.Request.Description), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DescriptionGroup(
                g.Key, g.Count(), g.Sum(x => x.Request.AmountRequested), g.Min(x => Day(x.Request.SubmittedUtc)),
                g.Where(x => x.Awaiting).Sum(x => x.Request.AmountRequested)))
            .OrderByDescending(g => g.Amount).ThenBy(g => g.Description)
            .ToList();

        return new TroopReport(
            start, end,
            accounts.Select(a => new TroopReportAccount(a.AccountId, a.Name, a.IsActive)).ToList(),
            points, invoiceGroups, reimbursementGroups);
    }

    /// <summary>The start date, the end of every month in between, and the end date.</summary>
    public static IReadOnlyList<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly> { from };
        for (var monthEnd = EndOfMonth(from); monthEnd < to; monthEnd = EndOfMonth(monthEnd.AddDays(1)))
        {
            if (monthEnd > from)
            {
                dates.Add(monthEnd);
            }
        }

        if (to > from)
        {
            dates.Add(to);
        }

        return dates;
    }

    private static DateOnly EndOfMonth(DateOnly day) => new DateOnly(day.Year, day.Month, 1).AddMonths(1).AddDays(-1);

    /// <summary>What a family still owed on an invoice at the end of a day: what's owed today plus
    /// every payment made since. Nothing before it was issued or once it was voided.</summary>
    public static decimal OwedOn(InvoiceEntity invoice, DateOnly date, IEnumerable<InvoicePaymentEntity> laterPayments)
    {
        if (Day(invoice.IssuedUtc) > date || (invoice.VoidedUtc is { } voided && Day(voided) <= date))
        {
            return 0;
        }

        // Voiding leaves AmountDueFromFamily as it was, so it's still what was owed before the void.
        return invoice.AmountDueFromFamily + laterPayments.Where(p => Day(p.PaidUtc) > date).Sum(p => p.Amount);
    }

    /// <summary>Requests submitted by the end of a day and neither paid nor turned down by then,
    /// split by whether they had been approved yet.</summary>
    public static (List<ReimbursementRequestEntity> Awaiting, List<ReimbursementRequestEntity> Approved) Owed(
        IEnumerable<ReimbursementRequestEntity> requests, DateOnly date)
    {
        var awaiting = new List<ReimbursementRequestEntity>();
        var approved = new List<ReimbursementRequestEntity>();
        foreach (var request in requests)
        {
            var reviewedBy = request.ReviewedUtc is { } reviewed && Day(reviewed) <= date;
            if (Day(request.SubmittedUtc) > date
                || (request.PaidUtc is { } paid && Day(paid) <= date)
                || (request.StatusValue == ReimbursementStatus.Rejected && reviewedBy))
            {
                continue;
            }

            (reviewedBy && request.StatusValue != ReimbursementStatus.Pending ? approved : awaiting).Add(request);
        }

        return (awaiting, approved);
    }

    private static string GroupKey(string description) =>
        string.IsNullOrWhiteSpace(description) ? "(No description)" : string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The calendar day a moment falls on, in the server's time zone (as on the status sheets).</summary>
    private static DateOnly Day(DateTimeOffset moment) => DateOnly.FromDateTime(moment.LocalDateTime);

    /// <summary>A stored calendar date (midnight UTC).</summary>
    private static DateOnly CalendarDay(DateTimeOffset date) => DateOnly.FromDateTime(date.UtcDateTime);

    private static DateTimeOffset LocalMidnight(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
    }
}
