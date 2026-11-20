using System.Reflection;

namespace BsaTroopFinances.Services;

/// <summary>
/// Which build of the app is running, for the footer of the signed-in pages.
///
/// The GitHub build (.github/workflows/ci.yml) works out a version (v1.2.3 for a version tag,
/// otherwise year.month.run, e.g. 2026.11.42) and passes it and the commit into the Dockerfile,
/// which builds them into the app: the version as AssemblyMetadata "AppVersion", the commit as
/// SourceRevisionId, which the SDK appends to the informational version ("1.0.0+&lt;sha&gt;", also
/// on any build made from a git checkout). A host that builds the Dockerfile itself has no
/// version; Render and Railway still say which commit they built.
/// </summary>
public sealed record AppVersion(string? Version, string? Commit)
{
    public static AppVersion Current { get; } = From(typeof(AppVersion).Assembly, Environment.GetEnvironmentVariable);

    /// <summary>"Version 2026.11.42 (85847da)", "Build 85847da", or "Unversioned build".</summary>
    public string Display => (Version, ShortCommit) switch
    {
        ({ } version, { } commit) => $"Version {version} ({commit})",
        ({ } version, null) => $"Version {version}",
        (null, { } commit) => $"Build {commit}",
        _ => "Unversioned build",
    };

    public string? ShortCommit => Commit is { Length: > 7 } ? Commit[..7] : Commit;

    public static AppVersion From(Assembly assembly, Func<string, string?> environment) =>
        From(
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "AppVersion")?.Value,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            environment);

    /// <param name="version">The AppVersion the build was given, if any.</param>
    /// <param name="informational">The informational version, e.g. "1.0.0+85847da..." (the part
    /// after "+" is the commit, when the build knew it).</param>
    /// <param name="environment">Reads the host's variables, for the commit when the build didn't
    /// know it.</param>
    public static AppVersion From(string? version, string? informational, Func<string, string?> environment)
    {
        var commit = informational?.IndexOf('+') is int plus and >= 0 ? informational[(plus + 1)..] : null;

        commit = Clean(commit)
            ?? Clean(environment("RENDER_GIT_COMMIT"))
            ?? Clean(environment("RAILWAY_GIT_COMMIT_SHA"));
        return new AppVersion(Clean(version), commit);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
