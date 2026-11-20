using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class GuardianAuthenticatorRepository(FinanceDb db) : IGuardianAuthenticatorRepository
{
    public Task<GuardianAuthenticatorEntity?> GetAsync(string guardianId) =>
        db.ReadAsync(c => c.GuardianAuthenticators.AsNoTracking()
            .FirstOrDefaultAsync(a => a.PartitionKey == GuardianAuthenticatorEntity.PartitionKeyValue && a.RowKey == guardianId));

    public async Task<IReadOnlySet<string>> GetGuardianIdsWithAuthenticatorAsync()
    {
        var ids = await db.ReadAsync(c => c.GuardianAuthenticators.AsNoTracking()
            .Where(a => a.PartitionKey == GuardianAuthenticatorEntity.PartitionKeyValue)
            .Select(a => a.RowKey)
            .ToListAsync());
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public Task UpsertAsync(GuardianAuthenticatorEntity authenticator) => db.WriteAsync(async c =>
    {
        await c.DeleteByKeyAsync<GuardianAuthenticatorEntity>(authenticator.PartitionKey, authenticator.RowKey);
        authenticator.Version = null;
        c.GuardianAuthenticators.Add(authenticator);
    });

    public Task DeleteAsync(string guardianId) => db.WriteAsync(c =>
        c.DeleteByKeyAsync<GuardianAuthenticatorEntity>(GuardianAuthenticatorEntity.PartitionKeyValue, guardianId));
}
