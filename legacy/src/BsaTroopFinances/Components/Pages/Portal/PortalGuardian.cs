using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.AspNetCore.Components.Authorization;

namespace BsaTroopFinances.Components.Pages.Portal;

/// <summary>Finds the guardian record for the signed-in parent: by GuardianId claim, else by email.</summary>
public static class PortalGuardian
{
    public static async Task<GuardianEntity?> ResolveAsync(Task<AuthenticationState>? authStateTask, IGuardianRepository guardians) =>
        await ResolveAsync(authStateTask is not null ? (await authStateTask).User : null, guardians);

    public static async Task<GuardianEntity?> ResolveAsync(ClaimsPrincipal? user, IGuardianRepository guardians)
    {
        var guardianId = user?.FindFirst("GuardianId")?.Value;
        if (!string.IsNullOrWhiteSpace(guardianId))
        {
            return await guardians.GetByIdAsync(guardianId);
        }

        var email = user?.FindFirst(ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? null : await guardians.GetByEmailAsync(email);
    }
}
