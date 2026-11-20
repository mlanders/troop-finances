using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

public class TemporaryEditPageTests : AdminPageTestBase
{
    private readonly IAdminUserService _users;

    public TemporaryEditPageTests()
    {
        _users = Fake<IAdminUserService>();
    }

    private static Claim CanTurnOn => new(TroopFinanceClaimsExtensions.CanTurnOnEditClaimType, "true");

    [Fact]
    public void An_allowed_viewer_turns_it_on_with_a_reason_and_length()
    {
        SignInAs(AdminRole.Reader, "Steve Admin", CanTurnOn);
        var page = Render<TemporaryEdit>();

        var button = page.Find("button.btn-primary");
        Assert.True(button.HasAttribute("disabled"));
        page.Find("#editReason").Input("Fix the October dues invoice");
        page.Find("#editHours").Change("4");
        page.Find("button.btn-primary").Click();

        _users.Received(1).StartTemporaryEditAsync(Arg.Any<ClaimsPrincipal>(), TimeSpan.FromHours(4), "Fix the October dues invoice");
    }

    [Fact]
    public void An_allowed_viewer_sees_the_last_day_they_can_turn_it_on()
    {
        var until = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
        _users.ResolveAsync(Arg.Any<ClaimsPrincipal>()).Returns(new Data.Entities.AdminUserEntity { CanTurnOnEdit = true, CanTurnOnEditUntilUtc = until });
        SignInAs(AdminRole.Reader, "Steve Admin", CanTurnOn);

        var page = Render<TemporaryEdit>();

        Assert.Contains(until.AddTicks(-1).ToLocalTime().ToString("MMM d, yyyy"), page.Find("#allowedThrough").TextContent);
    }

    [Fact]
    public void While_it_is_on_it_shows_when_it_ends_and_can_be_ended_early()
    {
        var until = DateTimeOffset.UtcNow.AddHours(2);
        SignInAs(AdminRole.Writer, "Steve Admin", CanTurnOn, new Claim(TroopFinanceClaimsExtensions.EditUntilClaimType, until.ToString("O")));
        var page = Render<TemporaryEdit>();

        Assert.Contains(until.ToLocalTime().ToString("MMM d, h:mm tt"), page.Markup);
        page.FindAll("button").Single(b => b.TextContent.Contains("End edit access now")).Click();

        _users.Received(1).EndTemporaryEditAsync(Arg.Any<ClaimsPrincipal>());
    }

    [Fact]
    public void Someone_not_allowed_is_told_to_ask_an_editor()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<TemporaryEdit>();

        Assert.Contains("ask an", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }
}
