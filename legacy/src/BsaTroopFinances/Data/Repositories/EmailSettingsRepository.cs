using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class EmailSettingsRepository(FinanceDb db) : IEmailSettingsRepository
{
    public async Task<EmailSettingsEntity> GetAsync() =>
        await db.ReadAsync(c => c.EmailSettings.AsNoTracking().FirstOrDefaultAsync(s =>
            s.PartitionKey == EmailSettingsEntity.PartitionKeyValue && s.RowKey == EmailSettingsEntity.RowKeyValue))
        ?? new EmailSettingsEntity();

    /// <summary>The single settings row: created the first time, overwritten after that.</summary>
    public Task SaveAsync(EmailSettingsEntity settings) => db.WriteAsync(async c =>
    {
        settings.PartitionKey = EmailSettingsEntity.PartitionKeyValue;
        settings.RowKey = EmailSettingsEntity.RowKeyValue;
        var exists = await c.EmailSettings.AnyAsync(s =>
            s.PartitionKey == EmailSettingsEntity.PartitionKeyValue && s.RowKey == EmailSettingsEntity.RowKeyValue);
        if (exists)
        {
            settings.Version = null;
            await c.UpdateDetachedAsync(settings);
        }
        else
        {
            c.EmailSettings.Add(settings);
        }
    });
}
