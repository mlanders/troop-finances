using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BsaTroopFinances.Tests;

public class AccessSettingsPageTests : AdminPageTestBase
{
    private readonly IAdminUserService _users;

    private readonly AdminUserEntity _me = new() { RowKey = "microsoft:me", Name = "Pat Treasurer", Email = "pat@example.com", Role = AdminAccessRole.Editor };
    private readonly AdminUserEntity _viewer = new() { RowKey = "google:viv", Name = "Viv Reader", Email = "viv@example.com", Role = AdminAccessRole.Viewer };
    private readonly AdminUserEntity _request = new() { RowKey = "google:new", Name = "Jordan New", Email = "jordan@example.com", Role = AdminAccessRole.None };

    public AccessSettingsPageTests()
    {
        _users = Fake<IAdminUserService>();
        _users.GetAllAsync().Returns([_me, _viewer, _request]);
        _users.GetAllSignInsAsync().Returns(
        [
            new AdminSignInEntity { RowKey = "microsoft:me", UserKey = "microsoft:me", Provider = "Microsoft", Email = "pat@example.com", LastSeenUtc = new(2026, 10, 20, 17, 0, 0, TimeSpan.Zero) },
            new AdminSignInEntity { RowKey = "google:pat", UserKey = "microsoft:me", Provider = "Google", Email = "pat@example.com", LastSeenUtc = new(2026, 10, 28, 17, 0, 0, TimeSpan.Zero) },
            new AdminSignInEntity { RowKey = "google:viv", UserKey = "google:viv", Provider = "Google", Email = "viv@example.com" },
            new AdminSignInEntity { RowKey = "google:new", UserKey = "google:new", Provider = "Google", Email = "jordan@example.com" },
        ]);
    }

    /// <summary>Signed in as the Editor in _me (one of the accounts on that person).</summary>
    private void SignInAsMe()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Pat Treasurer");
        auth.SetClaims(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, AdminAccessRole.Editor), new Claim("oid", "me"));
        _users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(_me);
    }

    private static IEnumerable<string> MenuItems(IRenderedComponent<AccessSettings> page, string personName) =>
        page.FindAll("tbody tr")
            .Single(row => row.QuerySelector("td")!.TextContent.Contains(personName))
            .QuerySelectorAll(".ellipsis-menu-item")
            .Select(i => i.TextContent.Trim());

    [Fact]
    public void Requests_and_people_with_access_are_listed_separately()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<AccessSettings>();

        var tables = page.FindAll("table.report-table");
        Assert.Equal(2, tables.Count);
        Assert.Contains("Jordan New", tables[0].TextContent);
        Assert.DoesNotContain("Viv Reader", tables[0].TextContent);
        Assert.Contains("Viv Reader", tables[1].TextContent);
        Assert.Contains("Read only", tables[1].TextContent);
    }

    [Fact]
    public void Each_account_someone_signs_in_with_shows_as_a_pill_with_its_last_sign_in()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<AccessSettings>();

        var pat = page.FindAll("tbody tr").Single(row => row.TextContent.Contains("Pat Treasurer"));
        var pills = pat.QuerySelectorAll(".sign-in-pill").Select(p => p.TextContent.Trim()).ToList();
        Assert.Equal(2, pills.Count);
        // Most recent first.
        Assert.StartsWith("Google", pills[0]);
        Assert.Contains(new DateTimeOffset(2026, 10, 28, 17, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("MMM d, yyyy h:mm tt"), pills[0]);
        Assert.StartsWith("Microsoft", pills[1]);
        Assert.Contains(new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("MMM d, yyyy h:mm tt"), pills[1]);
    }

    [Fact]
    public void Two_entries_with_the_same_email_can_be_combined_after_confirming()
    {
        var twin = new AdminUserEntity { RowKey = "google:viv2", Name = "Viv Reader", Email = "VIV@example.com", Role = AdminAccessRole.Editor };
        _users.GetAllAsync().Returns([_me, _viewer, twin, _request]);
        SignInAsMe();

        var page = Render<AccessSettings>();
        Assert.Equal(["Give read-only access", "Give edit access", "Dismiss request"], MenuItems(page, "Jordan New"));
        var combine = page.FindAll("tbody .ellipsis-menu-item").First(b => b.TextContent.Trim().StartsWith("Combine with Viv Reader"));
        combine.Click();
        _users.DidNotReceiveWithAnyArgs().CombineAsync(default!, default!, default!);

        Assert.Contains("Edit access whichever account", page.Find(".alert-info").TextContent);
        page.Find(".alert-info .btn-primary").Click();

        _users.Received(1).CombineAsync(Arg.Is<string>(k => k.StartsWith("google:viv")), Arg.Is<string>(k => k.StartsWith("google:viv")), Arg.Any<ClaimsPrincipal>());
    }

    [Fact]
    public void Read_only_users_see_the_lists_but_no_menus()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<AccessSettings>();

        Assert.Empty(page.FindAll(".ellipsis-menu"));
    }

    [Fact]
    public void An_editor_can_give_a_request_edit_access()
    {
        SignInAsMe();

        var page = Render<AccessSettings>();
        Assert.Equal(["Give read-only access", "Give edit access", "Dismiss request"], MenuItems(page, "Jordan New"));
        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Give edit access").Click();

        _users.Received(1).SetRoleAsync("google:new", AdminAccessRole.Editor, Arg.Any<ClaimsPrincipal>());
    }

    [Fact]
    public void An_editor_cant_change_or_remove_their_own_access()
    {
        SignInAsMe();

        var page = Render<AccessSettings>();

        Assert.Equal(["Mark as committee chair"], MenuItems(page, "Pat Treasurer"));
        Assert.Equal(["Change to edit", "Allow temporary edit", "Mark as committee chair", "Remove access"], MenuItems(page, "Viv Reader"));
    }

    [Fact]
    public void A_read_only_global_admin_can_only_allow_temporary_edit()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Gina Admin");
        auth.SetClaims(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, AdminAccessRole.Viewer),
            new Claim(TroopFinanceClaimsExtensions.GlobalAdminClaimType, "true"), new Claim("oid", "gina"));
        _users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity { RowKey = "microsoft:gina", Role = AdminAccessRole.Viewer, IsGlobalAdmin = true });

        var page = Render<AccessSettings>();

        Assert.Equal(["Allow temporary edit"], MenuItems(page, "Viv Reader"));
        Assert.Empty(MenuItems(page, "Pat Treasurer"));
        Assert.Empty(MenuItems(page, "Jordan New"));
    }

    [Fact]
    public void An_editor_can_allow_a_viewer_temporary_edit()
    {
        SignInAsMe();
        var page = Render<AccessSettings>();

        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Allow temporary edit").Click();
        // Nothing changes until the Editor picks the last day and confirms.
        _users.DidNotReceiveWithAnyArgs().AllowTemporaryEditAsync(default!, default, default!);
        var lastDay = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
        page.Find("#allowLastDay").Change(lastDay.ToString("yyyy-MM-dd"));
        page.Find("#confirmAllow").Click();

        _users.Received(1).AllowTemporaryEditAsync("google:viv", lastDay, Arg.Any<ClaimsPrincipal>());
    }

    [Fact]
    public void An_allowed_viewer_shows_their_last_day_and_the_editor_can_change_or_stop_it()
    {
        _viewer.CanTurnOnEdit = true;
        _viewer.CanTurnOnEditUntilUtc = DateTimeOffset.UtcNow.AddDays(5);
        SignInAsMe();
        var page = Render<AccessSettings>();

        var lastDay = _viewer.CanTurnOnEditUntilUtc.Value.AddTicks(-1).ToLocalTime();
        Assert.Contains($"Can turn on edit through {lastDay:MMM d}", page.FindAll("tbody tr").Single(r => r.TextContent.Contains("Viv Reader")).TextContent);
        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Stop allowing temporary edit").Click();
        _users.Received(1).StopAllowingTemporaryEditAsync("google:viv", Arg.Any<ClaimsPrincipal>());

        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Change temporary edit end date").Click();
        Assert.Equal(lastDay.ToString("yyyy-MM-dd"), page.Find("#allowLastDay").GetAttribute("value"));
    }

    [Fact]
    public void A_run_out_permission_shows_when_it_ended_and_can_be_given_again()
    {
        _viewer.CanTurnOnEdit = true;
        _viewer.CanTurnOnEditUntilUtc = DateTimeOffset.UtcNow.AddDays(-2);
        SignInAsMe();
        var page = Render<AccessSettings>();

        Assert.Contains("Temporary edit ended", page.FindAll("tbody tr").Single(r => r.TextContent.Contains("Viv Reader")).TextContent);
        Assert.Contains("Allow temporary edit", MenuItems(page, "Viv Reader"));
        Assert.DoesNotContain("Stop allowing temporary edit", MenuItems(page, "Viv Reader"));
    }

    [Fact]
    public void Someone_on_temporary_edit_gets_no_access_menus_and_others_temporary_edit_shows()
    {
        _viewer.CanTurnOnEdit = true;
        _viewer.EditUntilUtc = DateTimeOffset.UtcNow.AddHours(1);
        SignInAs(AdminRole.Writer, "Viv Reader",
            new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, _viewer.EditUntilUtc.Value.ToString("O")));

        var page = Render<AccessSettings>();

        Assert.Empty(page.FindAll(".ellipsis-menu"));
        Assert.Contains("Edit until", page.FindAll("tbody tr").Single(r => r.TextContent.Contains("Viv Reader")).TextContent);
    }

    [Fact]
    public void Removing_access_asks_first()
    {
        SignInAsMe();

        var page = Render<AccessSettings>();
        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Remove access").Click();
        _users.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!);

        page.Find(".alert-danger .btn-danger").Click();

        _users.Received(1).RemoveAsync("google:viv", Arg.Any<ClaimsPrincipal>());
    }

    [Fact]
    public void Settings_shows_how_many_are_waiting_for_access()
    {
        SignInAs(AdminRole.Reader);
        Fake<ITestDataLoader>();

        var page = Render<Settings>();

        Assert.Contains("1 waiting for access", page.Markup);
    }

    [Fact]
    public void A_refused_change_shows_why()
    {
        SignInAsMe();
        _users.SetRoleAsync("google:viv", AdminAccessRole.Editor, Arg.Any<ClaimsPrincipal>())
            .ThrowsAsync(new InvalidOperationException("Only Editors can change who has access."));

        var page = Render<AccessSettings>();
        page.FindAll("tbody .ellipsis-menu-item").Single(b => b.TextContent.Trim() == "Change to edit").Click();

        Assert.Contains("Only Editors can change who has access.", page.Find(".alert-danger").TextContent);
    }
}
