using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>Scripted reversal handler double; reverses nothing.</summary>
internal sealed class FakeReversalHandler : IReversalHandler
{
    private int _calls;

    public ReversalAttempt Result { get; init; } = new(ReversalAttemptStatus.Reversed, "TEST reversed");

    public bool Throw { get; init; }

    public bool WaitForCancellation { get; init; }

    public int Calls => Volatile.Read(ref _calls);

    public bool CanHandle(ReversalOperation operation) => true;

    public async Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Throw)
        {
            throw new InvalidOperationException("TEST handler exception");
        }

        return Result;
    }
}
