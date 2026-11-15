using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class FundraiserEventRepository(FinanceDb db) : IFundraiserEventRepository
{
    public async Task<IReadOnlyList<FundraiserEventEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.FundraiserEvents.AsNoTracking().OrderByDescending(e => e.EventDate).ToListAsync());

    public async Task<FundraiserEventEntity?> GetByIdAsync(string eventId) =>
        await db.ReadAsync(c => c.FundraiserEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PartitionKey == FundraiserEventEntity.PartitionKeyValue && e.RowKey == eventId));

    public Task AddAsync(FundraiserEventEntity fundraiserEvent) => db.WriteAsync(c =>
    {
        c.FundraiserEvents.Add(fundraiserEvent);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(FundraiserEventEntity fundraiserEvent) => db.WriteAsync(c => c.UpdateDetachedAsync(fundraiserEvent));
}
