using System.Security.Claims;
using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>
/// The treasurer-side people list: records everyone who signs into the admin area with Microsoft
/// or Google (a first sign-in is an access request), and lets Editors give, change or take away
/// Read-only / Edit access. Someone who uses both a Microsoft and a Google account is one person
/// with one role, as long as both providers vouch for the same address. The management methods
/// throw <see cref="InvalidOperationException"/> with a message fit to show when a change isn't
/// allowed.
/// </summary>
public interface IAdminUserService
{
    /// <summary>The signed-in person's row -- created as an access request on first sign-in --
    /// or null for anyone not signed in with Microsoft or Google. Cached briefly.</summary>
    Task<AdminUserEntity?> ResolveAsync(ClaimsPrincipal user);

    /// <summary>The stored role of the person an account (AdminIdentity.Key) belongs to -- None if
    /// it's unknown. Cached briefly; never creates.</summary>
    Task<string> GetRoleAsync(string signInKey);

    Task<IReadOnlyList<AdminUserEntity>> GetAllAsync();

    /// <summary>Every account anyone has signed in with; each one's UserKey is its person.</summary>
    Task<IReadOnlyList<AdminSignInEntity>> GetAllSignInsAsync();

    /// <summary>Gives (or changes) Read-only or Edit access. Granting Edit sends the "Edit access
    /// granted" email, if that's turned on.</summary>
    Task SetRoleAsync(string userKey, string role, ClaimsPrincipal actor);

    /// <summary>Takes away access, or dismisses a request: the row is deleted, so signing in
    /// again just makes a new request.</summary>
    Task RemoveAsync(string userKey, ClaimsPrincipal actor);

    /// <summary>Makes two people who share an email address one person with both their accounts
    /// and the higher of their two roles. For accounts that weren't grouped automatically --
    /// that happens only when both providers vouch for the address.</summary>
    Task CombineAsync(string userKey, string otherUserKey, ClaimsPrincipal actor);

    /// <summary>Marks (or clears) the committee chair. Marking one clears any other.</summary>
    Task SetCommitteeChairAsync(string userKey, bool isChair, ClaimsPrincipal actor);

    /// <summary>Allows a Read-only person to turn on Edit for themselves, a few hours at a time,
    /// through <paramref name="lastDay"/> (Pacific); after that day the permission, and any Edit
    /// they have on, ends. Also changes the last day of a permission already given. Standing
    /// Editors and Global admins only.</summary>
    Task AllowTemporaryEditAsync(string userKey, DateOnly lastDay, ClaimsPrincipal actor);

    /// <summary>Stops allowing temporary Edit before its last day, ending any they have on.</summary>
    Task StopAllowingTemporaryEditAsync(string userKey, ClaimsPrincipal actor);

    /// <summary>The signed-in person turns on Edit for themselves, for one of
    /// AdminUserService.TemporaryEditDurations, giving a reason. Recorded in Activity History.</summary>
    Task StartTemporaryEditAsync(ClaimsPrincipal actor, TimeSpan duration, string reason);

    /// <summary>The signed-in person turns their temporary Edit off before it runs out.</summary>
    Task EndTemporaryEditAsync(ClaimsPrincipal actor);
}
