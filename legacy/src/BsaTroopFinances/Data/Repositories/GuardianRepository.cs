using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class GuardianRepository(FinanceDb db) : IGuardianRepository
{
    public async Task<GuardianEntity?> GetByEmailAsync(string email)
    {
        var normalized = GuardianEntity.NormalizeEmail(email);
        return await db.ReadAsync(c => c.Guardians.AsNoTracking()
            .FirstOrDefaultAsync(g => g.PartitionKey == GuardianEntity.PartitionKeyValue && g.RowKey == normalized));
    }

    public async Task<GuardianEntity?> GetByIdAsync(string guardianId) =>
        await db.ReadAsync(c => c.Guardians.AsNoTracking().FirstOrDefaultAsync(g => g.GuardianId == guardianId));

    public async Task<IReadOnlyList<GuardianEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.Guardians.AsNoTracking().OrderBy(g => g.FullName).ToListAsync());

    public Task AddAsync(GuardianEntity guardian) => db.WriteAsync(c =>
    {
        c.Guardians.Add(guardian);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(GuardianEntity guardian) => db.WriteAsync(c => c.UpdateDetachedAsync(guardian));

    public Task DeleteAsync(GuardianEntity guardian) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<GuardianEntity>(guardian.PartitionKey, guardian.RowKey));
}
