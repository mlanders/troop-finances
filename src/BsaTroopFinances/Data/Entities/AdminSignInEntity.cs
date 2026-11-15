namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// One Microsoft or Google account someone has signed into the admin area with. Keyed by the
/// provider's stable id ("microsoft:{oid}" / "google:{sub}", see AdminIdentity.Key) and pointing
/// at the person (<see cref="AdminUserEntity"/>) it belongs to, whose role it gets. A person can
/// have several: a new account joins an existing person only when its provider vouches for the
/// same address as one of theirs (see AdminIdentity.VerifiedEmail).
/// </summary>
public class AdminSignInEntity
{
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    /// <summary>The person's AdminUsers RowKey.</summary>
    public string UserKey { get; set; } = string.Empty;

    /// <summary>"Microsoft" or "Google".</summary>
    public string Provider { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Shown to help recognise the account; not trusted.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The address the provider vouches for, lower-cased, or empty. The only thing
    /// accounts are grouped by.</summary>
    public string VerifiedEmail { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }

    public const string PartitionKeyValue = "AdminSignIn";

    public string SignInKey => RowKey;
}
