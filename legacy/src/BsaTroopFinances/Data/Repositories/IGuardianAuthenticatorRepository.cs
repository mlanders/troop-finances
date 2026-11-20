using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IGuardianAuthenticatorRepository
{
    Task<GuardianAuthenticatorEntity?> GetAsync(string guardianId);

    /// <summary>The guardian ids that have an authenticator app linked.</summary>
    Task<IReadOnlySet<string>> GetGuardianIdsWithAuthenticatorAsync();

    /// <summary>Adds or replaces the guardian's authenticator -- one per guardian.</summary>
    Task UpsertAsync(GuardianAuthenticatorEntity authenticator);

    Task DeleteAsync(string guardianId);
}
