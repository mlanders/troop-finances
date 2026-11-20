using System.Security.Cryptography;
using System.Text;

namespace BsaTroopFinances.Services;

/// <summary>
/// Lets a scheduler outside the app start the nightly backup: POST /internal/backup with the
/// header "Authorization: Bearer &lt;Backup:TriggerKey&gt;". For hosts that stop the app while
/// nobody is using it, where the in-app timer (<see cref="NightlyBackupWorker"/>) wouldn't be
/// running at 2 AM. The request wakes the app, and stays open until the backup finishes, so the
/// host keeps it running for that long. Running it again the same day does nothing: the backup is
/// already complete. The endpoint doesn't exist unless the key is set.
/// </summary>
public static class BackupTrigger
{
    public const string Path = "/internal/backup";

    /// <summary>Long enough that guessing it is out of the question (e.g. 32 random bytes as hex or base64).</summary>
    public const int MinimumKeyLength = 32;

    public static bool IsExternalSchedule(IConfiguration configuration) =>
        string.Equals(configuration["Backup:Schedule"], "External", StringComparison.OrdinalIgnoreCase);

    /// <summary>The configured key, or null when it's missing or too short to be safe.</summary>
    public static string? Key(IConfiguration configuration)
    {
        var key = configuration["Backup:TriggerKey"]?.Trim();
        return key is { Length: >= MinimumKeyLength } ? key : null;
    }

    /// <summary>Whether the request carries the key, compared in constant time.</summary>
    public static bool IsAuthorized(string? authorizationHeader, string key)
    {
        const string scheme = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorizationHeader[scheme.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(key);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
