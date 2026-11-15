using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IGuardianScoutLinkRepository
{
    /// <summary>"My scouts" -- a single partition query, the parent portal's hot path.</summary>
    Task<IReadOnlyList<GuardianScoutLinkEntity>> GetScoutsForGuardianAsync(string guardianId);

    /// <summary>"Who are this scout's guardians" (indexed by scout).</summary>
    Task<IReadOnlyList<GuardianScoutLinkEntity>> GetGuardiansForScoutAsync(string scoutId);

    Task<bool> IsLinkedAsync(string guardianId, string scoutId);

    Task AddLinkAsync(GuardianScoutLinkEntity link);

    Task RemoveLinkAsync(string guardianId, string scoutId);
}
