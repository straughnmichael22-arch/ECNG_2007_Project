using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Installation;

/// <summary>Exactly what to install: the manifest source and the approved target version.</summary>
public sealed record InstallTarget(ToolReference Tool, string ToolName, string Source, string Version)
{
    public static InstallTarget From(RemediationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Source))
        {
            throw new ArgumentException("The action has no installation source.", nameof(action));
        }

        return new InstallTarget(action.Tool.Id, action.Tool.Name, action.Source, action.TargetVersion);
    }
}

public sealed record InstallerAvailability(bool IsAvailable, string Description);

public enum InstalledStateKind
{
    NotInstalled,
    TargetVersionInstalled,
    OtherVersionInstalled,

    /// <summary>The state could not be determined; P3B must not assume anything.</summary>
    Unknown,
}

/// <summary>Whether the target is installed, with the evidence the decision was based on.</summary>
public sealed record InstalledState(InstalledStateKind Kind, string? DetectedVersion, string Evidence)
{
    public static InstalledState NotInstalled(string evidence) => new(InstalledStateKind.NotInstalled, null, evidence);

    public static InstalledState TargetVersion(string version, string evidence) =>
        new(InstalledStateKind.TargetVersionInstalled, version, evidence);

    public static InstalledState OtherVersion(string? version, string evidence) =>
        new(InstalledStateKind.OtherVersionInstalled, version, evidence);

    public static InstalledState Unknown(string evidence) => new(InstalledStateKind.Unknown, null, evidence);

    public string Describe() => Kind switch
    {
        InstalledStateKind.NotInstalled => "not installed",
        InstalledStateKind.TargetVersionInstalled or InstalledStateKind.OtherVersionInstalled =>
            $"version {DetectedVersion ?? "(unknown)"} installed",
        _ => "in an unknown state",
    };
}

public enum InstallAttemptStatus
{
    /// <summary>The installer exited successfully. This alone is never treated as success.</summary>
    Completed,
    Failed,
    TimedOut,
    FailedToStart,

    /// <summary>P3B refused to run the installer (for example, unsafe input).</summary>
    Rejected,
}

public sealed record InstallAttempt(InstallAttemptStatus Status, int? ExitCode, string Details)
{
    public string Describe() =>
        ExitCode is int code
            ? $"{Status}, exit code 0x{code:X8}: {Details}"
            : $"{Status}: {Details}";
}
