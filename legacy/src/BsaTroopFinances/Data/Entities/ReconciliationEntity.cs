namespace BsaTroopFinances.Data.Entities;

public static class ReconciliationStatus
{
    public const string InProgress = "In progress";
    public const string Complete = "Complete";
}

/// <summary>
/// Matching an account's register to one bank statement. While in progress, the ticked entries
/// are kept in Cleared (a comma-separated list of register RowKeys); finishing stamps this id on
/// each of them, which locks them. PartitionKey = AccountId.
/// </summary>
public class ReconciliationEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    // A calendar date stored as midnight UTC.
    public DateTimeOffset StatementDate { get; set; }
    public decimal StatementBalance { get; set; }

    public string Status { get; set; } = ReconciliationStatus.InProgress;

    /// <summary>The register entries ticked so far, while in progress.</summary>
    public string Cleared { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? CompletedUtc { get; set; }
    public string CompletedBy { get; set; } = string.Empty;

    public string AccountId => PartitionKey;
    public string ReconciliationId => RowKey;

    public IReadOnlySet<string> ClearedIds => Cleared.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    public static string NewRowKey() => $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";
}
