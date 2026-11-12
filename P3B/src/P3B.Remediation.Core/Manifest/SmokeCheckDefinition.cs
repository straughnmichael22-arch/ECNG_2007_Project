using System.Text.RegularExpressions;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// One post-remediation check declared for a tool in the manifest. Each tool lists only the
/// checks that apply to it; nothing is assumed about tools that declare none.
/// </summary>
/// <remarks>
/// PROVISIONAL – the <c>smokeTests</c> section of docs/proposals/local-eda-tool-manifest.PROVISIONAL.md,
/// derived from 3B-IR-001 ("locations and invocation metadata") and P3-StR2.2 ("approved post-fix checks").
/// </remarks>
public abstract record SmokeCheckDefinition
{
    public abstract string Kind { get; }

    public abstract string Describe();
}

/// <summary>The installer (for example WinGet) reports the approved version as installed.</summary>
public sealed record InstalledVersionCheck : SmokeCheckDefinition
{
    public override string Kind => "installedVersion";

    public override string Describe() => "The package manager reports the approved version as installed.";
}

/// <summary>A command can be found on the user or system PATH.</summary>
public sealed record CommandOnPathCheck(string Command) : SmokeCheckDefinition
{
    public override string Kind => "commandOnPath";

    public override string Describe() => $"The command '{Command}' can be found on the user or system PATH.";
}

/// <summary>An expected file (for example the executable) exists. May start with an environment variable such as %LOCALAPPDATA%.</summary>
public sealed record FileExistsCheck(string Path) : SmokeCheckDefinition
{
    public override string Kind => "fileExists";

    public override string Describe() => $"The file '{Path}' exists.";
}

/// <summary>The tool launches, exits successfully and reports the approved version.</summary>
public sealed record VersionCommandCheck(string Command, IReadOnlyList<string> Arguments, TimeSpan Timeout) : SmokeCheckDefinition
{
    public override string Kind => "versionCommand";

    public override string Describe() =>
        $"Running '{string.Join(' ', Arguments.Prepend(Command))}' succeeds and reports the approved version.";
}

/// <summary>A user-level environment variable contains the expected text.</summary>
public sealed record UserEnvironmentVariableCheck(string Name, string ExpectedContent) : SmokeCheckDefinition
{
    public override string Kind => "userEnvironmentVariable";

    public override string Describe() => $"The user environment variable '{Name}' contains '{ExpectedContent}'.";
}

/// <summary>A check kind P3B does not recognise. It is reported as unsupported, never guessed at.</summary>
public sealed record UnrecognisedSmokeCheck(string DeclaredKind) : SmokeCheckDefinition
{
    public override string Kind => DeclaredKind;

    public override string Describe() => $"A '{DeclaredKind}' check, which P3B does not recognise.";
}

/// <summary>Rules for commands named in smoke checks.</summary>
public static partial class SmokeCheckCommands
{
    /// <summary>A bare command name (for example <c>tool</c> or <c>tool.exe</c>) or an absolute path; never a relative path.</summary>
    public static bool IsAcceptable(string? command) =>
        !string.IsNullOrWhiteSpace(command)
        && command.IndexOfAny(['"', '%', ';', '|', '&', '<', '>']) < 0
        && !command.Any(char.IsControl)
        && (System.IO.Path.IsPathFullyQualified(command) || BareName().IsMatch(command));

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}\z")]
    private static partial Regex BareName();
}
