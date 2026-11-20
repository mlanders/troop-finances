using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class FinanceAuditRepository(FinanceDb db, TimeProvider timeProvider) : IFinanceAuditRepository
{
    public Task AddAsync(string entityType, string entityId, string action, string actor, string? notes = null, string? details = null)
    {
        var now = timeProvider.GetUtcNow();
        return db.WriteAsync(c =>
        {
            c.FinanceAudit.Add(new FinanceAuditEntryEntity
            {
                PartitionKey = FinanceAuditEntryEntity.BuildPartitionKey(entityType, entityId),
                RowKey = FinanceAuditEntryEntity.NewRowKey(now),
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Actor = actor,
                OccurredUtc = now,
                Notes = notes ?? string.Empty,
                Details = details ?? string.Empty,
            });
            return Task.CompletedTask;
        });
    }

    public async Task<IReadOnlyList<FinanceAuditEntryEntity>> GetForEntityAsync(string entityType, string entityId) =>
        await db.ReadAsync(async c => await c.FinanceAudit.AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.RowKey)
            .ToListAsync());

    public async Task<IReadOnlyList<FinanceAuditEntryEntity>> GetSinceAsync(DateTimeOffset sinceUtc) =>
        await db.ReadAsync(async c => await c.FinanceAudit.AsNoTracking()
            .Where(e => e.OccurredUtc >= sinceUtc).OrderByDescending(e => e.OccurredUtc).ToListAsync());

    public async Task<IReadOnlyList<FinanceAuditEntryEntity>> GetRecentAsync(int count) =>
        await db.ReadAsync(async c => await c.FinanceAudit.AsNoTracking()
            .OrderByDescending(e => e.OccurredUtc).Take(count).ToListAsync());
}
