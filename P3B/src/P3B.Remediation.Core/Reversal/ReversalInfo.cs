using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Reversal;

/// <summary>
/// What is needed to undo an executed action (3B-DC-003, P3B-DR-01), and how reliably it can
/// be undone. Only <see cref="ReversalKind.Automatic"/> changes are reversed by P3B; others are
/// explained to the user rather than promised.
/// </summary>
public sealed record ReversalInfo
{
    private ReversalInfo(
        ActionPreview preview,
        ReversalKind kind,
        string method,
        string? priorState,
        IEnumerable<string> changesToReverse,
        ReversalOperation? operation)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(changesToReverse);

        PreviewId = preview.PreviewId;
        Tool = preview.Tool;
        Issue = preview.Request.Issue;
        Kind = kind;
        Method = method;
        PriorState = priorState;
        ChangesToReverse = Array.AsReadOnly(changesToReverse.ToArray());
        Operation = operation;
    }

    public Guid PreviewId { get; }

    public ToolReference Tool { get; }

    public IssueReference Issue { get; }

    public ReversalKind Kind { get; }

    /// <summary>User-facing explanation of how the change is (or can be) reversed.</summary>
    public string Method { get; }

    /// <summary>The relevant state observed before the action ran, if any.</summary>
    public string? PriorState { get; }

    public IReadOnlyList<string> ChangesToReverse { get; }

    /// <summary>Set only for <see cref="ReversalKind.Automatic"/>.</summary>
    public ReversalOperation? Operation { get; }

    public static ReversalInfo Automatic(
        ActionPreview preview,
        string method,
        string? priorState,
        IEnumerable<string> changesToReverse,
        ReversalOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new ReversalInfo(preview, ReversalKind.Automatic, method, priorState, changesToReverse, operation);
    }

    public static ReversalInfo ManualOnly(
        ActionPreview preview,
        string method,
        string? priorState,
        IEnumerable<string> changesToReverse) =>
        new(preview, ReversalKind.ManualOnly, method, priorState, changesToReverse, operation: null);

    public static ReversalInfo NothingToReverse(ActionPreview preview, string? priorState) =>
        new(preview, ReversalKind.NothingToReverse, "Nothing was changed, so there is nothing to reverse.", priorState, [], operation: null);
}
