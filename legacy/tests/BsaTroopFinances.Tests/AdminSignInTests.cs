using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>Who a sign-in is (AdminIdentity) and the role claim each request gets from it.</summary>
public class AdminSignInTests
{
    [Fact]
    public void Keys_come_from_the_providers_stable_ids_not_the_email()
    {
        Assert.Equal("microsoft:abc-oid", AdminIdentity.Key(AdminUserServiceTests.Microsoft("ABC-OID", "Pat", "pat@example.com")));
        Assert.Equal("google:1234", AdminIdentity.Key(AdminUserServiceTests.Google("1234", "Jordan", "jordan@example.com", verified: true)));

        var guardian = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "maria")], "ParentAuth"));
        Assert.Equal(string.Empty, AdminIdentity.Key(guardian));
    }

    [Fact]
    public void Only_addresses_the_provider_vouches_for_are_verified()
    {
        Assert.Equal("jordan@example.com", AdminIdentity.VerifiedEmail(AdminUserServiceTests.Google("1", "Jordan", "jordan@example.com", verified: true)));
        Assert.Null(AdminIdentity.VerifiedEmail(AdminUserServiceTests.Google("1", "Jordan", "jordan@example.com", verified: false)));

        // A Microsoft "email" claim can be set to anything by the account's own tenant; only the sign-in name counts.
        var spoofed = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "x"), new Claim("email", "steve@example.org")], "AuthenticationTypes.Federation"));
        Assert.Null(AdminIdentity.VerifiedEmail(spoofed));
        Assert.Equal("pat@example.com", AdminIdentity.VerifiedEmail(AdminUserServiceTests.Microsoft("x", "Pat", "pat@example.com")));
    }

    [Theory]
    [InlineData("/admin/invoices?status=Outstanding", "/admin/invoices?status=Outstanding")]
    [InlineData("https://evil.example/", "/admin")]
    [InlineData("//evil.example/", "/admin")]
    [InlineData("/\\evil.example/", "/admin")]
    [InlineData(null, "/admin")]
    public void Return_urls_stay_on_this_site(string? returnUrl, string expected) =>
        Assert.Equal(expected, AdminIdentity.SafeReturnUrl(returnUrl));

    [Fact]
    public async Task The_stored_role_becomes_the_role_claim_and_a_spoofed_one_is_dropped()
    {
        var users = Substitute.For<IAdminUserService>();
        users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity { RowKey = "microsoft:x", Role = AdminAccessRole.Viewer });
        var spoofed = AdminUserServiceTests.Microsoft("x", "Pat", "pat@example.com", role: AdminAccessRole.Editor);

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(spoofed);

        Assert.Equal([AdminAccessRole.Viewer], result.FindAll(TroopFinanceClaimsExtensions.RoleClaimType).Select(c => c.Value));
        Assert.False(result.IsTroopFinanceWriter());
        Assert.True(result.HasTroopFinanceAccess());
    }

    [Fact]
    public async Task Temporary_edit_signs_in_as_an_editor_until_it_runs_out()
    {
        var until = DateTimeOffset.UtcNow.AddHours(1);
        var users = Substitute.For<IAdminUserService>();
        users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity
        {
            RowKey = "microsoft:x", Role = AdminAccessRole.Viewer, CanTurnOnEdit = true, EditUntilUtc = until,
        });

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(AdminUserServiceTests.Microsoft("x", "Pat", "pat@example.com"));

        Assert.True(result.IsTroopFinanceWriter());
        Assert.False(result.IsStandingTroopFinanceWriter());
        Assert.True(result.CanTurnOnEdit());
        Assert.Equal(until, result.TemporaryEditUntil());
    }

    [Fact]
    public async Task When_the_editors_permission_runs_out_edit_that_is_on_ends_with_it()
    {
        var users = Substitute.For<IAdminUserService>();
        users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity
        {
            RowKey = "microsoft:x", Role = AdminAccessRole.Viewer, CanTurnOnEdit = true,
            CanTurnOnEditUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1), EditUntilUtc = DateTimeOffset.UtcNow.AddHours(1),
        });

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(AdminUserServiceTests.Microsoft("x", "Pat", "pat@example.com"));

        Assert.False(result.IsTroopFinanceWriter());
        Assert.Null(result.TemporaryEditUntil());
        Assert.False(result.CanTurnOnEdit());
    }

    [Fact]
    public async Task Run_out_temporary_edit_is_read_only_and_spoofed_edit_claims_are_dropped()
    {
        var users = Substitute.For<IAdminUserService>();
        users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity
        {
            RowKey = "microsoft:x", Role = AdminAccessRole.Viewer, EditUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        var spoofed = AdminUserServiceTests.Microsoft("x", "Pat", "pat@example.com");
        ((ClaimsIdentity)spoofed.Identity!).AddClaims(
        [
            new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, DateTimeOffset.UtcNow.AddHours(8).ToString("O")),
            new Claim(TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, "true"),
        ]);

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(spoofed);

        Assert.False(result.IsTroopFinanceWriter());
        Assert.Null(result.TemporaryEditUntil());
        Assert.False(result.CanTurnOnEdit());
    }

    [Fact]
    public async Task An_access_request_gets_no_role_claim()
    {
        var users = Substitute.For<IAdminUserService>();
        users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new AdminUserEntity { RowKey = "google:1", Role = AdminAccessRole.None });

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(AdminUserServiceTests.Google("1", "J", "j@example.com", verified: true));

        Assert.False(result.HasTroopFinanceAccess());
    }

    [Fact]
    public async Task Guardians_and_dev_sign_ins_pass_through_untouched()
    {
        var users = Substitute.For<IAdminUserService>();
        var dev = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Dev Writer"), new Claim(TroopFinanceClaimsExtensions.RoleClaimType, AdminAccessRole.Editor)], "DevAuth"));

        var result = await new AdminRoleClaimsTransformation(users, TimeProvider.System).TransformAsync(dev);

        Assert.Same(dev, result);
        await users.DidNotReceiveWithAnyArgs().ResolveAsync(default!);
    }
}
