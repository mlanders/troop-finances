namespace BsaTroopFinances.Services;

/// <summary>
/// Everything that makes this copy of the app one troop's: its name, web address, contact,
/// time zone and charter details, from the "Troop" settings section (plus Email:SenderAddress).
/// Any troop can run the app by setting these (see infra/HOSTING.md "Your troop's settings");
/// nothing about a particular troop is written into the code.
/// </summary>
public sealed record TroopInfo
{
    public const string Section = "Troop";

    /// <summary>"Troop 52": in page headings, email subjects, PDFs and letters.</summary>
    public required string Name { get; init; }

    /// <summary>The home-screen name when the site is installed as an app, e.g. "52 Finance".</summary>
    public required string ShortName { get; init; }

    /// <summary>The name guardians see for this site in their authenticator app, e.g. "T52 Finance
    /// Guardian". Changing it doesn't stop existing app entries working; they just keep the old name.</summary>
    public required string AuthenticatorName { get; init; }

    /// <summary>The site's address, ending in "/", e.g. "https://finance.troop52.org/".</summary>
    public required string SiteUrl { get; init; }

    /// <summary>Where families send questions (usually the treasurer).</summary>
    public required string ContactEmail { get; init; }

    /// <summary>The time zone every date is shown in, as an IANA name (e.g. "America/Chicago").</summary>
    public required string TimeZone { get; init; }

    /// <summary>The address emails come from (Email:SenderAddress).</summary>
    public required string SenderAddress { get; init; }

    public string CharteredOrganization { get; init; } = "";

    public string Ein { get; init; } = "";

    /// <summary>The few characters under the mark on the home-screen icon, e.g. "52" (Troop:IconText;
    /// by default the number from "Troop 52"). Empty for none.</summary>
    public string IconText { get; init; } = "";

    /// <summary>"Troop 52 Finance": the site's name.</summary>
    public string AppName => $"{Name} Finance";

    /// <summary>"finance.troop52.org".</summary>
    public string SiteHost => Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri) ? uri.Host : SiteUrl;

    /// <summary>The settings a real (Production) site can't run without. Each is a key in the app's
    /// configuration; as an environment variable a ":" becomes "__".</summary>
    public static readonly string[] Required =
        ["Troop:Name", "Troop:SiteUrl", "Troop:ContactEmail", "Troop:TimeZone", "Email:SenderAddress", "Auth:GlobalAdmins"];

    public static TroopInfo From(IConfiguration configuration)
    {
        string Setting(string key) => configuration[$"{Section}:{key}"]?.Trim() ?? "";

        var name = Setting("Name") is { Length: > 0 } given ? given : "Troop";
        var shortName = Setting("ShortName") is { Length: > 0 } shortGiven
            ? shortGiven
            : $"{(name.StartsWith("Troop ", StringComparison.OrdinalIgnoreCase) ? name[6..] : name)} Finance";
        var site = Setting("SiteUrl") is { Length: > 0 } url ? url : HostAddress(configuration) ?? "http://localhost";
        var siteUrl = site.EndsWith('/') ? site : site + "/";
        var iconText = Setting("IconText") is { Length: > 0 } caption
            ? caption
            : name.StartsWith("Troop ", StringComparison.OrdinalIgnoreCase) && name[6..].Trim() is { Length: > 0 and <= 4 } number ? number : "";
        return new TroopInfo
        {
            Name = name,
            ShortName = shortName,
            AuthenticatorName = Setting("AuthenticatorName") is { Length: > 0 } issuer ? issuer : $"{shortName} Guardian",
            SiteUrl = siteUrl,
            ContactEmail = Setting("ContactEmail"),
            TimeZone = Setting("TimeZone") is { Length: > 0 } zone ? zone : "America/Los_Angeles",
            SenderAddress = configuration["Email:SenderAddress"]?.Trim() ?? "",
            CharteredOrganization = Setting("CharteredOrganization"),
            Ein = Setting("Ein"),
            IconText = iconText,
        };
    }

    /// <summary>The address a host gives the site when it doesn't have its own domain yet (Render's
    /// RENDER_EXTERNAL_URL, e.g. https://troop-finances.onrender.com), so Troop:SiteUrl can wait.</summary>
    private static string? HostAddress(IConfiguration configuration) =>
        configuration["RENDER_EXTERNAL_URL"] is { Length: > 0 } address ? address : null;

    /// <summary>
    /// Makes "local time" the troop's: every page formats dates via ToLocalTime()/DateTime.Today,
    /// and .NET reads TZ on Linux (the usual host, otherwise on UTC) when it first resolves
    /// TimeZoneInfo.Local. Windows ignores TZ and uses the machine's own zone, so local development
    /// shows whatever the developer's PC is set to.
    /// </summary>
    public static void UseTimeZone(IConfiguration configuration)
    {
        Environment.SetEnvironmentVariable("TZ", From(configuration).TimeZone);
        TimeZoneInfo.ClearCachedData();
    }

    /// <summary>The <see cref="Required"/> settings that are empty, plus any that are set but wrong.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();
        foreach (var key in Required)
        {
            var section = configuration.GetSection(key);
            var empty = string.IsNullOrWhiteSpace(section.Value) && !section.GetChildren().Any(c => !string.IsNullOrWhiteSpace(c.Value));
            if (empty && !(key == "Troop:SiteUrl" && HostAddress(configuration) is not null))
            {
                problems.Add($"{key} isn't set");
            }
        }

        var troop = From(configuration);
        if (!string.IsNullOrEmpty(configuration["Troop:SiteUrl"]) && !(Uri.TryCreate(troop.SiteUrl, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http"))
        {
            problems.Add("Troop:SiteUrl must be a full address, like https://finance.example.org/");
        }

        if (!string.IsNullOrEmpty(configuration["Troop:TimeZone"]) && !TimeZoneInfo.TryFindSystemTimeZoneById(troop.TimeZone, out _))
        {
            problems.Add($"Troop:TimeZone '{troop.TimeZone}' isn't a time zone this machine knows (use a name like America/Chicago)");
        }

        return problems;
    }
}
