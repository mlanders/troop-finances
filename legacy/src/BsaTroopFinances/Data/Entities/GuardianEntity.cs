namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// A parent/guardian who can log into the self-service portal via emailed one-time code.
/// PartitionKey is a constant; RowKey is the normalized (lowercased, trimmed) email address, and
/// (PartitionKey, RowKey) is the primary key -- so "look up by email" (the login hot path) is a
/// key lookup and two guardians can never share an email (inserting a duplicate fails).
/// </summary>
public class GuardianEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    /// <summary>Stable id independent of the email/RowKey, so other tables can reference a
    /// guardian without cascading if their email address ever changes.</summary>
    public string GuardianId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }

    public const string PartitionKeyValue = "Guardian";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
