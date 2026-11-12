using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Results;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>In-memory smoke-test double. Runs no commands.</summary>
internal sealed class FakeSmokeTestRunner : ISmokeTestRunner
{
    private int _calls;

    public SmokeTestOutcome Outcome { get; init; } = SmokeTestOutcome.Passed;

    public bool Throw { get; init; }

    public int Calls => Volatile.Read(ref _calls);

    public Task<SmokeTestResult> RunAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        if (Throw)
        {
            throw new InvalidOperationException("TEST smoke-test exception");
        }

        return Task.FromResult(new SmokeTestResult
        {
            PreviewId = preview.PreviewId,
            Tool = preview.Tool,
            Outcome = Outcome,
            Message = $"TEST smoke test {Outcome}",
            CompletedAt = DateTimeOffset.UtcNow,
        });
    }
}
