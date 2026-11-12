using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.VsCode;

public sealed record VsCodeOptions(TimeSpan QueryTimeout, TimeSpan InstallTimeout)
{
    public static VsCodeOptions Default { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
}

/// <summary>The only VS Code command lines P3B runs. None of them uses <c>--force</c>.</summary>
public static class VsCodeExtensionCommands
{
    public static string[] Version() => ["--version"];

    public static string[] List() => ["--list-extensions", "--show-versions"];

    public static string[] Install(string extensionId, string version) => ["--install-extension", $"{extensionId}@{version}"];

    public static string[] Uninstall(string extensionId) => ["--uninstall-extension", extensionId];

    /// <summary>
    /// Finds <paramref name="extensionId"/> in <c>--list-extensions --show-versions</c> output
    /// (one <c>publisher.name@version</c> per line). Identifiers are not case-sensitive.
    /// </summary>
    public static string? FindVersion(string output, string extensionId)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = line.LastIndexOf('@');
            if (at > 0 && string.Equals(line[..at], extensionId, StringComparison.OrdinalIgnoreCase))
            {
                return line[(at + 1)..];
            }
        }

        return null;
    }
}

/// <summary>
/// Installs exactly one approved version of a VS Code extension for the current user, through
/// VS Code's own command line (<see cref="VsCodeCli"/>, run without a shell). Safety decisions are
/// made by <see cref="InstallationService"/>; this class only runs <see cref="VsCodeExtensionCommands"/>.
/// </summary>
public sealed class VsCodeExtensionInstaller : IInstaller
{
    private const string UnsafeInput = "The extension identifier or version contains characters P3B will not pass to VS Code.";

    private readonly VsCodeCliLocator _locator;
    private readonly IProcessRunner _runner;
    private readonly VsCodeOptions _options;

    public VsCodeExtensionInstaller(VsCodeCliLocator locator, IProcessRunner runner, VsCodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        _locator = locator;
        _runner = runner;
        _options = options ?? VsCodeOptions.Default;
    }

    public RemediationMethod Method => RemediationMethod.VsCodeExtension;

    public async Task<InstallerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var location = _locator.Locate();
        if (location.Cli is null)
        {
            return new InstallerAvailability(false, location.Description);
        }

        var result = await _runner.RunAsync(location.Cli.Request(_options.QueryTimeout, VsCodeExtensionCommands.Version()), cancellationToken)
            .ConfigureAwait(false);
        return result switch
        {
            { Completion: ProcessCompletion.FailedToStart } => new InstallerAvailability(false, $"VS Code could not be started: {result.StartError}"),
            { Completion: ProcessCompletion.TimedOut } => new InstallerAvailability(false, "VS Code did not respond in time."),
            { ExitCode: not 0 } => new InstallerAvailability(false, $"VS Code's command line failed with exit code {result.ExitCode}."),
            _ => new InstallerAvailability(true, $"VS Code {FirstLine(result.StandardOutput)} is available."),
        };
    }

    public Task<InstalledState> GetInstalledStateAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetExtensionStateAsync(target.Source, target.Version, cancellationToken);
    }

    public async Task<InstalledState> GetExtensionStateAsync(string extensionId, string version, CancellationToken cancellationToken)
    {
        if (!VsCodeExtensionId.IsValid(extensionId) || !VsCodeExtensionId.IsValidVersion(version))
        {
            return InstalledState.Unknown(UnsafeInput);
        }

        var location = _locator.Locate();
        if (location.Cli is null)
        {
            return InstalledState.Unknown(location.Description);
        }

        var result = await _runner.RunAsync(location.Cli.Request(_options.QueryTimeout, VsCodeExtensionCommands.List()), cancellationToken)
            .ConfigureAwait(false);
        switch (result)
        {
            case { Completion: ProcessCompletion.FailedToStart }:
                return InstalledState.Unknown($"VS Code could not be started: {result.StartError}");
            case { Completion: ProcessCompletion.TimedOut }:
                return InstalledState.Unknown("VS Code did not finish listing extensions in time.");
            case { ExitCode: not 0 }:
                return InstalledState.Unknown($"Listing extensions failed with exit code {result.ExitCode}: {Summarise(result)}");
        }

        var installed = VsCodeExtensionCommands.FindVersion(result.StandardOutput, extensionId);
        if (installed is null)
        {
            return InstalledState.NotInstalled($"VS Code lists no extension '{extensionId}'.");
        }

        var evidence = $"VS Code lists {extensionId}@{installed}";
        return string.Equals(installed, version, StringComparison.Ordinal)
            ? InstalledState.TargetVersion(version, evidence)
            : InstalledState.OtherVersion(installed, evidence);
    }

    public Task<InstallAttempt> InstallAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return RunChangeAsync(target.Source, target.Version, VsCodeExtensionCommands.Install(target.Source, target.Version), "installation", cancellationToken);
    }

    public Task<InstallAttempt> UninstallExtensionAsync(string extensionId, string version, CancellationToken cancellationToken) =>
        RunChangeAsync(extensionId, version, VsCodeExtensionCommands.Uninstall(extensionId), "uninstallation", cancellationToken);

    public IReadOnlyList<string> DescribeInstall(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return
        [
            $"Use VS Code's own command line to install extension '{target.Source}' version {target.Version} for the current user (your VS Code profile only).",
            "VS Code is started without a command shell; nothing else on the computer is changed.",
            $"Stop VS Code's command line if it has not finished after {_options.InstallTimeout.TotalMinutes:0} minutes.",
        ];
    }

    public ReversalOperation CreateUninstallReversal(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new VsCodeExtensionUninstallReversal(target.Source, target.Version);
    }

    private async Task<InstallAttempt> RunChangeAsync(
        string extensionId,
        string version,
        string[] arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!VsCodeExtensionId.IsValid(extensionId) || !VsCodeExtensionId.IsValidVersion(version))
        {
            return new InstallAttempt(InstallAttemptStatus.Rejected, null, UnsafeInput);
        }

        var location = _locator.Locate();
        if (location.Cli is null)
        {
            return new InstallAttempt(InstallAttemptStatus.FailedToStart, null, location.Description);
        }

        var result = await _runner.RunAsync(location.Cli.Request(_options.InstallTimeout, arguments), cancellationToken).ConfigureAwait(false);
        return result switch
        {
            { Completion: ProcessCompletion.FailedToStart } =>
                new InstallAttempt(InstallAttemptStatus.FailedToStart, null, $"VS Code could not be started: {result.StartError}"),
            { Completion: ProcessCompletion.TimedOut } =>
                new InstallAttempt(InstallAttemptStatus.TimedOut, null, $"The {operation} did not finish in time and was stopped. {Summarise(result)}"),
            { ExitCode: 0 } => new InstallAttempt(InstallAttemptStatus.Completed, 0, Summarise(result)),
            _ => new InstallAttempt(InstallAttemptStatus.Failed, result.ExitCode, Summarise(result)),
        };
    }

    private static string FirstLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "(version not reported)";

    private static string Summarise(ProcessResult result)
    {
        var lines = $"{result.StandardOutput}\n{result.StandardError}"
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(6)
            .ToArray();
        return lines.Length == 0 ? "(no output)" : string.Join(" | ", lines);
    }
}

/// <summary>
/// Reverses a P3B extension installation by uninstalling exactly the recorded extension. It
/// refuses if another version is now installed, and verifies the extension is gone before
/// reporting success.
/// </summary>
public sealed class VsCodeExtensionUninstallHandler : IReversalHandler
{
    private readonly VsCodeExtensionInstaller _installer;

    public VsCodeExtensionUninstallHandler(VsCodeExtensionInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(installer);
        _installer = installer;
    }

    public bool CanHandle(ReversalOperation operation) => operation is VsCodeExtensionUninstallReversal;

    public async Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken)
    {
        if (operation is not VsCodeExtensionUninstallReversal uninstall)
        {
            throw new ArgumentException("Unsupported reversal operation.", nameof(operation));
        }

        var (id, version) = (uninstall.ExtensionId, uninstall.Version);
        var before = await _installer.GetExtensionStateAsync(id, version, cancellationToken).ConfigureAwait(false);

        switch (before.Kind)
        {
            case InstalledStateKind.NotInstalled:
                return new ReversalAttempt(ReversalAttemptStatus.Reversed, $"{id} is no longer installed, so nothing needed to be removed.");
            case InstalledStateKind.OtherVersionInstalled:
                return new ReversalAttempt(
                    ReversalAttemptStatus.Conflict,
                    $"{id} is now at version {before.DetectedVersion ?? "(unknown)"}, not {version} as installed by P3B, so it was not uninstalled.");
            case InstalledStateKind.Unknown:
                return new ReversalAttempt(ReversalAttemptStatus.Failed, $"P3B could not confirm which extensions are installed, so nothing was changed: {before.Evidence}");
        }

        var attempt = await _installer.UninstallExtensionAsync(id, version, cancellationToken).ConfigureAwait(false);
        var after = await _installer.GetExtensionStateAsync(id, version, CancellationToken.None).ConfigureAwait(false);

        return after.Kind == InstalledStateKind.NotInstalled
            ? new ReversalAttempt(ReversalAttemptStatus.Reversed, $"Uninstalled {id} {version} and verified that VS Code no longer lists it.")
            : new ReversalAttempt(
                ReversalAttemptStatus.Failed,
                $"The uninstallation did not complete ({attempt.Describe()}). Afterwards, {id} is {after.Describe()}.");
    }
}
