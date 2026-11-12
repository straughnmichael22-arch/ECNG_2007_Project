using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace P3B.Remediation.Windows.Processes;

/// <summary>
/// Runs a process without a shell (<c>UseShellExecute = false</c>), passing each argument through
/// <see cref="ProcessStartInfo.ArgumentList"/>. Captures exit code, standard output and standard
/// error; enforces a timeout; stops the whole process tree on timeout or cancellation. Standard
/// input is closed so a process can never wait for interactive input.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ProcessResult.NotStarted(ex.Message);
        }

        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var stopped = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            Stop(process);
        }

        var (output, error) = await DrainAsync(standardOutput, standardError).ConfigureAwait(false);
        stopwatch.Stop();

        if (stopped && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The process was cancelled and has been stopped.", cancellationToken);
        }

        return stopped
            ? new ProcessResult(ProcessCompletion.TimedOut, null, output, error, stopwatch.Elapsed, null)
            : new ProcessResult(ProcessCompletion.Exited, process.ExitCode, output, error, stopwatch.Elapsed, null);
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(DrainTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or cannot be stopped; the result still reports the timeout.
        }
    }

    private static async Task<(string Output, string Error)> DrainAsync(Task<string> output, Task<string> error)
    {
        // A grandchild process can keep the pipes open; never wait on them indefinitely.
        var both = Task.WhenAll(output, error);
        await Task.WhenAny(both, Task.Delay(DrainTimeout)).ConfigureAwait(false);
        return (
            output.IsCompletedSuccessfully ? output.Result : string.Empty,
            error.IsCompletedSuccessfully ? error.Result : string.Empty);
    }
}
