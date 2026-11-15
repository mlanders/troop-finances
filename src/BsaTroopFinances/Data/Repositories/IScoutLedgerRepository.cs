using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IScoutLedgerRepository
{
    /// <summary>A scout's whole ledger, already chronological (RowKey is ticks-prefixed) --
    /// one partition query.</summary>
    Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetForScoutAsync(string scoutId);

    /// <summary>Every ledger entry across every scout. Prefer GetForSourceAsync /
    /// GetTotalsBySourceAsync, which only read what they need.</summary>
    Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetAllAsync();

    /// <summary>Point-read a single entry (e.g. to reverse it when voiding an invoice).</summary>
    Task<ScoutLedgerEntryEntity?> GetEntryAsync(string scoutId, string rowKey);

    /// <summary>
    /// Writes the ledger row and applies its signed delta to the scout's cached balance in one
    /// transaction, so the two can't get out of step. This is the only way a ledger entry should
    /// ever be created -- go through this, not IScoutRepository.ApplyBalanceDeltaAsync directly.
    /// </summary>
    Task AppendEntryAsync(ScoutLedgerEntryEntity entry);

    /// <summary>Entries that came from one source, e.g. one fundraiser's allocations.</summary>
    Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetForSourceAsync(string sourceType, string sourceId);

    /// <summary>Total amount per source id for these sources (e.g. allocated per fundraiser).</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetTotalsBySourceAsync(string sourceType, IReadOnlyCollection<string> sourceIds);
}
