namespace BsaTroopFinances.Services;

/// <summary>
/// Picks how email is sent, from Email:Provider (see infra/HOSTING.md):
///   AzureCommunicationServices   ConnectionStrings:AzureCommunicationServices (as deployed by infra/main.bicep)
///   Smtp                         Email:Smtp:Host, :Port, :Username, :Password, :Security
///   Console                      written to the log, nothing sent
/// Left empty, it's Console in Development and Azure Communication Services everywhere else --
/// what the app did before the setting existed.
/// </summary>
public static class EmailProviders
{
    public const string AzureCommunicationServices = "AzureCommunicationServices";
    public const string Smtp = "Smtp";
    public const string Console = "Console";

    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["Email:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return environment.IsDevelopment() ? Console : AzureCommunicationServices;
        }

        foreach (var known in new[] { AzureCommunicationServices, Smtp, Console })
        {
            if (provider.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        throw new InvalidOperationException($"Email:Provider '{provider}' isn't known. Use {AzureCommunicationServices}, {Smtp} or {Console}.");
    }

    public static void Add(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        switch (Resolve(configuration, environment))
        {
            case Console:
                services.AddSingleton<IEmailSender, ConsoleEmailSender>();
                break;
            case Smtp:
                services.AddSingleton<IEmailSender>(_ => new SmtpEmailSender(configuration));
                break;
            default:
                services.AddScoped<IEmailSender, AzureCommunicationServicesEmailSender>();
                break;
        }
    }
}
