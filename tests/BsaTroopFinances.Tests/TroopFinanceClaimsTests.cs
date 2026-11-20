using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;

namespace BsaTroopFinances.Tests;

/// <summary>What each combination of the app's own claims lets someone do in /admin.</summary>
public class TroopFinanceClaimsTests
{
    private static ClaimsPrincipal User(string? role = null, DateTimeOffset? editUntil = null, bool canTurnOnEdit = false, bool globalAdmin = false)
    {
        var claims = new List<Claim> { new("oid", "someone") };
        if (role is not null)
        {
            claims.Add(new(TroopFinanceClaimsExtensions.RoleClaimType, role));
        }
        if (editUntil is { } until)
        {
            claims.Add(new(TroopFinanceClaimsExtensions.EditUntilClaimType, until.ToString("O")));
        }
        if (canTurnOnEdit)
        {
            claims.Add(new(TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, "true"));
        }
        if (globalAdmin)
        {
            claims.Add(new(TroopFinanceClaimsExtensions.GlobalAdminClaimType, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static readonly DateTimeOffset Until = new(2026, 11, 19, 20, 30, 0, TimeSpan.FromHours(-8));

    [Fact]
    public void Someone_with_no_role_has_no_access_at_all()
    {
        var user = User();

        Assert.Equal(AdminAccessRole.None, user.TroopFinanceRole());
        Assert.False(user.HasTroopFinanceAccess());
        Assert.False(user.IsTroopFinanceWriter());
        Assert.False(user.CanAllowTemporaryEdit());
    }

    [Fact]
    public void A_viewer_can_look_but_not_change_anything()
    {
        var user = User(AdminAccessRole.Viewer);

        Assert.Equal(AdminAccessRole.Viewer, user.TroopFinanceRole());
        Assert.True(user.HasTroopFinanceAccess());
        Assert.False(user.IsTroopFinanceWriter());
        Assert.False(user.IsStandingTroopFinanceWriter());
        Assert.False(user.CanAllowTemporaryEdit());
    }

    [Fact]
    public void A_standing_editor_can_change_things_and_allow_others_temporary_edit()
    {
        var user = User(AdminAccessRole.Editor);

        Assert.Equal(AdminAccessRole.Editor, user.TroopFinanceRole());
        Assert.True(user.HasTroopFinanceAccess());
        Assert.True(user.IsStandingTroopFinanceWriter());
        Assert.True(user.CanAllowTemporaryEdit());
        Assert.Null(user.TemporaryEditUntil());
    }

    [Fact]
    public void An_editor_on_temporary_edit_can_change_things_but_not_hand_out_edit()
    {
        var user = User(AdminAccessRole.Editor, editUntil: Until, canTurnOnEdit: true);

        Assert.True(user.IsTroopFinanceWriter());
        Assert.False(user.IsStandingTroopFinanceWriter());
        Assert.False(user.CanAllowTemporaryEdit());
        Assert.Equal(Until, user.TemporaryEditUntil());
        Assert.True(user.CanTurnOnEdit());
    }

    [Fact]
    public void A_global_admin_viewer_can_allow_temporary_edit_without_being_an_editor()
    {
        var user = User(AdminAccessRole.Viewer, globalAdmin: true);

        Assert.True(user.IsGlobalAdmin());
        Assert.True(user.CanAllowTemporaryEdit());
        Assert.False(user.IsTroopFinanceWriter());
    }

    [Fact]
    public void Claim_values_other_than_the_exact_ones_grant_nothing()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TroopFinanceClaimsExtensions.RoleClaimType, "editor"),
            new Claim(TroopFinanceClaimsExtensions.GlobalAdminClaimType, "True"),
            new Claim(TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, "yes"),
            new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, "not a date"),
        ], "test"));

        Assert.Equal(AdminAccessRole.None, user.TroopFinanceRole());
        Assert.False(user.IsGlobalAdmin());
        Assert.False(user.CanTurnOnEdit());
        Assert.False(user.CanAllowTemporaryEdit());
        Assert.Null(user.TemporaryEditUntil());
    }

    [Fact]
    public void Every_claim_the_app_adds_itself_is_listed_for_stripping() =>
        Assert.Equal(
            [TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, TroopFinanceClaimsExtensions.EditUntilClaimType,
             TroopFinanceClaimsExtensions.GlobalAdminClaimType, TroopFinanceClaimsExtensions.RoleClaimType],
            TroopFinanceClaimsExtensions.AppClaimTypes.Order());
}
