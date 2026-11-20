using System.Security.Claims;

namespace BsaTroopFinances.Services;

/// <summary>
/// Who a person signed into the admin area is, from their Microsoft or Google sign-in's claims.
/// Their key is the provider plus the id the provider
/// gives them -- "microsoft:{object id}" or "google:{subject}" -- which nobody else can claim.
/// Their email is shown to help recognise them, but isn't who they are: a work account's
/// "email" claim can be set to anything by its own organisation (see <see cref="VerifiedEmail"/>
/// for the one place an address is trusted).
/// </summary>
public static class AdminIdentity
{
    public const string Microsoft = "Microsoft";
    public const string Google = "Google";

    /// <summary>Added to Google sign-ins from the userinfo response (see Program.cs).</summary>
    public const string EmailVerifiedClaimType = "email_verified";

    private const string ObjectIdType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>"Microsoft" or "Google", or empty if not signed in with either (a guardian, or
    /// the local DevAuth stand-in).</summary>
    public static string Provider(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return string.Empty;
        }

        if (First(user, ObjectIdType, "oid") is not null)
        {
            return Microsoft;
        }

        return user.FindFirst(ClaimTypes.NameIdentifier) is { } id && id.Issuer.Contains("google", StringComparison.OrdinalIgnoreCase)
            ? Google
            : string.Empty;
    }

    /// <summary>The person's key, or empty if they aren't signed in with Microsoft or Google.</summary>
    public static string Key(ClaimsPrincipal user) => Provider(user) switch
    {
        Microsoft => $"microsoft:{First(user, ObjectIdType, "oid")!.ToLowerInvariant()}",
        Google => $"google:{user.FindFirst(ClaimTypes.NameIdentifier)!.Value}",
        _ => string.Empty,
    };

    public static string Name(ClaimsPrincipal user) =>
        First(user, "name", ClaimTypes.Name) ?? Email(user) ?? "Someone";

    public static string? Email(ClaimsPrincipal user) =>
        First(user, ClaimTypes.Email, "email", "preferred_username");

    /// <summary>
    /// An address the provider vouches for, or null: Google's email when Google says it's
    /// verified, or a Microsoft account's sign-in name (preferred_username), which has to be on a
    /// domain the account's own tenant has proven it owns. A Microsoft "email" claim is never
    /// used -- an outside tenant can set it to any address.
    /// </summary>
    public static string? VerifiedEmail(ClaimsPrincipal user) => Provider(user) switch
    {
        Google when string.Equals(user.FindFirst(EmailVerifiedClaimType)?.Value, "true", StringComparison.OrdinalIgnoreCase)
            => First(user, ClaimTypes.Email, "email"),
        Microsoft => First(user, "preferred_username"),
        _ => null,
    };

    /// <summary>A sign-in's "come back to" address, kept on this site: anything else becomes the fallback.</summary>
    public static string SafeReturnUrl(string? returnUrl, string fallback = "/admin") =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : fallback;

    private static string? First(ClaimsPrincipal user, params string[] types) =>
        types.Select(type => user.FindFirst(type)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
