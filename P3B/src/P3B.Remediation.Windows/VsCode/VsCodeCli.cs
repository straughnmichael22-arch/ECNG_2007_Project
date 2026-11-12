using System.Text.RegularExpressions;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.VsCode;

/// <summary>Read-only access to small text files, so tests can supply a fake <c>code.cmd</c>.</summary>
public interface ITextFileReader
{
    bool Exists(string path);

    /// <summary>Returns null if the file cannot be read.</summary>
    string? ReadAllText(string path);
}

public sealed class WindowsTextFileReader : ITextFileReader
{
    public bool Exists(string path) => File.Exists(path);

    public string? ReadAllText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// VS Code's command-line interface, run without <c>cmd.exe</c>: <c>Code.exe</c> with
/// <c>ELECTRON_RUN_AS_NODE=1</c> and the <c>cli.js</c> script, which is exactly what VS Code's own
/// <c>bin\code.cmd</c> does.
/// </summary>
public sealed record VsCodeCli(string Executable, string CliScript)
{
    /// <summary>The variables <c>code.cmd</c> sets, applied to this one process only.</summary>
    public static IReadOnlyDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>
    {
        ["ELECTRON_RUN_AS_NODE"] = "1",
        ["VSCODE_DEV"] = null,
    };

    public ProcessRequest Request(TimeSpan timeout, params string[] arguments) =>
        new(Executable, [CliScript, .. arguments], timeout, Environment);
}

public sealed record VsCodeCliLocation(VsCodeCli? Cli, string Description);

/// <summary>
/// Finds VS Code's command line. <c>code.cmd</c> is located with the normal command locator and
/// <b>read as text, never run</b>: P3B accepts it only if it has the layout VS Code ships, then
/// takes the <c>Code.exe</c> and <c>cli.js</c> paths from it. Both must exist inside the VS Code
/// folder. Anything else is refused, so a changed or unexpected script is never guessed at.
/// </summary>
public sealed partial class VsCodeCliLocator
{
    private readonly ICommandLocator _commands;
    private readonly ITextFileReader _files;

    public VsCodeCliLocator(ICommandLocator commands, ITextFileReader files)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(files);
        _commands = commands;
        _files = files;
    }

    public VsCodeCliLocation Locate()
    {
        var script = _commands.Locate("code.cmd");
        if (script is null)
        {
            return new VsCodeCliLocation(null, "VS Code's 'code' command was not found on PATH. Install VS Code (or add it to PATH), then try again.");
        }

        var text = _files.ReadAllText(script);
        if (text is null)
        {
            return new VsCodeCliLocation(null, $"VS Code's command script {script} could not be read.");
        }

        var launch = LaunchLine().Match(text);
        if (!ElectronAsNode().IsMatch(text) || !launch.Success)
        {
            return new VsCodeCliLocation(
                null,
                $"VS Code's command script {script} is not in the layout P3B recognises, so P3B will not use it. Install the extension from VS Code instead.");
        }

        var binFolder = Path.GetDirectoryName(script)!;
        var installFolder = Path.GetFullPath(Path.Combine(binFolder, ".."));
        var executable = Path.GetFullPath(Path.Combine(binFolder, launch.Groups["exe"].Value));
        var cli = Path.GetFullPath(Path.Combine(binFolder, launch.Groups["cli"].Value));

        if (!IsInside(executable, installFolder) || !IsInside(cli, installFolder)
            || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !_files.Exists(executable) || !_files.Exists(cli))
        {
            return new VsCodeCliLocation(null, $"The files named in {script} were not found inside the VS Code folder, so P3B will not use them.");
        }

        return new VsCodeCliLocation(new VsCodeCli(executable, cli), $"VS Code command line found at {executable}.");
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*set\s+ELECTRON_RUN_AS_NODE=1\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ElectronAsNode();

    // "%~dp0..\Code.exe" "%~dp0..\<folder>\resources\app\out\cli.js" %*
    [GeneratedRegex(@"^\s*""%~dp0(?<exe>[^""%\r\n]+\.exe)""\s+""%~dp0(?<cli>[^""%\r\n]+\\cli\.js)""\s+%\*\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex LaunchLine();
}
