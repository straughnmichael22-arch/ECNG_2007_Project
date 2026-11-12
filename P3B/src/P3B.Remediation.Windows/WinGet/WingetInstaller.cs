using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.WinGet;

public sealed record WingetOptions(TimeSpan QueryTimeout, TimeSpan InstallTimeout, TimeSpan UninstallTimeout)
{
    public static WingetOptions Default { get; } =
        new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(15));
}

/// <summary>
/// Runs WinGet for <see cref="InstallationService"/>: availability, installed-state queries,
/// per-user installs and uninstalls. It only runs commands from <see cref="WingetCommands"/>;
/// safety decisions are made by <see cref="InstallationService"/>.
/// </summary>
public sealed class WingetInstaller : IInstaller
{
    private const string AgreementsMessage =
        "WinGet needs source agreements to be accepted before it can run. P3B does not accept them on your behalf: "
        + "open a terminal, run 'winget list', review the agreements, then try again.";

    private readonly IProcessRunner _runner;
    private readonly WingetOptions _options;

    public WingetInstaller(IProcessRunner runner, WingetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _options = options ?? WingetOptions.Default;
    }

    public RemediationMethod Method => RemediationMethod.WinGet;

    public async Task<InstallerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(WingetCommands.Version(), _options.QueryTimeout, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            { Completion: ProcessCompletion.FailedToStart } => new InstallerAvailability(
                false,
                $"WinGet was not found on this computer ({result.StartError}). Install or update 'App Installer' from the Microsoft Store, then try again."),
            { Completion: ProcessCompletion.TimedOut } => new InstallerAvailability(false, "WinGet did not respond in time."),
            { ExitCode: not 0 } => new InstallerAvailability(
                false,
                $"WinGet could not run (exit code {WingetCommands.FormatExitCode(result.ExitCode ?? -1)})."),
            _ => new InstallerAvailability(true, $"WinGet {result.StandardOutput.Trim()} is available."),
        };
    }

    public Task<InstalledState> GetInstalledStateAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetPackageStateAsync(target.Source, target.Version, cancellationToken);
    }

    public async Task<InstalledState> GetPackageStateAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        if (!WingetCommands.IsSafeToken(packageId) || !WingetCommands.IsSafeToken(version))
        {
            return InstalledState.Unknown("The package identifier or version contains characters P3B will not pass to WinGet.");
        }

        var result = await RunAsync(WingetCommands.List(packageId), _options.QueryTimeout, cancellationToken).ConfigureAwait(false);
        switch (result)
        {
            case { Completion: ProcessCompletion.FailedToStart }:
                return InstalledState.Unknown($"WinGet could not be started: {result.StartError}");
            case { Completion: ProcessCompletion.TimedOut }:
                return InstalledState.Unknown("WinGet did not finish listing installed packages in time.");
            case { ExitCode: WingetCommands.NoInstalledPackageFound }:
                return InstalledState.NotInstalled($"WinGet reports no installed package '{packageId}'.");
            case { ExitCode: WingetCommands.SourceAgreementsNotAccepted }:
                return InstalledState.Unknown(AgreementsMessage);
            case { ExitCode: not 0 }:
                return InstalledState.Unknown(
                    $"'winget list' failed with exit code {WingetCommands.FormatExitCode(result.ExitCode ?? -1)}: {Summarise(result)}");
        }

        var match = WingetListParser.FindPackage(result.StandardOutput, packageId);
        if (match is null)
        {
            return InstalledState.Unknown($"WinGet's list output did not contain '{packageId}', so the installed version could not be confirmed.");
        }

        return string.Equals(match.Version, version, StringComparison.Ordinal)
            ? InstalledState.TargetVersion(version, match.Line)
            : InstalledState.OtherVersion(match.Version, match.Line);
    }

    public async Task<InstallAttempt> InstallAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!WingetCommands.IsSafeToken(target.Source) || !WingetCommands.IsSafeToken(target.Version))
        {
            return new InstallAttempt(InstallAttemptStatus.Rejected, null, "The package identifier or version contains characters P3B will not pass to WinGet.");
        }

        var result = await RunAsync(WingetCommands.Install(target.Source, target.Version), _options.InstallTimeout, cancellationToken)
            .ConfigureAwait(false);
        return ToAttempt(result, "installation");
    }

    public async Task<InstallAttempt> UninstallPackageAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        if (!WingetCommands.IsSafeToken(packageId) || !WingetCommands.IsSafeToken(version))
        {
            return new InstallAttempt(InstallAttemptStatus.Rejected, null, "The package identifier or version contains characters P3B will not pass to WinGet.");
        }

        var result = await RunAsync(WingetCommands.Uninstall(packageId, version), _options.UninstallTimeout, cancellationToken)
            .ConfigureAwait(false);
        return ToAttempt(result, "uninstallation");
    }

    public IReadOnlyList<string> DescribeInstall(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return
        [
            $"Run WinGet to install package '{target.Source}' version {target.Version} from the 'winget' source, for the current user only.",
            "Accept the package's licence agreement on your behalf, which a silent installation requires.",
            "No machine-wide installation is attempted: if the package has no per-user installer, the installation fails instead.",
            $"Stop the installer if it has not finished after {_options.InstallTimeout.TotalMinutes:0} minutes.",
        ];
    }

    public ReversalOperation CreateUninstallReversal(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new WingetUninstallReversal(target.Source, target.Version);
    }

    /// <summary>The last meaningful lines of output, without progress-bar noise.</summary>
    public static string Summarise(ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var lines = $"{result.StandardOutput}\n{result.StandardError}"
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 2 && !line.Contains('█') && !line.Contains('▒'))
            .TakeLast(8)
            .ToArray();
        var text = lines.Length == 0 ? "(no output)" : string.Join(" | ", lines);
        return text.Length <= 800 ? text : string.Concat(text.AsSpan(0, 800), "…");
    }

    private static InstallAttempt ToAttempt(ProcessResult result, string operation) => result switch
    {
        { Completion: ProcessCompletion.FailedToStart } =>
            new InstallAttempt(InstallAttemptStatus.FailedToStart, null, $"WinGet could not be started: {result.StartError}"),
        { Completion: ProcessCompletion.TimedOut } =>
            new InstallAttempt(InstallAttemptStatus.TimedOut, null, $"The {operation} did not finish in time and was stopped. {Summarise(result)}"),
        { ExitCode: 0 } => new InstallAttempt(InstallAttemptStatus.Completed, 0, Summarise(result)),
        { ExitCode: WingetCommands.SourceAgreementsNotAccepted } =>
            new InstallAttempt(InstallAttemptStatus.Failed, result.ExitCode, AgreementsMessage),
        _ => new InstallAttempt(InstallAttemptStatus.Failed, result.ExitCode, Summarise(result)),
    };

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        _runner.RunAsync(new ProcessRequest(WingetCommands.Executable, arguments, timeout), cancellationToken);
}
