using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class ScoutLedgerRepository(FinanceDb db, IScoutRepository scoutRepository) : IScoutLedgerRepository
{
    public async Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetForScoutAsync(string scoutId)
    {
        var partitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId);
        return await db.ReadAsync(async c => await c.ScoutLedger.AsNoTracking()
            .Where(e => e.PartitionKey == partitionKey).OrderBy(e => e.RowKey).ToListAsync());
    }

    public async Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.ScoutLedger.AsNoTracking().ToListAsync());

    public async Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetForSourceAsync(string sourceType, string sourceId) =>
        await db.ReadAsync(async c => await c.ScoutLedger.AsNoTracking()
            .Where(e => e.SourceTypeValue == sourceType && e.SourceId == sourceId)
            .OrderBy(e => e.RowKey)
            .ToListAsync());

    public async Task<IReadOnlyDictionary<string, decimal>> GetTotalsBySourceAsync(string sourceType, IReadOnlyCollection<string> sourceIds) =>
        await db.ReadAsync(async c => await c.ScoutLedger.AsNoTracking()
            .Where(e => e.SourceTypeValue == sourceType && sourceIds.Contains(e.SourceId))
            .GroupBy(e => e.SourceId)
            .Select(g => new { SourceId = g.Key, Total = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.SourceId, x => x.Total));

    public async Task<ScoutLedgerEntryEntity?> GetEntryAsync(string scoutId, string rowKey)
    {
        var partitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(scoutId);
        return await db.ReadAsync(c => c.ScoutLedger.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PartitionKey == partitionKey && e.RowKey == rowKey));
    }

    /// <summary>Writes the entry and moves the scout's cached balance by its signed amount. Both
    /// happen in one transaction (joining the caller's, if any), so they can't get out of step.</summary>
    public Task AppendEntryAsync(ScoutLedgerEntryEntity entry) => db.InTransactionAsync(async () =>
    {
        await db.WriteAsync(c =>
        {
            c.ScoutLedger.Add(entry);
            return Task.CompletedTask;
        });
        await scoutRepository.ApplyBalanceDeltaAsync(entry.PartitionKey, entry.SignedAmount);
    });
}
