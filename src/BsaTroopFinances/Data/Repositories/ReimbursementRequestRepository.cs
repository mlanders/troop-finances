using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class ReimbursementRequestRepository(FinanceDb db) : IReimbursementRequestRepository
{
    public async Task<IReadOnlyList<ReimbursementRequestEntity>> GetBySubmitterAsync(string guardianId)
    {
        var partitionKey = ReimbursementRequestEntity.BuildPartitionKey(guardianId);
        return await db.ReadAsync(async c => await c.ReimbursementRequests.AsNoTracking()
            .Where(r => r.PartitionKey == partitionKey)
            .OrderByDescending(r => r.SubmittedUtc).ThenByDescending(r => r.RowKey).ToListAsync());
    }

    public async Task<PagedResult<ReimbursementRequestEntity>> GetSubmitterPageAsync(string guardianId, string? status, int page, int pageSize)
    {
        var partitionKey = ReimbursementRequestEntity.BuildPartitionKey(guardianId);
        return await db.ReadAsync(async c =>
        {
            var query = Filter(c.ReimbursementRequests.AsNoTracking().Where(r => r.PartitionKey == partitionKey), status, null, null);
            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.SubmittedUtc).ThenByDescending(r => r.RowKey)
                .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return new PagedResult<ReimbursementRequestEntity>(items, total);
        });
    }

    public async Task<IReadOnlyDictionary<string, int>> GetSubmitterStatusCountsAsync(string guardianId)
    {
        var partitionKey = ReimbursementRequestEntity.BuildPartitionKey(guardianId);
        return await db.ReadAsync(async c => await c.ReimbursementRequests
            .Where(r => r.PartitionKey == partitionKey)
            .GroupBy(r => r.StatusValue)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count));
    }

    public async Task<IReadOnlyList<ReimbursementRequestEntity>> GetAllAsync(DateTimeOffset? submittedFrom = null, DateTimeOffset? submittedTo = null) =>
        await db.ReadAsync(async c => await Filter(c.ReimbursementRequests.AsNoTracking(), null, submittedFrom, submittedTo)
            .OrderByDescending(r => r.SubmittedUtc).ToListAsync());

    public async Task<PagedResult<ReimbursementRequestEntity>> GetPageAsync(string? status, DateTimeOffset? submittedFrom, DateTimeOffset? submittedTo, int page, int pageSize, string? nameSearch = null, string? descriptionSearch = null) =>
        await db.ReadAsync(async c =>
        {
            var query = Filter(c.ReimbursementRequests.AsNoTracking(), status, submittedFrom, submittedTo);
            if (!string.IsNullOrWhiteSpace(nameSearch))
            {
                // Same idea as the Invoices search: the guardian, the scout, or the family -- a
                // general troop purchase by Maria turns up when searching for her scout too.
                // Lower-cased on both sides: SQL Server ignores case anyway, PostgreSQL doesn't.
                var text = nameSearch.Trim().ToLowerInvariant();
                query = query.Where(r => r.SubmitterName.ToLower().Contains(text)
                    || (r.ScoutName != null && r.ScoutName.ToLower().Contains(text))
                    || c.GuardianScoutLinks.Any(l => l.GuardianId == r.SubmitterGuardianId && l.ScoutName.ToLower().Contains(text)));
            }
            if (!string.IsNullOrWhiteSpace(descriptionSearch))
            {
                var description = descriptionSearch.Trim().ToLowerInvariant();
                query = query.Where(r => r.Description.ToLower().Contains(description));
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.SubmittedUtc).ThenByDescending(r => r.RowKey)
                .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return new PagedResult<ReimbursementRequestEntity>(items, total);
        });

    public async Task<IReadOnlyList<ReimbursementRequestEntity>> GetPaidBetweenAsync(DateTimeOffset paidFrom, DateTimeOffset paidTo) =>
        await db.ReadAsync(async c => await c.ReimbursementRequests.AsNoTracking()
            .Where(r => r.StatusValue == ReimbursementStatus.Paid && r.PaidUtc >= paidFrom && r.PaidUtc < paidTo)
            .ToListAsync());

    public async Task<IReadOnlyList<ReimbursementRequestEntity>> GetByScoutAsync(string scoutId) =>
        await db.ReadAsync(async c => await c.ReimbursementRequests.AsNoTracking().Where(r => r.ScoutId == scoutId).ToListAsync());

    public async Task<IReadOnlyList<ReimbursementRequestEntity>> GetBySourcePrefixAsync(string prefix) =>
        await db.ReadAsync(async c => await c.ReimbursementRequests.AsNoTracking()
            .Where(r => r.SourceRef != null && r.SourceRef.StartsWith(prefix)).ToListAsync());

    public async Task<(int Count, decimal Total)> GetStatusSummaryAsync(string status) =>
        await db.ReadAsync(async c =>
        {
            var matching = c.ReimbursementRequests.Where(r => r.StatusValue == status);
            return (await matching.CountAsync(), await matching.SumAsync(r => (decimal?)r.AmountRequested) ?? 0);
        });

    public async Task<ReimbursementRequestEntity?> GetByIdAsync(string guardianId, string requestId) =>
        await db.ReadAsync(c => c.ReimbursementRequests.AsNoTracking().FirstOrDefaultAsync(r =>
            r.PartitionKey == ReimbursementRequestEntity.BuildPartitionKey(guardianId) && r.RowKey == requestId));

    public async Task<ReimbursementRequestEntity?> GetByRequestIdAsync(string requestId) =>
        await db.ReadAsync(c => c.ReimbursementRequests.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == requestId));

    public Task AddAsync(ReimbursementRequestEntity request) => db.WriteAsync(c =>
    {
        if (string.IsNullOrEmpty(request.RequestId))
        {
            request.RequestId = request.RowKey;
        }
        c.ReimbursementRequests.Add(request);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(ReimbursementRequestEntity request) => db.WriteAsync(c => c.UpdateDetachedAsync(request));

    public Task DeleteAsync(ReimbursementRequestEntity request) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<ReimbursementRequestEntity>(request.PartitionKey, request.RowKey));

    private static IQueryable<ReimbursementRequestEntity> Filter(IQueryable<ReimbursementRequestEntity> query, string? status,
        DateTimeOffset? submittedFrom, DateTimeOffset? submittedTo)
    {
        if (status is not null)
        {
            query = query.Where(r => r.StatusValue == status);
        }
        if (submittedFrom is { } from)
        {
            query = query.Where(r => r.SubmittedUtc >= from);
        }
        if (submittedTo is { } to)
        {
            query = query.Where(r => r.SubmittedUtc <= to);
        }
        return query;
    }
}
