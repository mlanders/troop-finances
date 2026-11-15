namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// A pending email one-time-code login attempt. PartitionKey is a constant, RowKey is the
/// normalized email -- one active code per email, enforced by always upserting (a re-requested
/// code replaces the prior one rather than accumulating rows). The code itself is never stored
/// in plaintext, only a keyed hash, so a database read (or leak) can't itself grant access.
/// </summary>
public class LoginCodeEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    /// <summary>HMAC-SHA256 of the 6-digit code, keyed with a server-side secret.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    public const string PartitionKeyValue = "LoginCode";
}
