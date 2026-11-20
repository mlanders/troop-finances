using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IFinanceAuditRepository
{
    Task AddAsync(string entityType, string entityId, string action, string actor, string? notes = null, string? details = null);

    Task<IReadOnlyList<FinanceAuditEntryEntity>> GetForEntityAsync(string entityType, string entityId);

    /// <summary>All recorded actions across every entity since <paramref name="sinceUtc"/>, newest first.</summary>
    Task<IReadOnlyList<FinanceAuditEntryEntity>> GetSinceAsync(DateTimeOffset sinceUtc);

    /// <summary>The most recent <paramref name="count"/> recorded actions across every entity, newest first.</summary>
    Task<IReadOnlyList<FinanceAuditEntryEntity>> GetRecentAsync(int count);
}
