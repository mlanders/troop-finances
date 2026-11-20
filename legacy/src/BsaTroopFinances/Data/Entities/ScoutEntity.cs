namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// A scout in this app's own roster (kept here, not read live from any other system).
/// PartitionKey is a constant and RowKey is the scout's id; (PartitionKey, RowKey) is the
/// primary key, kept from the Table Storage era.
/// </summary>
public class ScoutEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Rank { get; set; }
    public string? PatrolName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Denormalized running balance, kept in sync by IScoutLedgerRepository.AppendEntryAsync
    /// (same transaction as the ledger row). Source of truth is the
    /// ScoutLedger partition for this scout -- this field exists purely so list/roster pages don't
    /// need a ledger scan per scout to show a balance.</summary>
    public decimal CachedBalance { get; set; }
    public DateTimeOffset BalanceUpdatedUtc { get; set; }

    /// <summary>This scout's stable id (same as RowKey), used as the FK everywhere else.</summary>
    public string ScoutId => RowKey;

    public const string PartitionKeyValue = "Scout";

    public static string NewRowKey() => Guid.NewGuid().ToString("N");
}
