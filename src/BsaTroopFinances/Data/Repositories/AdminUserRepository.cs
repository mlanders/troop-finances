using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class AdminUserRepository(FinanceDb db) : IAdminUserRepository
{
    public async Task<AdminUserEntity?> GetAsync(string userKey) =>
        await db.ReadAsync(c => c.AdminUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PartitionKey == AdminUserEntity.PartitionKeyValue && u.RowKey == userKey));

    public async Task<IReadOnlyList<AdminUserEntity>> GetAllAsync() =>
        await db.ReadAsync(async c => await c.AdminUsers.AsNoTracking().OrderBy(u => u.Name).ToListAsync());

    public async Task<int> CountWithRoleAsync(string role) =>
        await db.ReadAsync(c => c.AdminUsers.CountAsync(u => u.Role == role));

    public Task AddAsync(AdminUserEntity user) => db.WriteAsync(c =>
    {
        c.AdminUsers.Add(user);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(AdminUserEntity user) => db.WriteAsync(c => c.UpdateDetachedAsync(user));

    public Task DeleteAsync(string userKey) => db.WriteAsync(async c =>
    {
        await c.AdminSignIns.Where(s => s.UserKey == userKey).ExecuteDeleteAsync();
        await c.DeleteByKeyAsync<AdminUserEntity>(AdminUserEntity.PartitionKeyValue, userKey);
    });

    public async Task<AdminSignInEntity?> GetSignInAsync(string signInKey) =>
        await db.ReadAsync(c => c.AdminSignIns.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PartitionKey == AdminSignInEntity.PartitionKeyValue && s.RowKey == signInKey));

    public async Task<IReadOnlyList<AdminSignInEntity>> GetAllSignInsAsync() =>
        await db.ReadAsync(async c => await c.AdminSignIns.AsNoTracking().ToListAsync());

    public async Task<IReadOnlyList<AdminSignInEntity>> GetSignInsForUserAsync(string userKey) =>
        await db.ReadAsync(async c => await c.AdminSignIns.AsNoTracking().Where(s => s.UserKey == userKey).ToListAsync());

    public async Task<IReadOnlyList<AdminSignInEntity>> FindSignInsByVerifiedEmailAsync(string verifiedEmail) =>
        await db.ReadAsync(async c => await c.AdminSignIns.AsNoTracking()
            .Where(s => s.VerifiedEmail == verifiedEmail)
            .OrderByDescending(s => s.LastSeenUtc)
            .ToListAsync());

    public Task AddSignInAsync(AdminSignInEntity signIn) => db.WriteAsync(c =>
    {
        c.AdminSignIns.Add(signIn);
        return Task.CompletedTask;
    });

    public Task UpdateSignInAsync(AdminSignInEntity signIn) => db.WriteAsync(c => c.UpdateDetachedAsync(signIn));
}
