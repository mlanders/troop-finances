using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The real AdminUserService over an in-memory AdminUsers table.</summary>
public class AdminUserServiceTests
{
    private readonly InMemoryAdminUsers _repository = new();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly TestClock _clock = new();
    private string _globalAdmin = "gina@example.com";
    private readonly AdminUserService _service;

    public AdminUserServiceTests()
    {
        _service = NewService();
    }

    /// <summary>Over the same table, with an empty cache -- like another server instance, or a minute later.</summary>
    private AdminUserService NewService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:BootstrapEditors:0"] = "steve@example.org",
                [$"{AdminUserService.GlobalAdminsKey}:0"] = _globalAdmin,
            })
            .Build();
        return new AdminUserService(_repository, _audit, _notifications, new MemoryCache(new MemoryCacheOptions()),
            configuration, _clock, NullLogger<AdminUserService>.Instance);
    }

    internal static ClaimsPrincipal Microsoft(string oid, string name, string email, string? role = null)
    {
        List<Claim> claims = [new("oid", oid), new("name", name), new("preferred_username", email)];
        if (role is not null)
        {
            claims.Add(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "AuthenticationTypes.Federation", "name", "roles"));
    }

    internal static ClaimsPrincipal Google(string sub, string name, string email, bool verified)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, sub, ClaimValueTypes.String, "Google"),
            new(ClaimTypes.Name, name, ClaimValueTypes.String, "Google"),
            new(ClaimTypes.Email, email, ClaimValueTypes.String, "Google"),
            new(AdminIdentity.EmailVerifiedClaimType, verified ? "True" : "False", ClaimValueTypes.String, "Google"),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Google"));
    }

    private AdminUserEntity Seed(string key, string name, string role, bool chair = false)
    {
        var row = new AdminUserEntity { RowKey = key, Name = name, Email = $"{name.ToLowerInvariant()}@example.com", Role = role, IsCommitteeChair = chair };
        _repository.Rows[key] = row;
        return row;
    }

    private static readonly ClaimsPrincipal Pat = Microsoft("pat-oid", "Pat", "pat@example.com", AdminAccessRole.Editor);

    [Fact]
    public async Task A_first_sign_in_is_recorded_as_an_access_request()
    {
        var row = await _service.ResolveAsync(Google("1234", "Jordan Lee", "jordan@example.com", verified: true));

        Assert.NotNull(row);
        Assert.Equal("google:1234", row.RowKey);
        Assert.Equal(AdminAccessRole.None, row.Role);
        Assert.Equal(("Jordan Lee", "jordan@example.com"), (row.Name, row.Email));
        Assert.True(_repository.Rows.ContainsKey("google:1234"));
        var signIn = _repository.SignIns["google:1234"];
        Assert.Equal(("google:1234", "Google", "jordan@example.com"), (signIn.UserKey, signIn.Provider, signIn.VerifiedEmail));
    }

    [Fact]
    public async Task A_second_account_with_the_same_verified_address_joins_the_same_person()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("pat-oid", "Pat", "Pat@Example.com"));

        var row = await _service.ResolveAsync(Google("pat-google", "Pat G", "pat@example.com", verified: true));

        Assert.Equal("microsoft:pat-oid", row!.RowKey);
        Assert.Equal(AdminAccessRole.Editor, row.Role);
        Assert.Single(_repository.Rows);
        Assert.Equal(["google:pat-google", "microsoft:pat-oid"], _repository.SignIns.Values.Where(s => s.UserKey == "microsoft:pat-oid").Select(s => s.RowKey).Order());
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("google:pat-google"));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:pat-oid", "Sign-in Linked", "Pat G", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task An_address_Google_hasnt_verified_never_joins_anyone()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("pat-oid", "Pat", "pat@example.com"));

        var row = await _service.ResolveAsync(Google("imposter", "Pat?", "pat@example.com", verified: false));

        Assert.Equal("google:imposter", row!.RowKey);
        Assert.Equal(AdminAccessRole.None, row.Role);
    }

    [Fact]
    public async Task A_request_made_before_the_address_was_known_joins_once_it_is()
    {
        // Pat had access before accounts were recorded separately: the migration copied their
        // account over with no verified address yet.
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Viewer);
        _repository.SignIns["microsoft:pat-oid"] = new AdminSignInEntity { RowKey = "microsoft:pat-oid", UserKey = "microsoft:pat-oid", Provider = "Microsoft" };
        var google = Google("pat-google", "Pat", "pat@example.com", verified: true);
        Assert.Equal(AdminAccessRole.None, (await _service.ResolveAsync(google))!.Role);

        await _service.ResolveAsync(Microsoft("pat-oid", "Pat", "pat@example.com"));
        var row = await NewService().ResolveAsync(google);

        Assert.Equal(("microsoft:pat-oid", AdminAccessRole.Viewer), (row!.RowKey, row.Role));
        Assert.False(_repository.Rows.ContainsKey("google:pat-google"));
        Assert.Equal("microsoft:pat-oid", _repository.SignIns["google:pat-google"].UserKey);
    }

    [Fact]
    public async Task Two_people_who_already_had_access_are_combined_once_both_addresses_are_vouched_for()
    {
        // Both accounts were given Edit separately, before they could be grouped.
        Seed("microsoft:jamie-oid", "Jamie", AdminAccessRole.Editor);
        Seed("google:jamie", "Jamie", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("jamie-oid", "Jamie", "jamie@example.com"));

        var row = await _service.ResolveAsync(Google("jamie", "Jamie", "jamie@example.com", verified: true));

        Assert.Single(_repository.Rows);
        Assert.Equal(AdminAccessRole.Editor, row!.Role);
        Assert.Equal(2, _repository.SignIns.Values.Count(s => s.UserKey == row.RowKey));
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("microsoft:jamie-oid"));
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("google:jamie"));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey, "People Combined", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Combining_keeps_the_higher_role_and_the_committee_chair()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        Seed("microsoft:jo-oid", "Jo", AdminAccessRole.Viewer, chair: true);
        Seed("google:jo", "Jo", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("jo-oid", "Jo", "jo@example.com"));

        var row = await _service.ResolveAsync(Google("jo", "Jo", "jo@example.com", verified: true));

        Assert.Equal(("google:jo", AdminAccessRole.Editor, true), (row!.RowKey, row.Role, row.IsCommitteeChair));
        Assert.False(_repository.Rows.ContainsKey("microsoft:jo-oid"));
        Assert.Equal("google:jo", _repository.SignIns["microsoft:jo-oid"].UserKey);
    }

    [Fact]
    public async Task An_editor_can_combine_two_people_who_share_an_address()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        var ms = Seed("microsoft:jamie-oid", "Jamie", AdminAccessRole.Editor);
        var google = Seed("google:jamie", "Jamie", AdminAccessRole.Viewer);
        ms.Email = google.Email = "jamie@example.com";

        await _service.CombineAsync("google:jamie", "microsoft:jamie-oid", Pat);

        Assert.Equal(AdminAccessRole.Editor, _repository.Rows["microsoft:jamie-oid"].Role);
        Assert.False(_repository.Rows.ContainsKey("google:jamie"));
    }

    [Fact]
    public async Task People_with_different_addresses_cant_be_combined_or_combined_by_a_viewer()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        Seed("google:jordan", "Jordan", AdminAccessRole.Viewer);
        var viewer = Microsoft("viv-oid", "Viv", "viv@example.com", AdminAccessRole.Viewer);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CombineAsync("google:jordan", "microsoft:sam-oid", Pat));
        Assert.Contains("share an email address", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CombineAsync("google:jordan", "microsoft:sam-oid", viewer));
        Assert.Equal(2, _repository.Rows.Count);
    }

    [Fact]
    public async Task A_role_change_applies_to_every_account_at_once()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("jordan-oid", "Jordan", "jordan@example.com"));
        await _service.ResolveAsync(Google("jordan-google", "Jordan", "jordan@example.com", verified: true));
        Assert.Equal(AdminAccessRole.None, await _service.GetRoleAsync("google:jordan-google"));

        await _service.SetRoleAsync("microsoft:jordan-oid", AdminAccessRole.Viewer, Pat);

        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("microsoft:jordan-oid"));
        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("google:jordan-google"));
    }

    [Fact]
    public async Task Nobody_changes_their_own_access_from_their_other_account()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("pat-oid", "Pat", "pat@example.com"));
        var patOnGoogle = Google("pat-google", "Pat", "pat@example.com", verified: true);
        await _service.ResolveAsync(patOnGoogle);
        var actor = new ClaimsPrincipal(patOnGoogle.Identities.Single().Clone());
        ((ClaimsIdentity)actor.Identity!).AddClaim(new Claim(TroopFinanceClaimsExtensions.RoleClaimType, AdminAccessRole.Editor));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("microsoft:pat-oid", AdminAccessRole.Viewer, actor));
        Assert.Contains("your own access", ex.Message);
    }

    [Fact]
    public async Task Removing_someone_forgets_all_their_accounts()
    {
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);
        await _service.ResolveAsync(Microsoft("jordan-oid", "Jordan", "jordan@example.com"));
        await _service.ResolveAsync(Google("jordan-google", "Jordan", "jordan@example.com", verified: true));

        await _service.RemoveAsync("microsoft:jordan-oid", Pat);

        Assert.Empty(_repository.SignIns.Values.Where(s => s.UserKey == "microsoft:jordan-oid"));
        Assert.Equal(AdminAccessRole.None, await _service.GetRoleAsync("google:jordan-google"));
    }

    [Fact]
    public async Task Guardians_and_dev_sign_ins_are_not_recorded()
    {
        var guardian = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "maria@example.com"), new Claim("GuardianId", "maria")], "ParentAuth"));

        Assert.Null(await _service.ResolveAsync(guardian));
        Assert.Empty(_repository.Rows);
    }

    [Fact]
    public async Task A_bootstrap_address_becomes_the_first_editor()
    {
        var row = await _service.ResolveAsync(Microsoft("ABC-OID", "Steve", "Steve@example.org"));

        Assert.Equal("microsoft:abc-oid", row!.RowKey);
        Assert.Equal(AdminAccessRole.Editor, row.Role);
        Assert.Equal(AdminAccessRole.Editor, _repository.Rows["microsoft:abc-oid"].Role);
    }

    [Fact]
    public async Task A_bootstrap_address_gets_nothing_once_an_editor_exists()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);

        var row = await _service.ResolveAsync(Microsoft("other-oid", "Not Steve", "steve@example.org"));

        Assert.Equal(AdminAccessRole.None, row!.Role);
    }

    [Fact]
    public async Task A_bootstrap_address_Google_hasnt_verified_gets_nothing()
    {
        var row = await _service.ResolveAsync(Google("999", "Steve?", "steve@example.org", verified: false));

        Assert.Equal(AdminAccessRole.None, row!.Role);
    }

    [Fact]
    public async Task Granting_edit_access_is_audited_and_announced()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("google:1234", "Jordan", AdminAccessRole.None);

        await _service.SetRoleAsync("google:1234", AdminAccessRole.Editor, Pat);

        Assert.Equal(AdminAccessRole.Editor, _repository.Rows["google:1234"].Role);
        Assert.Equal("Pat", _repository.Rows["google:1234"].RoleChangedBy);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "google:1234", "Access Granted (Edit)", "Pat", Arg.Any<string?>(), Arg.Any<string?>());
        await _notifications.Received(1).EditorGrantedAsync(Arg.Is<AdminUserEntity>(u => u.RowKey == "google:1234"), "Pat");
    }

    [Fact]
    public async Task Granting_read_only_access_sends_no_email()
    {
        Seed("google:1234", "Jordan", AdminAccessRole.None);

        await _service.SetRoleAsync("google:1234", AdminAccessRole.Viewer, Pat);

        Assert.Equal(AdminAccessRole.Viewer, _repository.Rows["google:1234"].Role);
        await _notifications.DidNotReceiveWithAnyArgs().EditorGrantedAsync(default!, default!);
    }

    [Fact]
    public async Task A_change_applies_at_once_even_after_the_role_was_cached()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        var jordan = Microsoft("jordan-oid", "Jordan", "jordan@example.com");
        await _service.ResolveAsync(jordan);
        Assert.Equal(AdminAccessRole.None, await _service.GetRoleAsync("microsoft:jordan-oid"));

        await _service.SetRoleAsync("microsoft:jordan-oid", AdminAccessRole.Viewer, Pat);

        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("microsoft:jordan-oid"));
        Assert.Equal(AdminAccessRole.Viewer, (await _service.ResolveAsync(jordan))!.Role);
    }

    [Fact]
    public async Task Nobody_changes_their_own_access()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("microsoft:pat-oid", AdminAccessRole.Viewer, Pat));
        Assert.Contains("your own access", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RemoveAsync("microsoft:pat-oid", Pat));
        Assert.Equal(AdminAccessRole.Editor, _repository.Rows["microsoft:pat-oid"].Role);
    }

    [Fact]
    public async Task The_last_editor_cant_be_demoted_or_removed()
    {
        // Only reachable by an Editor whose own row is gone (e.g. a dev sign-in), but the rule holds regardless.
        Seed("microsoft:sam-oid", "Sam", AdminAccessRole.Editor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("microsoft:sam-oid", AdminAccessRole.Viewer, Pat));
        Assert.Contains("only Editor", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RemoveAsync("microsoft:sam-oid", Pat));
        Assert.True(_repository.Rows.ContainsKey("microsoft:sam-oid"));
    }

    [Fact]
    public async Task Read_only_users_cant_change_access()
    {
        Seed("google:1234", "Jordan", AdminAccessRole.None);
        var viewer = Microsoft("viv-oid", "Viv", "viv@example.com", AdminAccessRole.Viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("google:1234", AdminAccessRole.Editor, viewer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RemoveAsync("google:1234", viewer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetCommitteeChairAsync("google:1234", true, viewer));
        Assert.Equal(AdminAccessRole.None, _repository.Rows["google:1234"].Role);
    }

    [Fact]
    public async Task Removing_access_deletes_the_row_and_is_audited()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("google:1234", "Jordan", AdminAccessRole.Viewer);

        await _service.RemoveAsync("google:1234", Pat);

        Assert.False(_repository.Rows.ContainsKey("google:1234"));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "google:1234", "Access Removed", "Pat", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task There_is_only_ever_one_committee_chair()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("google:1", "Old Chair", AdminAccessRole.Viewer, chair: true);
        Seed("google:2", "New Chair", AdminAccessRole.Viewer);

        await _service.SetCommitteeChairAsync("google:2", true, Pat);

        Assert.Equal(["google:2"], _repository.Rows.Values.Where(u => u.IsCommitteeChair).Select(u => u.RowKey));
    }

    private static readonly ClaimsPrincipal Jordan = Microsoft("jordan-oid", "Jordan", "jordan@example.com", AdminAccessRole.Viewer);

    /// <summary>Jordan while on temporary Edit, as AdminRoleClaimsTransformation would sign them in.</summary>
    private ClaimsPrincipal JordanOnTemporaryEdit()
    {
        var principal = Microsoft("jordan-oid", "Jordan", "jordan@example.com", AdminAccessRole.Editor);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, _clock.GetUtcNow().AddHours(1).ToString("O")));
        return principal;
    }

    [Fact]
    public async Task An_allowed_viewer_can_turn_on_edit_until_it_runs_out()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);
        await _service.AllowTemporaryEditAsync("microsoft:jordan-oid", NextWeek, Pat);

        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(2), "  Fix the October dues invoice  ");

        var row = _repository.Rows["microsoft:jordan-oid"];
        Assert.Equal((AdminAccessRole.Viewer, "Fix the October dues invoice"), (row.Role, row.EditReason));
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("microsoft:jordan-oid"));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:jordan-oid", "Temporary Edit Started", "Jordan", Arg.Any<string?>(), Arg.Any<string?>());

        _clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("microsoft:jordan-oid"));
    }

    [Fact]
    public async Task Temporary_edit_can_be_ended_early()
    {
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer).CanTurnOnEdit = true;
        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "Record a payment");

        await _service.EndTemporaryEditAsync(Jordan);

        Assert.Null(_repository.Rows["microsoft:jordan-oid"].EditUntilUtc);
        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("microsoft:jordan-oid"));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:jordan-oid", "Temporary Edit Ended Early", "Jordan", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Temporary_edit_needs_permission_a_reason_and_an_allowed_length()
    {
        var row = Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "Fix it"));
        row.CanTurnOnEdit = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "   "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(24), "Fix it"));
        Assert.Null(_repository.Rows["microsoft:jordan-oid"].EditUntilUtc);
    }

    [Fact]
    public async Task Only_a_viewer_can_be_allowed_temporary_edit()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("google:2", "Sam", AdminAccessRole.Editor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AllowTemporaryEditAsync("google:2", NextWeek, Pat));
    }

    [Fact]
    public async Task Temporary_edit_doesnt_count_as_an_editor_for_the_last_editor_check()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer).CanTurnOnEdit = true;
        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "Fix it");

        Assert.Equal(1, await _repository.CountWithRoleAsync(AdminAccessRole.Editor));
    }

    [Fact]
    public async Task Temporary_edit_cant_change_who_has_access()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer).CanTurnOnEdit = true;
        Seed("google:3", "Riley", AdminAccessRole.Viewer);
        var actor = JordanOnTemporaryEdit();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("google:3", AdminAccessRole.Editor, actor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RemoveAsync("google:3", actor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AllowTemporaryEditAsync("google:3", NextWeek, actor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetCommitteeChairAsync("google:3", true, actor));
        Assert.Equal(AdminAccessRole.Viewer, _repository.Rows["google:3"].Role);
    }

    [Fact]
    public async Task Standing_edit_or_disallowing_clears_temporary_edit()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer).CanTurnOnEdit = true;
        Seed("google:3", "Riley", AdminAccessRole.Viewer).CanTurnOnEdit = true;
        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "Fix it");

        await _service.StopAllowingTemporaryEditAsync("microsoft:jordan-oid", Pat);
        await _service.SetRoleAsync("google:3", AdminAccessRole.Editor, Pat);

        var jordan = _repository.Rows["microsoft:jordan-oid"];
        Assert.False(jordan.CanTurnOnEdit);
        Assert.Null(jordan.EditUntilUtc);
        Assert.Equal(AdminAccessRole.Viewer, await _service.GetRoleAsync("microsoft:jordan-oid"));
        Assert.False(_repository.Rows["google:3"].CanTurnOnEdit);
    }

    [Fact]
    public async Task Turning_on_temporary_edit_sends_the_alert()
    {
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer).CanTurnOnEdit = true;

        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(4), "Record a payment");

        await _notifications.Received(1).TemporaryEditStartedAsync(
            Arg.Is<AdminUserEntity>(u => u.RowKey == "microsoft:jordan-oid"), _clock.GetUtcNow().AddHours(4), "Record a payment");
    }

    private static readonly ClaimsPrincipal Gina = Microsoft("gina-oid", "Gina", "gina@example.com");

    [Fact]
    public async Task A_global_admin_gets_read_only_and_can_turn_on_edit()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);

        var row = await _service.ResolveAsync(Gina);

        Assert.Equal((AdminAccessRole.Viewer, true, true), (row!.Role, row.IsGlobalAdmin, row.CanElevate(_clock.GetUtcNow())));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:gina-oid", "Global Admin Set", AdminUserService.GlobalAdminsKey, Arg.Any<string?>(), Arg.Any<string?>());

        await _service.StartTemporaryEditAsync(Gina, TimeSpan.FromHours(1), "Fix a payment");
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("microsoft:gina-oid"));
        await _notifications.Received(1).TemporaryEditStartedAsync(Arg.Any<AdminUserEntity>(), Arg.Any<DateTimeOffset>(), "Fix a payment");
    }

    [Fact]
    public async Task A_global_admin_is_cleared_once_no_longer_listed()
    {
        await _service.ResolveAsync(Gina);
        _globalAdmin = "someone-else@example.com";

        var row = await NewService().ResolveAsync(Gina);

        Assert.Equal((AdminAccessRole.Viewer, false, false), (row!.Role, row.IsGlobalAdmin, row.CanElevate(_clock.GetUtcNow())));
    }

    [Fact]
    public async Task A_read_only_global_admin_can_allow_others_temporary_edit_but_nothing_else()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("google:3", "Riley", AdminAccessRole.Viewer);
        await _service.ResolveAsync(Gina);
        var gina = Microsoft("gina-oid", "Gina", "gina@example.com", AdminAccessRole.Viewer);
        ((ClaimsIdentity)gina.Identity!).AddClaim(new Claim(TroopFinanceClaimsExtensions.GlobalAdminClaimType, "true"));

        await _service.AllowTemporaryEditAsync("google:3", NextWeek, gina);

        Assert.True(_repository.Rows["google:3"].CanTurnOnEdit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetRoleAsync("google:3", AdminAccessRole.Editor, gina));
    }

    [Fact]
    public async Task A_plain_viewer_cant_allow_temporary_edit()
    {
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);
        Seed("google:3", "Riley", AdminAccessRole.Viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AllowTemporaryEditAsync("google:3", NextWeek, Jordan));
    }

    // The clock starts at 10:00 AM Pacific on Wed Nov 4, 2026.
    private static readonly DateOnly NextWeek = new(2026, 11, 11);

    /// <summary>Midnight Pacific (PST, UTC-8) at the end of Nov 6.</summary>
    private static readonly DateTimeOffset EndOfNov6 = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Allowing_temporary_edit_ends_after_the_last_day_picked()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);

        await _service.AllowTemporaryEditAsync("microsoft:jordan-oid", new DateOnly(2026, 11, 6), Pat);

        var row = _repository.Rows["microsoft:jordan-oid"];
        Assert.Equal((true, EndOfNov6), (row.CanTurnOnEdit, row.CanTurnOnEditUntilUtc));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:jordan-oid", "Temporary Edit Allowed", "Pat",
            Arg.Any<string?>(), Arg.Is<string?>(d => d!.Contains("through Nov 6, 2026")));

        // 11:30 PM on the last day: two hours of Edit is cut short at midnight.
        _clock.Advance(EndOfNov6 - _clock.GetUtcNow() - TimeSpan.FromMinutes(30));
        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(2), "Fix it");
        Assert.Equal(EndOfNov6, _repository.Rows["microsoft:jordan-oid"].EditUntilUtc);
        Assert.Equal(AdminAccessRole.Editor, await _service.GetRoleAsync("microsoft:jordan-oid"));

        _clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(AdminAccessRole.Viewer, await NewService().GetRoleAsync("microsoft:jordan-oid"));
        Assert.False(_repository.Rows["microsoft:jordan-oid"].CanElevate(_clock.GetUtcNow()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(1), "Fix it"));
    }

    [Fact]
    public async Task The_last_day_must_be_from_today_to_a_year_out()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AllowTemporaryEditAsync("microsoft:jordan-oid", new DateOnly(2026, 11, 3), Pat));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AllowTemporaryEditAsync("microsoft:jordan-oid", new DateOnly(2027, 11, 6), Pat));
        Assert.False(_repository.Rows["microsoft:jordan-oid"].CanTurnOnEdit);

        await _service.AllowTemporaryEditAsync("microsoft:jordan-oid", new DateOnly(2026, 11, 4), Pat);
        Assert.True(_repository.Rows["microsoft:jordan-oid"].IsAllowedTemporaryEdit(_clock.GetUtcNow()));
    }

    [Fact]
    public async Task Moving_the_last_day_earlier_cuts_short_edit_that_is_on()
    {
        Seed("microsoft:pat-oid", "Pat", AdminAccessRole.Editor);
        Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);
        await _service.AllowTemporaryEditAsync("microsoft:jordan-oid", NextWeek, Pat);
        _clock.Advance(TimeSpan.FromHours(13));   // 11:00 PM on Nov 4
        await _service.StartTemporaryEditAsync(Jordan, TimeSpan.FromHours(4), "Fix it");

        await _service.AllowTemporaryEditAsync("microsoft:jordan-oid", new DateOnly(2026, 11, 4), Pat);

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero), _repository.Rows["microsoft:jordan-oid"].EditUntilUtc);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.AdminUser, "microsoft:jordan-oid", "Temporary Edit End Date Changed", "Pat",
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task A_permission_from_before_end_dates_keeps_working_until_one_is_set()
    {
        var row = Seed("microsoft:jordan-oid", "Jordan", AdminAccessRole.Viewer);
        row.CanTurnOnEdit = true;

        _clock.Advance(TimeSpan.FromDays(400));

        Assert.True(row.CanElevate(_clock.GetUtcNow()));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        // The app runs on Pacific time; pinned so the tests don't depend on the machine's zone.
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        public void Advance(TimeSpan by) => _now += by;
    }

    internal sealed class InMemoryAdminUsers : IAdminUserRepository
    {
        public Dictionary<string, AdminUserEntity> Rows { get; } = [];

        public Task<AdminUserEntity?> GetAsync(string userKey) =>
            Task.FromResult(Rows.TryGetValue(userKey, out var row) ? Copy(row) : null);

        public Task<IReadOnlyList<AdminUserEntity>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<AdminUserEntity>>(Rows.Values.Select(Copy).ToList());

        public Task<int> CountWithRoleAsync(string role) => Task.FromResult(Rows.Values.Count(u => u.Role == role));

        public Task AddAsync(AdminUserEntity user)
        {
            Rows.Add(user.RowKey, Copy(user));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AdminUserEntity user)
        {
            Rows[user.RowKey] = Copy(user);
            return Task.CompletedTask;
        }

        public Dictionary<string, AdminSignInEntity> SignIns { get; } = [];

        public Task DeleteAsync(string userKey)
        {
            Rows.Remove(userKey);
            foreach (var key in SignIns.Values.Where(s => s.UserKey == userKey).Select(s => s.RowKey).ToList())
            {
                SignIns.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task<AdminSignInEntity?> GetSignInAsync(string signInKey) =>
            Task.FromResult(SignIns.TryGetValue(signInKey, out var row) ? Copy(row) : null);

        public Task<IReadOnlyList<AdminSignInEntity>> GetAllSignInsAsync() =>
            Task.FromResult<IReadOnlyList<AdminSignInEntity>>(SignIns.Values.Select(Copy).ToList());

        public Task<IReadOnlyList<AdminSignInEntity>> GetSignInsForUserAsync(string userKey) =>
            Task.FromResult<IReadOnlyList<AdminSignInEntity>>(SignIns.Values.Where(s => s.UserKey == userKey).Select(Copy).ToList());

        public Task<IReadOnlyList<AdminSignInEntity>> FindSignInsByVerifiedEmailAsync(string verifiedEmail) =>
            Task.FromResult<IReadOnlyList<AdminSignInEntity>>(SignIns.Values.Where(s => s.VerifiedEmail == verifiedEmail)
                .OrderByDescending(s => s.LastSeenUtc).Select(Copy).ToList());

        public Task AddSignInAsync(AdminSignInEntity signIn)
        {
            SignIns.Add(signIn.RowKey, Copy(signIn));
            return Task.CompletedTask;
        }

        public Task UpdateSignInAsync(AdminSignInEntity signIn)
        {
            SignIns[signIn.RowKey] = Copy(signIn);
            return Task.CompletedTask;
        }

        // Like the database: what's stored isn't the caller's object.
        private static AdminUserEntity Copy(AdminUserEntity u) => new()
        {
            RowKey = u.RowKey, Name = u.Name, Email = u.Email, Role = u.Role,
            IsCommitteeChair = u.IsCommitteeChair, FirstSeenUtc = u.FirstSeenUtc, LastSeenUtc = u.LastSeenUtc,
            RoleChangedBy = u.RoleChangedBy, RoleChangedUtc = u.RoleChangedUtc,
            CanTurnOnEdit = u.CanTurnOnEdit, CanTurnOnEditUntilUtc = u.CanTurnOnEditUntilUtc, EditUntilUtc = u.EditUntilUtc, EditReason = u.EditReason, IsGlobalAdmin = u.IsGlobalAdmin,
        };

        private static AdminSignInEntity Copy(AdminSignInEntity s) => new()
        {
            RowKey = s.RowKey, UserKey = s.UserKey, Provider = s.Provider, Name = s.Name, Email = s.Email,
            VerifiedEmail = s.VerifiedEmail, FirstSeenUtc = s.FirstSeenUtc, LastSeenUtc = s.LastSeenUtc,
        };
    }
}
