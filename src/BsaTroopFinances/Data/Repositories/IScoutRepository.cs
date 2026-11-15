using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IScoutRepository
{
    Task<IReadOnlyList<ScoutEntity>> GetActiveAsync();

    /// <summary>Every scout, active or not, for the admin roster page.</summary>
    Task<IReadOnlyList<ScoutEntity>> GetAllAsync();

    Task<ScoutEntity?> GetByIdAsync(string scoutId);

    Task AddAsync(ScoutEntity scout);

    Task UpdateAsync(ScoutEntity scout);

    Task DeleteAsync(ScoutEntity scout);

    /// <summary>
    /// Adds a signed delta to a scout's cached balance in one atomic UPDATE. Used only by
    /// IScoutLedgerRepository.AppendEntryAsync, in the same transaction as the ledger row -- callers
    /// should go through that, not call this directly, so the ledger and the cached balance never
    /// diverge.
    /// </summary>
    Task ApplyBalanceDeltaAsync(string scoutId, decimal delta);
}
