using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IGuardianRepository
{
    /// <summary>Key lookup by (normalized) email -- the login hot path.</summary>
    Task<GuardianEntity?> GetByEmailAsync(string email);

    /// <summary>Lookup by the stable GuardianId (indexed).</summary>
    Task<GuardianEntity?> GetByIdAsync(string guardianId);

    Task<IReadOnlyList<GuardianEntity>> GetAllAsync();

    Task AddAsync(GuardianEntity guardian);

    Task UpdateAsync(GuardianEntity guardian);

    Task DeleteAsync(GuardianEntity guardian);
}
