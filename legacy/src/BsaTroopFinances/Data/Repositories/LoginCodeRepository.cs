using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class LoginCodeRepository(FinanceDb db) : ILoginCodeRepository
{
    public async Task<LoginCodeEntity?> GetByEmailAsync(string email)
    {
        var normalized = GuardianEntity.NormalizeEmail(email);
        return await db.ReadAsync(c => c.LoginCodes.AsNoTracking()
            .FirstOrDefaultAsync(l => l.PartitionKey == LoginCodeEntity.PartitionKeyValue && l.RowKey == normalized));
    }

    /// <summary>One active code per email: a re-requested code replaces the previous one.</summary>
    public Task UpsertAsync(LoginCodeEntity code) => db.WriteAsync(async c =>
    {
        await c.DeleteByKeyAsync<LoginCodeEntity>(code.PartitionKey, code.RowKey);
        code.Version = null;
        c.LoginCodes.Add(code);
    });

    public Task DeleteAsync(string email) => db.WriteAsync(c =>
        c.DeleteByKeyAsync<LoginCodeEntity>(LoginCodeEntity.PartitionKeyValue, GuardianEntity.NormalizeEmail(email)));
}
