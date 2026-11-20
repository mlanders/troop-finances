namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// An authenticator app (Google/Microsoft Authenticator, 1Password, ...) a guardian has linked so
/// they can sign in with its rotating 6-digit code instead of waiting for an emailed one.
/// PartitionKey is a constant, RowKey is the GuardianId -- at most one app per guardian. Kept in
/// its own table rather than on GuardianEntity so sign-in bookkeeping (attempt counts, last code
/// used) never collides with an admin editing the guardian.
/// </summary>
public class GuardianAuthenticatorEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    /// <summary>The shared TOTP secret, AES-GCM encrypted (see AuthenticatorSecretProtector) -- a
    /// database read alone can't produce valid codes.</summary>
    public string SecretProtected { get; set; } = string.Empty;

    public DateTimeOffset EnabledUtc { get; set; }

    /// <summary>The 30-second time step of the last code accepted, so the same code can't be used
    /// twice.</summary>
    public long LastUsedStep { get; set; }

    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }

    public const string PartitionKeyValue = "GuardianAuthenticator";
}
