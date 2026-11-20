namespace BsaTroopFinances.Services;

/// <summary>
/// The endpoints the meeting sign-in app calls, server to server, with the header
/// "Authorization: Bearer &lt;SignInApp:ApiKey&gt;" (the same key set in that app's Finance:ApiKey).
/// They don't exist until the key is set. The sign-in app sends its roster and its trips, takes
/// payments at meetings and reads back who has paid; nothing it sends issues or voids an invoice
/// by itself (see SignInAppService).
/// <list type="bullet">
/// <item>PUT /api/signin/roster: the whole roster, as a list of SignInRosterPerson.</item>
/// <item>PUT /api/signin/trips/{signUpId}: a TripUpload, saved for the treasurer to review.</item>
/// <item>GET /api/signin/trips/{signUpId}/status: a TripLineStatusDto for each person on it.</item>
/// <item>POST /api/signin/trips/{signUpId}/payments: a TripPaymentUpload. 200 when recorded (or
/// already recorded), 404 when the trip or person isn't here, 409 when their invoice isn't issued
/// yet, 422 when it can't be recorded (e.g. more than is owed), each with a message.</item>
/// <item>PUT /api/signin/trips/{signUpId}/fuel: a FuelUpload of drivers after the trip, set up as
/// pending reimbursements (SignInFuelService); 200 with a FuelLineStatusDto for each driver.</item>
/// <item>GET /api/signin/trips/{signUpId}/fuel: a FuelLineStatusDto for each driver set up.</item>
/// </list>
/// </summary>
public static class SignInAppApi
{
    public const string BasePath = "/api/signin";

    public static string? Key(IConfiguration configuration)
    {
        var key = configuration["SignInApp:ApiKey"]?.Trim();
        return key is { Length: >= BackupTrigger.MinimumKeyLength } ? key : null;
    }

    /// <summary>The same Bearer check as the backup trigger, in constant time.</summary>
    public static bool IsAuthorized(string? authorizationHeader, string key) => BackupTrigger.IsAuthorized(authorizationHeader, key);

    public static void Map(WebApplication app)
    {
        if (Key(app.Configuration) is not { } key)
        {
            if (!string.IsNullOrWhiteSpace(app.Configuration["SignInApp:ApiKey"]))
            {
                app.Logger.LogWarning("SignInApp:ApiKey is shorter than {Length} characters, so {Path} is turned off.",
                    BackupTrigger.MinimumKeyLength, BasePath);
            }

            return;
        }

        var api = app.MapGroup(BasePath)
            .AllowAnonymous()
            .DisableAntiforgery()
            .AddEndpointFilter(async (context, next) =>
                IsAuthorized(context.HttpContext.Request.Headers.Authorization, key)
                    ? await next(context)
                    : Results.Unauthorized());

        api.MapPut("/roster", async (List<SignInRosterPerson> roster, SignInAppService service) =>
        {
            await service.ReceiveRosterAsync(roster);
            return Results.NoContent();
        });

        api.MapPut("/trips/{signUpId}", async (string signUpId, TripUpload trip, SignInAppService service) =>
        {
            try
            {
                await service.ReceiveTripAsync(signUpId, trip);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        api.MapGet("/trips/{signUpId}/status", async (string signUpId, SignInAppService service) =>
            Results.Ok(await service.GetStatusAsync(signUpId)));

        api.MapPut("/trips/{signUpId}/fuel", async (string signUpId, FuelUpload fuel, SignInFuelService service) =>
        {
            try
            {
                return Results.Ok(await service.ReceiveFuelAsync(signUpId, fuel));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        api.MapGet("/trips/{signUpId}/fuel", async (string signUpId, SignInFuelService service) =>
            Results.Ok(await service.GetFuelStatusAsync(signUpId)));

        api.MapPost("/trips/{signUpId}/payments", async (string signUpId, TripPaymentUpload payment, SignInAppService service) =>
        {
            var result = await service.RecordPaymentAsync(signUpId, payment);
            var body = new { outcome = result.Outcome.ToString(), message = result.Message };
            return result.Outcome switch
            {
                TripPaymentOutcome.Recorded or TripPaymentOutcome.AlreadyRecorded => Results.Ok(body),
                TripPaymentOutcome.NotFound => Results.NotFound(body),
                TripPaymentOutcome.NotInvoiced => Results.Conflict(body),
                _ => Results.UnprocessableEntity(body),
            };
        });
    }
}
