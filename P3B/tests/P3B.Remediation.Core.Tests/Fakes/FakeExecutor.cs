using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests.Fakes;

public enum FakeExecutionMode
{
    Succeed,
    Fail,
    Throw,
    WaitForCancellation,
    WaitForRelease,
    ReportCancelledWithPartialChange,
}

/// <summary>
/// In-memory executor double. It never starts processes or touches the file system,
/// registry, network or any installed software; it only records what it was asked to do.
/// </summary>
internal sealed class FakeExecutor : IRemediationExecutor
{
    public const string SimulatedChange = "TEST change (simulated; no real software is modified)";
    public const string SimulatedPriorState = "TEST prior state";

    private int _executeCalls;
    private int _running;
    private int _maxConcurrent;

    public FakeExecutionMode Mode { get; init; } = FakeExecutionMode.Succeed;

    public Func<RemediationAction, bool> Handles { get; init; } = _ => true;

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ExecuteCalls => Volatile.Read(ref _executeCalls);

    public int MaxConcurrentExecutions => Volatile.Read(ref _maxConcurrent);

    public bool CanHandle(RemediationAction action) => Handles(action);

    public ActionPreview CreatePreview(RemediationAction action) => new(
        action,
        proposedChanges: [SimulatedChange],
        reversalMethod: "TEST reversal (simulated; nothing to undo)");

    public async Task<ExecutionReport> ExecuteAsync(
        ActionPreview preview,
        ApprovalDecision approval,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executeCalls);
        RecordConcurrency(Interlocked.Increment(ref _running));
        try
        {
            if (!approval.Approves(preview))
            {
                return ExecutionReport.Failure("FakeExecutor refused: the action was not approved.");
            }

            Started.TrySetResult();

            switch (Mode)
            {
                case FakeExecutionMode.Succeed:
                    return Success(preview);
                case FakeExecutionMode.Fail:
                    return ExecutionReport.Failure("TEST failure", SimulatedPriorState);
                case FakeExecutionMode.Throw:
                    throw new InvalidOperationException("TEST exception");
                case FakeExecutionMode.WaitForCancellation:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
                case FakeExecutionMode.WaitForRelease:
                    await Release.Task.WaitAsync(cancellationToken);
                    return Success(preview);
                case FakeExecutionMode.ReportCancelledWithPartialChange:
                    return ExecutionReport.Cancelled("TEST cancelled part-way", SimulatedPriorState, [SimulatedChange], Reversal(preview));
                default:
                    throw new InvalidOperationException($"Unknown mode {Mode}.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private static ExecutionReport Success(ActionPreview preview) => ExecutionReport.Success(
        "TEST success",
        SimulatedPriorState,
        [SimulatedChange],
        Reversal(preview));

    /// <summary>Reversal data only; no fake handler ever acts on the real environment.</summary>
    private static ReversalInfo Reversal(ActionPreview preview) => ReversalInfo.Automatic(
        preview,
        preview.ReversalMethod,
        SimulatedPriorState,
        [SimulatedChange],
        new RestoreUserEnvironmentVariableReversal("TEST_VARIABLE", null, "TEST_VALUE"));

    private void RecordConcurrency(int running)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref _maxConcurrent);
            if (running <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxConcurrent, running, observed) != observed);
    }
}
