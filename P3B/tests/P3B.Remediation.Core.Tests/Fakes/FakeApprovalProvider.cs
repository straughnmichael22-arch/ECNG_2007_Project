using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>Stands in for the user. Approves by default; tests override the decision.</summary>
internal sealed class FakeApprovalProvider : IApprovalProvider
{
    private int _requests;

    public Func<ActionPreview, ApprovalDecision?> Decide { get; init; } = ApprovalDecision.Approve;

    public bool WaitForCancellation { get; init; }

    public int Requests => Volatile.Read(ref _requests);

    public ActionPreview? LastPreview { get; private set; }

    public async Task<ApprovalDecision> RequestApprovalAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        LastPreview = preview;

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        // Deliberately allows null so tests can check that a missing decision is a rejection.
        return Decide(preview)!;
    }
}
