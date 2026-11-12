using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Execution;

public enum ExecutionStatus
{
    Succeeded,
    Failed,

    /// <summary>Stopped part-way; any partial changes are still reported.</summary>
    Cancelled,
}

/// <summary>
/// What an <see cref="IRemediationExecutor"/> reports after running an approved action.
/// </summary>
public sealed record ExecutionReport
{
    private ExecutionReport(
        ExecutionStatus status,
        string message,
        string? priorState,
        IEnumerable<string> observedChanges,
        ReversalInfo? reversal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Status = status;
        Message = message;
        PriorState = priorState;
        ObservedChanges = Array.AsReadOnly(observedChanges.ToArray());
        Reversal = reversal;
    }

    public ExecutionStatus Status { get; }

    public bool Succeeded => Status == ExecutionStatus.Succeeded;

    public string Message { get; }

    /// <summary>The relevant state observed before the action changed anything (P3B-DR-01).</summary>
    public string? PriorState { get; }

    public IReadOnlyList<string> ObservedChanges { get; }

    public ReversalInfo? Reversal { get; }

    /// <summary>A successful action must say how it can (or cannot) be undone (3B-DC-003).</summary>
    public static ExecutionReport Success(
        string message,
        string? priorState,
        IEnumerable<string> observedChanges,
        ReversalInfo reversal)
    {
        ArgumentNullException.ThrowIfNull(observedChanges);
        ArgumentNullException.ThrowIfNull(reversal);
        return new ExecutionReport(ExecutionStatus.Succeeded, message, priorState, observedChanges, reversal);
    }

    /// <summary>A failed action still reports prior state, any partial changes and how to undo them.</summary>
    public static ExecutionReport Failure(
        string message,
        string? priorState = null,
        IEnumerable<string>? observedChanges = null,
        ReversalInfo? reversal = null) =>
        new(ExecutionStatus.Failed, message, priorState, observedChanges ?? Array.Empty<string>(), reversal);

    /// <summary>An interrupted action reports what it found afterwards, so partial changes are not lost.</summary>
    public static ExecutionReport Cancelled(
        string message,
        string? priorState = null,
        IEnumerable<string>? observedChanges = null,
        ReversalInfo? reversal = null) =>
        new(ExecutionStatus.Cancelled, message, priorState, observedChanges ?? Array.Empty<string>(), reversal);
}
