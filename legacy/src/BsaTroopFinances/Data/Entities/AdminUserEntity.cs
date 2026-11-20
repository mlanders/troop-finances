namespace BsaTroopFinances.Data.Entities;

/// <summary>What a treasurer-side (admin) user may do. None is a signed-in person still waiting
/// for an Editor to give them access -- an access request.</summary>
public static class AdminAccessRole
{
    public const string None = "None";
    public const string Viewer = "Viewer";
    public const string Editor = "Editor";

    public static string DisplayName(string role) => role switch
    {
        Viewer => "Read only",
        Editor => "Edit",
        _ => "No access",
    };
}

/// <summary>
/// One row per person who has ever signed into the admin area with Microsoft or Google. Each
/// account they've used is an <see cref="AdminSignInEntity"/> pointing here, so all of them get
/// this one role. Keyed by the first account's key ("microsoft:{oid}" / "google:{sub}", see
/// AdminIdentity.Key), never by email, so a changed or reused address can't take over someone
/// else's access. A first sign-in creates the row with <see cref="AdminAccessRole.None"/>; Editors
/// then grant a role from /admin/settings/access.
/// </summary>
public class AdminUserEntity
{
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    /// <summary>From whichever of their accounts signed in most recently.</summary>
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = AdminAccessRole.None;

    /// <summary>Gets a copy of the "Edit access granted" email. At most one person at a time.</summary>
    public bool IsCommitteeChair { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    /// <summary>The latest sign-in with any of their accounts.</summary>
    public DateTimeOffset LastSeenUtc { get; set; }
    public string RoleChangedBy { get; set; } = string.Empty;
    public DateTimeOffset? RoleChangedUtc { get; set; }

    /// <summary>A Viewer an Editor has allowed to turn on Edit for themselves, for a few hours at
    /// a time, when they need it. Never set on an Editor.</summary>
    public bool CanTurnOnEdit { get; set; }

    /// <summary>When the Editor's permission in <see cref="CanTurnOnEdit"/> runs out: the end of
    /// the last day the Editor picked (Pacific). Null only for permissions given before end dates
    /// existed, which last until an Editor sets one or stops them.</summary>
    public DateTimeOffset? CanTurnOnEditUntilUtc { get; set; }

    /// <summary>When the temporary Edit they turned on ends; null when it isn't on.</summary>
    public DateTimeOffset? EditUntilUtc { get; set; }

    /// <summary>Why they turned on the current temporary Edit.</summary>
    public string EditReason { get; set; } = string.Empty;

    public const string PartitionKeyValue = "AdminUser";

    public string UserKey => RowKey;
    public bool HasAccess => Role is AdminAccessRole.Viewer or AdminAccessRole.Editor;

    /// <summary>Listed in Auth:GlobalAdmins (by an address the provider vouches for), kept in step
    /// at each sign-in. Read only day to day, but can always turn on temporary Edit and can allow
    /// others to. Set only from configuration, never from the website.</summary>
    public bool IsGlobalAdmin { get; set; }

    /// <summary>An Editor's permission to turn on temporary Edit that hasn't run out.</summary>
    public bool IsAllowedTemporaryEdit(DateTimeOffset now) =>
        CanTurnOnEdit && (CanTurnOnEditUntilUtc is null || CanTurnOnEditUntilUtc > now);

    /// <summary>Read only, and allowed to turn on temporary Edit: by an Editor (until the end date
    /// they set), or as Global admin.</summary>
    public bool CanElevate(DateTimeOffset now) => Role == AdminAccessRole.Viewer && (IsAllowedTemporaryEdit(now) || IsGlobalAdmin);

    /// <summary>On, and still allowed: when an Editor's permission runs out, Edit ends with it.</summary>
    public bool IsTemporaryEditOn(DateTimeOffset now) => CanElevate(now) && EditUntilUtc > now;

    /// <summary>The role they act with right now: Editor while temporary Edit is on, else the
    /// stored role. The stored role stays Viewer, so temporary Edit never counts as an Editor
    /// for the last-Editor or bootstrap checks.</summary>
    public string EffectiveRole(DateTimeOffset now) => IsTemporaryEditOn(now) ? AdminAccessRole.Editor : Role;
}
