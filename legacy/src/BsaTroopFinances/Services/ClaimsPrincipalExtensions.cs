using System.Security.Claims;
using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>
/// /admin access comes from the person's row in the AdminUsers table, managed by Editors at
/// /admin/settings/access. <see cref="AdminRoleClaimsTransformation"/> turns that row into a
/// <see cref="RoleClaimType"/> claim on every request: "Viewer" can view treasurer data (policy
/// "AdminOnly"), "Editor" can view and make changes (policy "AdminWriter"). Someone signed in
/// with no role gets no access at all -- they're an access request.
/// </summary>
public static class TroopFinanceClaimsExtensions
{
    /// <summary>Never stored in the sign-in cookie; added fresh per request, and any copy that
    /// arrives from an identity provider is stripped first.</summary>
    public const string RoleClaimType = "troopfin_role";

    /// <summary>When the temporary Edit they turned on ends (round-trip "O" format); only present
    /// while it's on. Added and stripped like <see cref="RoleClaimType"/>.</summary>
    public const string EditUntilClaimType = "troopfin_edit_until";

    /// <summary>"true" for a Viewer allowed to turn on temporary Edit. Added and stripped like
    /// <see cref="RoleClaimType"/>.</summary>
    public const string CanTurnOnEditClaimType = "troopfin_can_turn_on_edit";

    /// <summary>"true" for someone listed in Auth:GlobalAdmins. Added and stripped like
    /// <see cref="RoleClaimType"/>.</summary>
    public const string GlobalAdminClaimType = "troopfin_global_admin";

    /// <summary>Every claim type this app adds itself, so any copy from a provider is dropped.</summary>
    public static readonly IReadOnlyList<string> AppClaimTypes = [RoleClaimType, EditUntilClaimType, CanTurnOnEditClaimType, GlobalAdminClaimType];

    /// <summary>Listed in Auth:GlobalAdmins: can turn on temporary Edit, and allow others to.</summary>
    public static bool IsGlobalAdmin(this ClaimsPrincipal user) =>
        user.HasClaim(GlobalAdminClaimType, "true");

    /// <summary>Can allow (or stop allowing) others temporary Edit: a standing Editor or the Global admin.</summary>
    public static bool CanAllowTemporaryEdit(this ClaimsPrincipal user) =>
        user.IsStandingTroopFinanceWriter() || user.IsGlobalAdmin();

    public static bool IsTroopFinanceWriter(this ClaimsPrincipal user) =>
        user.HasClaim(RoleClaimType, AdminAccessRole.Editor);

    /// <summary>An Editor by standing role, not just for a while: only they may change who has access.</summary>
    public static bool IsStandingTroopFinanceWriter(this ClaimsPrincipal user) =>
        user.IsTroopFinanceWriter() && user.TemporaryEditUntil() is null;

    /// <summary>When their temporary Edit ends, or null if they aren't on temporary Edit.</summary>
    public static DateTimeOffset? TemporaryEditUntil(this ClaimsPrincipal user) =>
        DateTimeOffset.TryParse(user.FindFirst(EditUntilClaimType)?.Value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var until) ? until : null;

    /// <summary>A Viewer allowed to turn on temporary Edit (whether or not it's on now).</summary>
    public static bool CanTurnOnEdit(this ClaimsPrincipal user) =>
        user.HasClaim(CanTurnOnEditClaimType, "true");

    /// <summary>Can view /admin at all: Viewer, or Editor (which implies read).</summary>
    public static bool HasTroopFinanceAccess(this ClaimsPrincipal user) =>
        user.HasClaim(RoleClaimType, AdminAccessRole.Viewer) || user.IsTroopFinanceWriter();

    /// <summary>The role behind the claim: Editor, Viewer, or None.</summary>
    public static string TroopFinanceRole(this ClaimsPrincipal user) =>
        user.IsTroopFinanceWriter() ? AdminAccessRole.Editor
        : user.HasTroopFinanceAccess() ? AdminAccessRole.Viewer
        : AdminAccessRole.None;
}
