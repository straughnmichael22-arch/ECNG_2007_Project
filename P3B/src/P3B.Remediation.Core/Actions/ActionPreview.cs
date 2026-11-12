using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// What an action will do, shown to the user before approval is requested (3B-UR-001).
/// </summary>
/// <remarks>
/// A preview without at least one proposed change and a reversal method cannot be
/// constructed, so every approved action is explicit and reversible (3B-DC-003).
/// </remarks>
public sealed record ActionPreview
{
    public ActionPreview(
        RemediationAction action,
        IEnumerable<string> proposedChanges,
        string reversalMethod,
        ReversalExpectation expectedReversal = ReversalExpectation.Reversible)
    {
        if (!Enum.IsDefined(expectedReversal))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedReversal));
        }

        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(proposedChanges);
        ArgumentException.ThrowIfNullOrWhiteSpace(reversalMethod);

        var changes = proposedChanges.ToArray();
        if (changes.Length == 0 || changes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A preview must list at least one proposed change, and none may be blank.",
                nameof(proposedChanges));
        }

        Action = action;
        ProposedChanges = Array.AsReadOnly(changes);
        ReversalMethod = reversalMethod;
        ExpectedReversal = expectedReversal;
    }

    /// <summary>
    /// How reversible the action is expected to be, so the user can be asked for extra
    /// confirmation before an action that cannot be fully reversed.
    /// </summary>
    public ReversalExpectation ExpectedReversal { get; }

    /// <summary>Identifies this preview; an approval is only valid for the preview it names.</summary>
    public Guid PreviewId { get; } = Guid.NewGuid();

    public RemediationAction Action { get; }

    public RemediationRequest Request => Action.Request;

    public ToolReference Tool => Action.Request.Tool;

    /// <summary>The approved version the action targets, taken from the manifest.</summary>
    public string? TargetVersion => Action.TargetVersion;

    public IReadOnlyList<string> ProposedChanges { get; }

    public string ReversalMethod { get; }
}
