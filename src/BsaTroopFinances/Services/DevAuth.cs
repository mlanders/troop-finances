using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>
/// Local-only stand-in for the Microsoft/Google admin sign-in, so the admin area can be clicked
/// through (by a person, a browser test, or Claude) without a real account. Turned on with
/// DevAuth:Enabled=true, and only ever in the Development environment -- startup fails if it's
/// enabled anywhere else. When on, the admin cookie's challenge goes to /dev/signin instead of
/// /admin/signin, which issues the normal "AdminCookie" with a fake user holding the Editor
/// (Writer, default) or Viewer (Reader) role claim directly, or no role at all (None, to see the
/// "contact an administrator" page). The fake user isn't a Microsoft or Google sign-in, so it
/// has no AdminUsers row: the role claim transformation and the circuit re-check skip it.
///   /dev/signin?role=Writer|Reader|None&amp;returnUrl=/admin   sign in (or switch role)
///   /dev/signout                                               sign out
/// </summary>
public static class DevAuth
{
    public const string SignInPath = "/dev/signin";
    public const string SignOutPath = "/dev/signout";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("DevAuth:Enabled");
        if (enabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"DevAuth:Enabled is set in the {environment.EnvironmentName} environment. The fake admin sign-in is for local development only; remove the setting.");
        }

        return enabled;
    }

    /// <summary>Points the admin cookie's sign-in at /dev/signin instead of /admin/signin.</summary>
    public static void UseDevSignIn(CookieAuthenticationOptions options)
    {
        options.ForwardChallenge = null;
        options.LoginPath = SignInPath;
        options.ReturnUrlParameter = "returnUrl";
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet(SignInPath, async (HttpContext http, string? role, string? returnUrl) =>
        {
            (string name, string email, string? claimRole) = role?.ToLowerInvariant() switch
            {
                "reader" => ("Dev Reader", "dev.reader@localhost", AdminAccessRole.Viewer),
                "none" => ("Dev Requester", "dev.requester@localhost", null),
                _ => ("Dev Writer", "dev.writer@localhost", AdminAccessRole.Editor),
            };
            List<Claim> claims = [new Claim("name", name), new Claim("preferred_username", email)];
            if (claimRole is not null)
            {
                claims.Add(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, claimRole));
            }

            var identity = new ClaimsIdentity(claims, authenticationType: "DevAuth", nameType: "name", roleType: "roles");

            await http.SignInAsync("AdminCookie", new ClaimsPrincipal(identity));

            // Only same-site relative paths, so this can't be used as an open redirect.
            return Results.LocalRedirect(AdminIdentity.SafeReturnUrl(returnUrl));
        });

        app.MapGet(SignOutPath, async (HttpContext http) =>
        {
            await http.SignOutAsync("AdminCookie");
            return Results.LocalRedirect("/");
        });
    }
}
