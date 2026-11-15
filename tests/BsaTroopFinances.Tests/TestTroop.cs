using BsaTroopFinances.Services;

namespace BsaTroopFinances.Tests;

/// <summary>The troop the tests run as: deliberately not any real troop, so a test catches text
/// that's still written into the code instead of coming from the settings.</summary>
public static class TestTroop
{
    public static readonly TroopInfo Info = new()
    {
        Name = "Troop 52",
        ShortName = "52 Finance",
        AuthenticatorName = "T52 Finance Guardian",
        SiteUrl = "https://finance.troop52.example/",
        ContactEmail = "treasurer@troop52.example",
        TimeZone = "America/Chicago",
        SenderAddress = "noreply@troop52.example",
    };
}
