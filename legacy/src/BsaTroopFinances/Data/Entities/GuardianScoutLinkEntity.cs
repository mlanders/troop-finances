namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// Links a guardian to a scout they can see in the portal ("my scouts"). PartitionKey =
/// GuardianId and RowKey = ScoutId, so the primary key also prevents duplicate links; ScoutId is
/// indexed for the reverse lookup ("who are this scout's guardians").
/// </summary>
public class GuardianScoutLinkEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string GuardianId { get; set; } = string.Empty;
    public string ScoutId { get; set; } = string.Empty;
    public string ScoutName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    public static string BuildPartitionKey(string guardianId) => guardianId;
    public static string BuildRowKey(string scoutId) => scoutId;
}
