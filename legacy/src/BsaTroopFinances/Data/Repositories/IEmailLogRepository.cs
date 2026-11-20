using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IEmailLogRepository
{
    Task AddAsync(EmailLogEntity entry);

    /// <summary>Everything sent on or after <paramref name="since"/>, newest first.</summary>
    Task<IReadOnlyList<EmailLogEntity>> GetSinceAsync(DateTimeOffset since);
}
