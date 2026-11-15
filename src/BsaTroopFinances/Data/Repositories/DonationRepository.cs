using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class DonationRepository(FinanceDb db) : IDonationRepository
{
    public async Task<IReadOnlyList<DonationEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.Donations.AsNoTracking()
            .OrderByDescending(d => d.ReceivedDate).ThenByDescending(d => d.CreatedUtc).ToListAsync());

    public async Task<IReadOnlyList<DonationEntity>> GetReceivedBetweenAsync(DateOnly from, DateOnly to)
    {
        // ReceivedDate is a calendar date stored as midnight UTC, so the bounds are too.
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return await db.ReadAsync(async c => await c.Donations.AsNoTracking()
            .Where(d => d.ReceivedDate >= start && d.ReceivedDate < end).ToListAsync());
    }

    public async Task<DonationEntity?> GetByIdAsync(string donationId) =>
        await db.ReadAsync(c => c.Donations.AsNoTracking()
            .FirstOrDefaultAsync(d => d.PartitionKey == DonationEntity.PartitionKeyValue && d.RowKey == donationId));

    public Task AddAsync(DonationEntity donation) => db.WriteAsync(c =>
    {
        c.Donations.Add(donation);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(DonationEntity donation) => db.WriteAsync(c => c.UpdateDetachedAsync(donation));

    public Task DeleteAsync(string donationId) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<DonationEntity>(DonationEntity.PartitionKeyValue, donationId));
}
