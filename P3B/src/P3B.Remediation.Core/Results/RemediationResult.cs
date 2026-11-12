using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Results;

/// <summary>
/// The record of one remediation request (P3B-DR-01): the originating issue and tool, the
/// action and its approved target, the proposed changes (via <see cref="Preview"/>), the prior
/// state, the observed changes and the completion outcome. Smoke-test results are kept
/// separately in <see cref="SmokeTestResult"/> (3B-UR-002).
/// </summary>
public sealed record RemediationResult
{
    /// <summary>The request, carrying the affected tool and originating audit issue.</summary>
    public required RemediationRequest Request { get; init; }

    public required RemediationOutcome Outcome { get; init; }

    public required string Message { get; init; }

    /// <summary>The resolved action, including its approved target version; null if the request was refused before planning succeeded.</summary>
    public RemediationAction? Action { get; init; }

    /// <summary>The preview (proposed changes) shown to the user; null if refused before a preview existed.</summary>
    public ActionPreview? Preview { get; init; }

    /// <summary>The relevant state observed before the action ran, if it ran.</summary>
    public string? PriorState { get; init; }

    public IReadOnlyList<string> ObservedChanges { get; init; } = Array.Empty<string>();

    /// <summary>How the action's changes can (or cannot) be reversed, including after a failure or cancellation.</summary>
    public ReversalInfo? Reversal { get; init; }

    /// <summary>The persisted reversal record (P3B-DR-02); null if nothing needed recording or saving failed (see <see cref="Message"/>).</summary>
    public Guid? ReversalJournalEntryId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}
