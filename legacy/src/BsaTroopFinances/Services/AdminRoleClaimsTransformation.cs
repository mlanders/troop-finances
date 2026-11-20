using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using Microsoft.AspNetCore.Authentication;

namespace BsaTroopFinances.Services;

/// <summary>
/// Turns the signed-in person's AdminUsers row into a <see cref="TroopFinanceClaimsExtensions.RoleClaimType"/>
/// claim on each request, so the "AdminOnly"/"AdminWriter" policies and every page's
/// IsTroopFinanceWriter() check read the role as it is now, not as it was at sign-in. Also
/// records a first sign-in as an access request. Guardians (portal cookie) and the local DevAuth
/// stand-in aren't Microsoft or Google sign-ins, so they pass through untouched.
/// </summary>
public sealed class AdminRoleClaimsTransformation(IAdminUserService adminUsers, TimeProvider timeProvider) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (AdminIdentity.Key(principal).Length == 0)
        {
            return principal;
        }

        var row = await adminUsers.ResolveAsync(principal);
        var transformed = principal.Clone();
        var identity = (ClaimsIdentity)transformed.Identity!;

        // A role can only come from this app's own table: drop any claim of the same types an
        // identity provider (e.g. an outside tenant's claims mapping) might have slipped in.
        foreach (var claim in transformed.Identities
            .SelectMany(i => i.Claims.Where(c => TroopFinanceClaimsExtensions.AppClaimTypes.Contains(c.Type)).Select(c => (i, c))).ToList())
        {
            claim.i.TryRemoveClaim(claim.c);
        }

        if (row is { HasAccess: true })
        {
            var now = timeProvider.GetUtcNow();
            identity.AddClaim(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, row.EffectiveRole(now)));
            if (row.IsTemporaryEditOn(now))
            {
                identity.AddClaim(new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, row.EditUntilUtc!.Value.ToString("O")));
            }

            if (row.CanElevate(now))
            {
                identity.AddClaim(new Claim(TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, "true"));
            }

            if (row.IsGlobalAdmin)
            {
                identity.AddClaim(new Claim(TroopFinanceClaimsExtensions.GlobalAdminClaimType, "true"));
            }
        }

        return transformed;
    }
}
