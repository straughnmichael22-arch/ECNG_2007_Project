using P3B.Integration.P3A;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Configuration;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.WinForms.Presentation;
using P3B.Remediation.Windows.Processes;
using P3B.Remediation.Windows.UserSettings;
using P3B.Remediation.Windows.Verification;
using P3B.Remediation.Windows.VsCode;
using P3B.Remediation.Windows.WinGet;

namespace P3B.Remediation.WinForms.Composition;

/// <summary>Where P3B keeps its files.</summary>
public sealed record AppPaths(string JournalPath, string ResultsPath)
{
    /// <summary>
    /// %LOCALAPPDATA%\P3B. The journal location is still a group decision (docs/BLOCKERS.md #7);
    /// change it here.
    /// </summary>
    public static AppPaths Default { get; } = Under(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "P3B"));

    public static AppPaths Under(string folder) => new(
        Path.Combine(folder, "reversal-journal.json"),
        Path.Combine(folder, "latest-remediation-results.json"));
}

/// <summary>Builds the real services for the desktop application.</summary>
public static class AppComposition
{
    public static DashboardController CreateController(IApprovalProvider userApprovals, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(userApprovals);
        ArgumentNullException.ThrowIfNull(paths);

        var gate = new OperationGate();
        var manifest = new SwitchableManifestSource();
        var processRunner = new ProcessRunner();
        var winget = new WingetInstaller(processRunner);
        var userEnvironment = new UserEnvironment();
        var commandLocator = new WindowsCommandLocator();
        var journal = new JsonFileReversalJournal(paths.JournalPath);
        var approvals = new ProgressApprovalProvider(userApprovals);

        var vscode = new VsCodeExtensionInstaller(new VsCodeCliLocator(commandLocator, new WindowsTextFileReader()), processRunner);

        IRemediationExecutor[] executors = [new InstallationService([winget, vscode])];
        var smokeTests = new SmokeTestService(
        [
            new InstalledVersionCheckHandler([winget, vscode]),
            new FileExistsCheckHandler(new WindowsFileProbe()),
            new CommandOnPathCheckHandler(commandLocator),
            new UserEnvironmentVariableCheckHandler(userEnvironment),
            new VersionCommandCheckHandler(commandLocator, processRunner),
        ]);

        var remediation = new RemediationService(manifest, executors, smokeTests, approvals, journal, gate);
        var rollback = new RollbackService(
            journal,
            [new WingetUninstallHandler(winget), new VsCodeExtensionUninstallHandler(vscode), new UserEnvironmentReversalHandler(userEnvironment)],
            gate);

        // P3A's own audit, run against whichever manifest is open, supplies findings and every re-audit.
        var p3aAudit = new P3AAuditAdapter(() => manifest.ManifestPath);

        return new DashboardController(
            manifest, remediation, executors, rollback, journal, approvals, gate, paths.ResultsPath, readinessAuditor: p3aAudit);
    }

    /// <summary>P3A's manifest copied next to the application, or null if it is not there.</summary>
    public static string? DefaultManifestPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, EDAEnvironmentManager.Audit.EdaManifestLoader.FileName);
        return File.Exists(path) ? path : null;
    }
}
