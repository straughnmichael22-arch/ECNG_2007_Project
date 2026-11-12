using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace P3B.Remediation.Windows.WinGet;

/// <summary>
/// The only WinGet command lines P3B runs. Package IDs and versions are validated so they can
/// never be read as WinGet options. All commands are restricted to the <c>winget</c> source
/// (the <c>msstore</c> source needs agreements P3B does not accept on the user's behalf).
/// Never used: <c>--force</c>, <c>--override</c>, <c>--scope machine</c>.
/// </summary>
public static partial class WingetCommands
{
    public const string Executable = "winget";

    /// <summary>APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND, observed from <c>winget list</c> for an absent package.</summary>
    public const int NoInstalledPackageFound = unchecked((int)0x8A150014);

    /// <summary>Observed when a source's agreements have not been accepted.</summary>
    public const int SourceAgreementsNotAccepted = unchecked((int)0x8A150046);

    public static bool IsSafeToken(string? value) => value is not null && SafeToken().IsMatch(value);

    public static IReadOnlyList<string> Version() => ["--version"];

    public static IReadOnlyList<string> List(string packageId)
    {
        RequireSafe(packageId);
        return ["list", "--id", packageId, "--exact", "--source", "winget", "--disable-interactivity"];
    }

    public static IReadOnlyList<string> Install(string packageId, string version)
    {
        RequireSafe(packageId);
        RequireSafe(version);
        return
        [
            "install", "--id", packageId, "--exact", "--version", version,
            "--source", "winget", "--scope", "user", "--silent",
            "--accept-package-agreements", "--disable-interactivity",
        ];
    }

    public static IReadOnlyList<string> Uninstall(string packageId, string version)
    {
        RequireSafe(packageId);
        RequireSafe(version);
        return
        [
            "uninstall", "--id", packageId, "--exact", "--version", version,
            "--source", "winget", "--scope", "user", "--silent", "--disable-interactivity",
        ];
    }

    public static string FormatExitCode(int exitCode) => $"0x{exitCode:X8}";

    private static void RequireSafe(string value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (!IsSafeToken(value))
        {
            throw new ArgumentException($"'{value}' is not a safe WinGet package identifier or version.", parameterName);
        }
    }

    // Starts with a letter or digit (so it cannot be an option) and contains no spaces, quotes or shell characters.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}\z")]
    private static partial Regex SafeToken();
}
