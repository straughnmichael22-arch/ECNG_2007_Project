namespace P3B.Remediation.Windows.Processes;

/// <summary>
/// A process to run. Arguments are passed individually and never through a shell, so no
/// argument can be interpreted as a shell command. <c>Environment</c> sets (or, with a
/// null value, removes) environment variables for this process only; P3B's own environment and the
/// user's settings are never changed.
/// </summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string?>? Environment = null);

public enum ProcessCompletion
{
    Exited,
    TimedOut,
    FailedToStart,
}

/// <summary>Everything observed about one process run.</summary>
public sealed record ProcessResult(
    ProcessCompletion Completion,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string? StartError)
{
    public static ProcessResult NotStarted(string error) =>
        new(ProcessCompletion.FailedToStart, null, string.Empty, string.Empty, TimeSpan.Zero, error);
}

public interface IProcessRunner
{
    /// <summary>
    /// Runs the process to completion or timeout. On cancellation the process tree is stopped
    /// and <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
