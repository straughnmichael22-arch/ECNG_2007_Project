using P3B.Remediation.WinForms.Composition;

namespace P3B.Remediation.WinForms;

internal static class Program
{
    /// <summary>
    /// Optional arguments: <c>--manifest &lt;path&gt;</c> (default: P3A's eda-tool-manifest.json next to
    /// the application), <c>--audit &lt;path&gt;</c> to load an audit-results file instead of running
    /// P3A's audit at start-up, and <c>--data-folder &lt;path&gt;</c> to keep the journal and results
    /// somewhere other than %LOCALAPPDATA%\P3B.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var manifestPath = ValueAfter(args, "--manifest") ?? AppComposition.DefaultManifestPath();
        var auditPath = ValueAfter(args, "--audit");
        var dataFolder = ValueAfter(args, "--data-folder");
        var paths = dataFolder is null ? AppPaths.Default : AppPaths.Under(Path.GetFullPath(dataFolder));

        Application.Run(new MainForm(
            approvals => AppComposition.CreateController(approvals, paths),
            manifestPath,
            auditPath));
    }

    internal static string? ValueAfter(string[] args, string option)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
