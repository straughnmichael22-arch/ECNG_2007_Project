using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.Tests.Fakes;

/// <summary>
/// Scripted process runner. It never starts a process: it returns queued results per
/// subcommand (by default the first argument) and records every request. Unscripted commands fail the test.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<ProcessResult>>>> _scripts = new(StringComparer.Ordinal);
    private readonly List<ProcessRequest> _requests = [];
    private readonly object _sync = new();

    /// <summary>Which argument selects the script. Default: the first (the WinGet subcommand).</summary>
    public int KeyArgument { get; init; }

    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public static ProcessResult Exited(int exitCode, string output = "", string error = "") =>
        new(ProcessCompletion.Exited, exitCode, output, error, TimeSpan.FromMilliseconds(5), null);

    public static ProcessResult TimedOut(string output = "") =>
        new(ProcessCompletion.TimedOut, null, output, string.Empty, TimeSpan.FromMinutes(1), null);

    public static ProcessResult NotFound() => ProcessResult.NotStarted("TEST: the system cannot find the file specified");

    /// <summary>Queues results for a subcommand; the last one repeats.</summary>
    public FakeProcessRunner Script(string subcommand, params ProcessResult[] results)
    {
        foreach (var result in results)
        {
            Enqueue(subcommand, _ => Task.FromResult(result));
        }

        return this;
    }

    /// <summary>Queues a run that waits until it is cancelled.</summary>
    public FakeProcessRunner ScriptHangUntilCancelled(string subcommand)
    {
        Enqueue(subcommand, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable.");
        });
        return this;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<ProcessResult>> next;
        lock (_sync)
        {
            _requests.Add(request);
            var key = request.Arguments.Count > KeyArgument ? request.Arguments[KeyArgument] : string.Empty;
            if (!_scripts.TryGetValue(key, out var queue) || queue.Count == 0)
            {
                throw new InvalidOperationException($"Unscripted process: {request.FileName} {string.Join(' ', request.Arguments)}");
            }

            next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }

        return next(cancellationToken);
    }

    public IReadOnlyList<ProcessRequest> RequestsFor(string subcommand) =>
        Requests.Where(r => r.Arguments.Count > KeyArgument && r.Arguments[KeyArgument] == subcommand).ToArray();

    private void Enqueue(string subcommand, Func<CancellationToken, Task<ProcessResult>> step)
    {
        lock (_sync)
        {
            if (!_scripts.TryGetValue(subcommand, out var queue))
            {
                queue = new Queue<Func<CancellationToken, Task<ProcessResult>>>();
                _scripts[subcommand] = queue;
            }

            queue.Enqueue(step);
        }
    }
}
