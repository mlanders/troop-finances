using BsaTroopFinances.Services;
using Bunit;

namespace BsaTroopFinances.Tests;

public class AppVersionTests
{
    private const string Sha = "85847da3c0ffee1234567890abcdef1234567890";
    private static readonly Func<string, string?> NoHost = _ => null;

    [Fact]
    public void The_github_build_shows_its_version_and_commit()
    {
        var version = AppVersion.From("2026.11.42", $"1.0.0+{Sha}", NoHost);

        Assert.Equal("Version 2026.11.42 (85847da)", version.Display);
        Assert.Equal(Sha, version.Commit);
    }

    [Fact]
    public void A_build_without_a_version_shows_its_commit()
    {
        Assert.Equal("Build 85847da", AppVersion.From(null, $"1.0.0+{Sha}", NoHost).Display);
    }

    [Theory]
    [InlineData("RENDER_GIT_COMMIT")]
    [InlineData("RAILWAY_GIT_COMMIT_SHA")]
    public void A_host_that_built_it_says_which_commit(string variable)
    {
        var version = AppVersion.From(null, "1.0.0", name => name == variable ? Sha : null);

        Assert.Equal("Build 85847da", version.Display);
    }

    [Fact]
    public void The_commit_built_in_wins_over_the_hosts()
    {
        var version = AppVersion.From(null, "1.0.0+aaaaaaa1111", _ => Sha);

        Assert.Equal("Build aaaaaaa", version.Display);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "1.0.0")]
    [InlineData(" ", "1.0.0+")]
    public void Nothing_known_says_so(string? appVersion, string? informational)
    {
        Assert.Equal("Unversioned build", AppVersion.From(appVersion, informational, NoHost).Display);
    }

    [Fact]
    public void This_build_has_a_version_line() =>
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current.Display));

    [Fact]
    public void The_footer_links_the_legal_pages_and_shows_the_version()
    {
        using var context = new BunitContext();
        var footer = context.Render<BsaTroopFinances.Components.SiteFooter>();

        Assert.Equal(["/privacy", "/terms"], footer.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal(AppVersion.Current.Display, footer.Find(".site-footer-version").TextContent);
    }
}
