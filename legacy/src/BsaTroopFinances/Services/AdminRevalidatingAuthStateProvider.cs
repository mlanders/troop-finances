using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace BsaTroopFinances.Services;

/// <summary>
/// An open admin page (a live Blazor circuit) keeps the identity it started with, so without this
/// someone whose access was just removed or changed could keep clicking for hours. Every minute
/// it compares the circuit's role with the stored one; on a mismatch the circuit is signed out,
/// the page sends them back through sign-in, and the fresh request picks up the new role.
/// Guardians and the local DevAuth stand-in have no admin key and are always left alone.
/// </summary>
public sealed class AdminRevalidatingAuthStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        var key = AdminIdentity.Key(user);
        if (key.Length == 0)
        {
            return true;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IAdminUserService>().GetRoleAsync(key);
        return stored == user.TroopFinanceRole();
    }
}
