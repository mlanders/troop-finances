using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IEmailSettingsRepository
{
    /// <summary>Returns the defaults (everything enabled, no admin recipients) until settings
    /// have been saved for the first time.</summary>
    Task<EmailSettingsEntity> GetAsync();

    Task SaveAsync(EmailSettingsEntity settings);
}
