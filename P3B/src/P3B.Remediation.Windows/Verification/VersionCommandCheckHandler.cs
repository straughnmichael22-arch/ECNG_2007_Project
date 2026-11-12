using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.Verification;

/// <summary>
/// Launches the tool with the manifest's version arguments and checks that it exits successfully
/// and reports the approved version. Only <c>.exe</c> files are launched, directly and without a
/// shell; scripts such as <c>.cmd</c> are never run.
/// </summary>
public sealed class VersionCommandCheckHandler : ISmokeCheckHandler
{
    private readonly ICommandLocator _locator;
    private readonly IProcessRunner _runner;

    public VersionCommandCheckHandler(ICommandLocator locator, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        _locator = locator;
        _runner = runner;
    }

    public bool CanHandle(SmokeCheckDefinition check) => check is VersionCommandCheck;

    public async Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var command = (VersionCommandCheck)check;

        if (!SmokeCheckCommands.IsAcceptable(command.Command))
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Unsupported, $"'{command.Command}' is not an acceptable command, so it was not run.");
        }

        var path = _locator.Locate(command.Command);
        if (path is null)
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"'{command.Command}' could not be found, so it could not be launched.");
        }

        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Unsupported, $"{path} is not an .exe file; P3B only launches executables directly, never scripts.");
        }

        var result = await _runner.RunAsync(new ProcessRequest(path, command.Arguments, command.Timeout), cancellationToken).ConfigureAwait(false);
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        var firstLine = FirstLine(output);

        return result switch
        {
            { Completion: ProcessCompletion.FailedToStart } =>
                SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"{path} could not be started: {result.StartError}"),
            { Completion: ProcessCompletion.TimedOut } =>
                SmokeCheckResult.For(check, SmokeCheckStatus.TimedOut, $"{path} did not respond within {command.Timeout.TotalSeconds:0} seconds and was stopped."),
            { ExitCode: not 0 } =>
                SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"{path} exited with code {result.ExitCode}: {firstLine}"),
            _ when VersionText.ContainsVersion(output, action.TargetVersion) =>
                SmokeCheckResult.For(check, SmokeCheckStatus.Passed, $"{path} reported version {action.TargetVersion}: {firstLine}"),
            _ =>
                SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"{path} responded, but its output does not show the approved version {action.TargetVersion}: {firstLine}"),
        };
    }

    private static string FirstLine(string output)
    {
        var line = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return line is null ? "(no output)" : line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 200), "…");
    }
}
