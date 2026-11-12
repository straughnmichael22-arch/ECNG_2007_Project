using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Verification;

/// <summary>Runs one kind of <see cref="SmokeCheckDefinition"/>.</summary>
public interface ISmokeCheckHandler
{
    bool CanHandle(SmokeCheckDefinition check);

    /// <summary>
    /// Must explain any result other than <see cref="SmokeCheckStatus.Passed"/>. Throws
    /// <see cref="OperationCanceledException"/> when cancelled or timed out.
    /// </summary>
    Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken);
}

/// <summary>Read-only file-system queries used by smoke checks.</summary>
public interface IFileProbe
{
    /// <summary>Expands references such as <c>%LOCALAPPDATA%</c>; unknown references are left unchanged.</summary>
    string ExpandEnvironmentVariables(string path);

    bool FileExists(string path);
}

/// <summary>Finds a command the way a newly opened terminal would.</summary>
public interface ICommandLocator
{
    /// <summary>
    /// Returns the full path of a bare command name found on the current user and system PATH
    /// (read fresh, not from P3B's own possibly outdated environment), or of an existing absolute
    /// path. Returns null if not found. Never searches the current directory.
    /// </summary>
    string? Locate(string command);
}
