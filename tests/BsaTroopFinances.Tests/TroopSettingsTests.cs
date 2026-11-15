using System.Text.Json;
using System.Text.RegularExpressions;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace BsaTroopFinances.Tests;

/// <summary>Any troop can run the app: what's particular to one troop comes from the "Troop"
/// settings (see Services/TroopInfo and infra/HOSTING.md), never from the code.</summary>
public partial class TroopSettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).Build();

    private static readonly (string, string?)[] Complete =
    [
        ("Troop:Name", "Troop 52"), ("Troop:SiteUrl", "https://finance.troop52.example"), ("Troop:ContactEmail", "treasurer@troop52.example"),
        ("Troop:TimeZone", "America/Chicago"), ("Email:SenderAddress", "noreply@troop52.example"), ("Auth:GlobalAdmins:0", "pat@troop52.example"),
    ];

    [Fact]
    public void The_settings_name_the_site_and_fill_in_the_rest()
    {
        var troop = TroopInfo.From(Config(Complete));

        Assert.Equal("Troop 52", troop.Name);
        Assert.Equal("Troop 52 Finance", troop.AppName);
        Assert.Equal("52 Finance", troop.ShortName);
        Assert.Equal("52 Finance Guardian", troop.AuthenticatorName);
        Assert.Equal("https://finance.troop52.example/", troop.SiteUrl);
        Assert.Equal("finance.troop52.example", troop.SiteHost);
        Assert.Equal("noreply@troop52.example", troop.SenderAddress);
    }

    [Fact]
    public void Names_can_be_set_individually()
    {
        var troop = TroopInfo.From(Config([.. Complete, ("Troop:ShortName", "Pack Money"), ("Troop:AuthenticatorName", "T52 Finance Guardian")]));

        Assert.Equal("Pack Money", troop.ShortName);
        Assert.Equal("T52 Finance Guardian", troop.AuthenticatorName);
    }

    [Fact]
    public void A_complete_setup_has_no_problems()
    {
        Assert.Empty(TroopInfo.Problems(Config(Complete)));
    }

    [Fact]
    public void A_real_site_lists_every_missing_setting()
    {
        var problems = TroopInfo.Problems(Config());

        Assert.Equal(TroopInfo.Required.Select(key => $"{key} isn't set"), problems);
    }

    [Fact]
    public void A_wrong_time_zone_or_address_is_named()
    {
        var problems = TroopInfo.Problems(Config([.. Complete.Where(s => s.Item1 is not ("Troop:TimeZone" or "Troop:SiteUrl")),
            ("Troop:TimeZone", "Central"), ("Troop:SiteUrl", "finance troop52")]));

        Assert.Contains(problems, p => p.Contains("Troop:TimeZone 'Central'"));
        Assert.Contains(problems, p => p.Contains("Troop:SiteUrl"));
    }

    [Fact]
    public void The_installed_app_is_named_for_the_troop()
    {
        using var manifest = JsonDocument.Parse(TroopManifest.Json(TestTroop.Info));

        Assert.Equal("52 Finance", manifest.RootElement.GetProperty("short_name").GetString());
        Assert.Contains("Troop 52", manifest.RootElement.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("52")]
    [InlineData("")]
    public void The_home_screen_icons_are_drawn_with_the_troops_number(string caption)
    {
        var icons = new TroopIcons(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BsaTroopFinances", "wwwroot", "favicon.svg")), caption);

        foreach (var (file, (size, _)) in TroopIcons.Files)
        {
            var png = icons.Png(file)!;
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal((size, size), (bitmap.Width, bitmap.Height));
            Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));            // opaque, as iPhones need
            Assert.Contains(Pixels(bitmap), p => p.Red > 180 && p.Green < 60 && p.Blue < 70);   // the fleur-de-lis
            Assert.Contains(Pixels(bitmap), p => p.Red > 220 && p.Green > 160 && p.Blue < 60);  // the coin
            // The caption sits centred under the mark, so its first character covers the area just
            // left of centre. Only that side is checked: the coin's navy "$" is off to the right, and
            // with Linux's font (DejaVu, wider than Windows' Arial) its edge reaches about 57% across.
            var captionRows = Pixels(bitmap, SKRectI.Create((int)(size * 0.40), (int)(size * 0.64), (int)(size * 0.12), (int)(size * 0.18))).Count(p => p.Blue > 100 && p.Red < 40);
            Assert.True(caption.Length > 0 ? captionRows > 0 : captionRows == 0, $"{file}: {captionRows} navy pixels in the caption area");

            if (Environment.GetEnvironmentVariable("TROOPFIN_ICON_DUMP") is { Length: > 0 } folder)
            {
                File.WriteAllBytes(Path.Combine(folder, $"{(caption.Length > 0 ? caption : "none")}-{file}"), png);
            }
        }

        Assert.Null(icons.Png("other.png"));
    }

    private static IEnumerable<SKColor> Pixels(SKBitmap bitmap, SKRectI? area = null)
    {
        var box = area ?? SKRectI.Create(0, 0, bitmap.Width, bitmap.Height);
        for (var y = box.Top; y < box.Bottom; y += 2)
        {
            for (var x = box.Left; x < box.Right; x += 2)
            {
                yield return bitmap.GetPixel(x, y);
            }
        }
    }

    [Fact]
    public void The_icon_shows_the_troop_number_unless_told_otherwise()
    {
        Assert.Equal("52", TroopInfo.From(Config(("Troop:Name", "Troop 52"))).IconText);
        Assert.Equal("", TroopInfo.From(Config(("Troop:Name", "Pack Finance Committee"))).IconText);
        Assert.Equal("P52", TroopInfo.From(Config(("Troop:Name", "Troop 52"), ("Troop:IconText", "P52"))).IconText);
    }

    [Fact]
    public void The_site_address_can_come_from_the_host()
    {
        var config = Config([.. Complete.Where(s => s.Item1 != "Troop:SiteUrl"), ("RENDER_EXTERNAL_URL", "https://troop-finances.onrender.com")]);

        Assert.Empty(TroopInfo.Problems(config));
        Assert.Equal("https://troop-finances.onrender.com/", TroopInfo.From(config).SiteUrl);
    }

    [Theory]
    [InlineData("RAILWAY_PUBLIC_DOMAIN", "troop-finances-production.up.railway.app", null, null, "https://troop-finances-production.up.railway.app/")]
    [InlineData("FLY_APP_NAME", "troop52-finance", null, null, "https://troop52-finance.fly.dev/")]
    [InlineData("CONTAINER_APP_NAME", "troop-finances", "CONTAINER_APP_ENV_DNS_SUFFIX", "happy-hill-1234.westus3.azurecontainerapps.io", "https://troop-finances.happy-hill-1234.westus3.azurecontainerapps.io/")]
    public void Other_hosts_supply_the_site_address_too(string key, string value, string? key2, string? value2, string expected)
    {
        (string, string)[] host = key2 is null ? [(key, value)] : [(key, value), (key2, value2!)];
        var config = Config([.. Complete.Where(s => s.Item1 != "Troop:SiteUrl"), .. host]);

        Assert.Empty(TroopInfo.Problems(config));
        Assert.Equal(expected, TroopInfo.From(config).SiteUrl);
    }

    [Fact]
    public void A_site_address_that_is_set_wins_over_the_hosts()
    {
        var config = Config([.. Complete, ("FLY_APP_NAME", "troop52-finance")]);

        Assert.Equal(TroopInfo.From(Config(Complete)).SiteUrl, TroopInfo.From(config).SiteUrl);
    }

    [Fact]
    public void Half_of_a_container_apps_address_is_not_enough()
    {
        var config = Config([.. Complete.Where(s => s.Item1 != "Troop:SiteUrl"), ("CONTAINER_APP_NAME", "troop-finances")]);

        Assert.Contains("Troop:SiteUrl isn't set", TroopInfo.Problems(config));
    }

    /// <summary>
    /// The app's own files don't mention a particular troop, its web address, or a person: those
    /// belong in settings. (Fails with the offending lines, so a slip is easy to find.)
    /// </summary>
    [Fact]
    public void No_troop_or_person_is_written_into_the_app()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "BsaTroopFinances");
        string[] kinds = [".cs", ".razor", ".json", ".html", ".js", ".css", ".svg", ".webmanifest"];
        var found = Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories)
            .Where(f => kinds.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Segments(f, app).Any(s => s is "bin" or "obj" or "lib" or "Migrations" or "PostgresMigrations"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(app, f), Line: i + 1, Text: line)))
            .Where(l => TroopSpecific().IsMatch(l.Text) || EmailAddress().Matches(l.Text).Any(m => !m.Groups["domain"].Value.StartsWith("example.", StringComparison.OrdinalIgnoreCase)))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(found.Count == 0, "Move these into the Troop settings:\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"Troop 441|T441|troop441|441 Finance", RegexOptions.IgnoreCase)]
    private static partial Regex TroopSpecific();

    /// <summary>Any real-looking address: only example.com/example.org ones belong in the code.</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@(?<domain>[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|org|net|edu|us|io|dev|app|co))\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailAddress();

    private static IEnumerable<string> Segments(string file, string root) =>
        Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BsaTroopFinances.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Couldn't find the repository root.");
    }
}
