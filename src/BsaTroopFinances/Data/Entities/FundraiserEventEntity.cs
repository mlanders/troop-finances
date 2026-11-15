namespace BsaTroopFinances.Data.Entities;

public static class FundraiserEventStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

/// <summary>
/// A fundraiser (popcorn sale, car wash, etc.) whose proceeds get allocated into individual
/// scouts' accounts via ScoutLedgerEntry rows referencing this event's id. TotalRaised is
/// informational only -- this app does not track a troop-level/unallocated balance (out of
/// scope; the troop's general fund is assumed tracked elsewhere).
/// </summary>
public class FundraiserEventEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string Name { get; set; } = string.Empty;

    // A calendar date stored as midnight UTC (a carry-over from Table Storage, which had no date
    // type) -- convert at the edges with DateOnly.FromDateTime / date.ToDateTime(TimeOnly.MinValue).
    public DateTimeOffset EventDate { get; set; }
    public decimal? TotalRaised { get; set; }
    public decimal? TotalExpenses { get; set; }
    public decimal? GoalAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string StatusValue { get; set; } = FundraiserEventStatus.Open;
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string EventId => RowKey;

    /// <summary>What's available to allocate to scouts in total: the amount raised less the fundraiser's expenses.</summary>
    public decimal NetProceeds => (TotalRaised ?? 0) - (TotalExpenses ?? 0);

    public const string PartitionKeyValue = "FundraiserEvent";

    public static string NewRowKey() => Guid.NewGuid().ToString("N");
}
