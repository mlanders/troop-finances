using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class GuardianScoutLinkRepository(FinanceDb db) : IGuardianScoutLinkRepository
{
    public async Task<IReadOnlyList<GuardianScoutLinkEntity>> GetScoutsForGuardianAsync(string guardianId)
    {
        var partitionKey = GuardianScoutLinkEntity.BuildPartitionKey(guardianId);
        return await db.ReadAsync(async c => await c.GuardianScoutLinks.AsNoTracking()
            .Where(l => l.PartitionKey == partitionKey).OrderBy(l => l.ScoutName).ToListAsync());
    }

    public async Task<IReadOnlyList<GuardianScoutLinkEntity>> GetGuardiansForScoutAsync(string scoutId) =>
        await db.ReadAsync(async c => await c.GuardianScoutLinks.AsNoTracking().Where(l => l.ScoutId == scoutId).ToListAsync());

    public async Task<bool> IsLinkedAsync(string guardianId, string scoutId)
    {
        var partitionKey = GuardianScoutLinkEntity.BuildPartitionKey(guardianId);
        var rowKey = GuardianScoutLinkEntity.BuildRowKey(scoutId);
        return await db.ReadAsync(c => c.GuardianScoutLinks.AnyAsync(l => l.PartitionKey == partitionKey && l.RowKey == rowKey));
    }

    /// <summary>Adds the link, or refreshes it (e.g. the scout's name) if it already exists.</summary>
    public Task AddLinkAsync(GuardianScoutLinkEntity link) => db.WriteAsync(async c =>
    {
        var exists = await c.GuardianScoutLinks.AnyAsync(l => l.PartitionKey == link.PartitionKey && l.RowKey == link.RowKey);
        if (exists)
        {
            link.Version = null;
            await c.UpdateDetachedAsync(link);
        }
        else
        {
            c.GuardianScoutLinks.Add(link);
        }
    });

    public Task RemoveLinkAsync(string guardianId, string scoutId) => db.WriteAsync(c =>
        c.DeleteByKeyAsync<GuardianScoutLinkEntity>(
            GuardianScoutLinkEntity.BuildPartitionKey(guardianId), GuardianScoutLinkEntity.BuildRowKey(scoutId)));
}
