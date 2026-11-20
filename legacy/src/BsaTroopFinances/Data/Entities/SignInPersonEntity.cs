namespace BsaTroopFinances.Data.Entities;

/// <summary>Who a sign-in app roster person is: a Scout or an Adult. Stored as these strings.</summary>
public static class SignInPersonType
{
    public const string Scout = "Scout";
    public const string Adult = "Adult";

    public static bool IsValid(string? type) => type is Scout or Adult;
}

/// <summary>
/// A person on the meeting sign-in app's roster, as that app last sent it (PUT /api/signin/roster),
/// and who they are here: the finance scout (for a Scout) or guardian (for an Adult) the treasurer
/// linked them to on Families > Sign-in app links. The link is by the sign-in app's own id, so a
/// rename in either app never breaks it, and every trip line from that person matches by itself.
/// Each finance scout or guardian can be linked to only one sign-in person (unique indexes).
/// PartitionKey is a constant; RowKey is the sign-in app's PersonId.
/// </summary>
public class SignInPersonEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>SignInPersonType.Scout or Adult.</summary>
    public string PersonType { get; set; } = SignInPersonType.Scout;

    /// <summary>False once the sign-in app stops listing them as active.</summary>
    public bool IsActiveInSignIn { get; set; } = true;

    public DateTimeOffset LastSyncedUtc { get; set; }

    /// <summary>The finance scout this Scout is, once linked.</summary>
    public string? LinkedScoutId { get; set; }

    /// <summary>The finance guardian this Adult is, once linked.</summary>
    public string? LinkedGuardianId { get; set; }

    /// <summary>Never charged here (a visitor, a leader whose trips the troop covers): no link needed.</summary>
    public bool NotBilled { get; set; }

    public string? LinkedBy { get; set; }
    public DateTimeOffset? LinkedUtc { get; set; }

    public string PersonId => RowKey;
    public bool IsAdult => PersonType == SignInPersonType.Adult;

    /// <summary>The finance scout or guardian id, whichever applies.</summary>
    public string? LinkedId => IsAdult ? LinkedGuardianId : LinkedScoutId;

    public bool IsLinked => LinkedId is not null;

    public const string PartitionKeyValue = "SignInPerson";
}
