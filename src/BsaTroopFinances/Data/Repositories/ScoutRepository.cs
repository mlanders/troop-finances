using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class ScoutRepository(FinanceDb db, TimeProvider timeProvider) : IScoutRepository
{
    public async Task<IReadOnlyList<ScoutEntity>> GetActiveAsync() =>
        await db.ReadAsync(async c => await c.Scouts.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.FullName).ToListAsync());

    public async Task<IReadOnlyList<ScoutEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.Scouts.AsNoTracking().OrderBy(s => s.FullName).ToListAsync());

    public async Task<ScoutEntity?> GetByIdAsync(string scoutId) =>
        await db.ReadAsync(c => c.Scouts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PartitionKey == ScoutEntity.PartitionKeyValue && s.RowKey == scoutId));

    public Task AddAsync(ScoutEntity scout) => db.WriteAsync(c =>
    {
        c.Scouts.Add(scout);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(ScoutEntity scout) => db.WriteAsync(c => c.UpdateDetachedAsync(scout));

    public Task DeleteAsync(ScoutEntity scout) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<ScoutEntity>(scout.PartitionKey, scout.RowKey));

    public async Task ApplyBalanceDeltaAsync(string scoutId, decimal delta)
    {
        if (delta == 0)
        {
            // A zero-amount ledger entry (e.g. a name change) leaves the balance as is. Skipping the
            // write also keeps the scout's row version unchanged for a caller about to update it.
            return;
        }

        // One atomic UPDATE ... SET CachedBalance = CachedBalance + @delta -- no read-modify-write,
        // so concurrent payments can't lose an update.
        var now = timeProvider.GetUtcNow();
        var rows = await db.ReadAsync(c => c.Scouts
            .Where(s => s.PartitionKey == ScoutEntity.PartitionKeyValue && s.RowKey == scoutId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CachedBalance, x => x.CachedBalance + delta)
                .SetProperty(x => x.BalanceUpdatedUtc, now)));
        if (rows == 0)
        {
            throw new InvalidOperationException($"Scout '{scoutId}' not found.");
        }
    }
}
