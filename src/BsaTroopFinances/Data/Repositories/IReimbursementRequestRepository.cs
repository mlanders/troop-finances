using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IReimbursementRequestRepository
{
    /// <summary>A guardian's own submissions -- one partition query.</summary>
    Task<IReadOnlyList<ReimbursementRequestEntity>> GetBySubmitterAsync(string guardianId);

    /// <summary>One page of a guardian's own submissions, newest first, filtered in the database
    /// by status (null = all), with the total number of matches. For the portal's My Receipts.</summary>
    Task<PagedResult<ReimbursementRequestEntity>> GetSubmitterPageAsync(string guardianId, string? status, int page, int pageSize);

    /// <summary>How many of a guardian's submissions are in each status (statuses with none are absent).</summary>
    Task<IReadOnlyDictionary<string, int>> GetSubmitterStatusCountsAsync(string guardianId);

    /// <summary>Every request (optionally only those submitted in [submittedFrom, submittedTo]),
    /// newest first. For reports; the list page uses GetPageAsync instead.</summary>
    Task<IReadOnlyList<ReimbursementRequestEntity>> GetAllAsync(DateTimeOffset? submittedFrom = null, DateTimeOffset? submittedTo = null);

    Task<ReimbursementRequestEntity?> GetByIdAsync(string guardianId, string requestId);

    /// <summary>Lookup by request id alone (indexed), for links that don't carry the
    /// submitter's guardian id.</summary>
    Task<ReimbursementRequestEntity?> GetByRequestIdAsync(string requestId);

    Task AddAsync(ReimbursementRequestEntity request);

    Task UpdateAsync(ReimbursementRequestEntity request);

    Task DeleteAsync(ReimbursementRequestEntity request);

    /// <summary>One page of requests, newest first, filtered in the database by status (null = all)
    /// and submitted date, with the total number of matches. A name search matches the submitting
    /// guardian, the scout the request is for, or any scout linked to the submitting guardian.</summary>
    Task<PagedResult<ReimbursementRequestEntity>> GetPageAsync(string? status, DateTimeOffset? submittedFrom, DateTimeOffset? submittedTo, int page, int pageSize, string? nameSearch = null, string? descriptionSearch = null);

    /// <summary>Requests marked paid in [paidFrom, paidTo).</summary>
    Task<IReadOnlyList<ReimbursementRequestEntity>> GetPaidBetweenAsync(DateTimeOffset paidFrom, DateTimeOffset paidTo);

    /// <summary>Requests tied to one scout.</summary>
    Task<IReadOnlyList<ReimbursementRequestEntity>> GetByScoutAsync(string scoutId);

    /// <summary>How many requests have this status and their total amount.</summary>
    Task<(int Count, decimal Total)> GetStatusSummaryAsync(string status);
}
