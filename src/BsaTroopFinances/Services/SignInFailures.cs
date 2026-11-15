using Microsoft.AspNetCore.Authentication;

namespace BsaTroopFinances.Services;

/// <summary>
/// A sign-in that comes back from Microsoft or Google with an error (cancelled, consent declined,
/// ...) would otherwise throw and land on the generic Error page. This sends it to a friendly
/// retry page instead, after anything already handling it.
/// </summary>
public static class SignInFailures
{
    public const string RetryPath = "/admin/sign-in-failed";

    public static void SendToRetryPage(RemoteAuthenticationEvents events)
    {
        var previous = events.OnRemoteFailure;
        events.OnRemoteFailure = async context =>
        {
            await previous(context);
            if (context.Result is { Handled: true })
            {
                return;
            }

            context.Response.Redirect(RetryPath);
            context.HandleResponse();
        };
    }
}
