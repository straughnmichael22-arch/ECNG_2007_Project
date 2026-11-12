namespace P3B.Remediation.Core.Reversal;

public enum ReversalEntryStatus
{
    /// <summary>Reversal information is preserved and the change has not been reversed (P3B-DR-02).</summary>
    Pending,

    /// <summary>The change was reversed and verified.</summary>
    Reversed,

    /// <summary>The user discarded the information after being warned that reversal would no longer be possible.</summary>
    Discarded,
}

/// <summary>One persisted record of a change and how to reverse it.</summary>
public sealed record ReversalJournalEntry(
    Guid EntryId,
    DateTimeOffset RecordedAt,
    Guid PreviewId,
    string ToolId,
    string IssueId,
    ReversalKind Kind,
    string Method,
    string? PriorState,
    IReadOnlyList<string> ChangesToReverse,
    ReversalOperation? Operation,
    ReversalEntryStatus Status,
    DateTimeOffset? ClosedAt)
{
    public static ReversalJournalEntry FromReversal(ReversalInfo reversal, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        return new ReversalJournalEntry(
            Guid.NewGuid(),
            recordedAt,
            reversal.PreviewId,
            reversal.Tool.Value,
            reversal.Issue.Value,
            reversal.Kind,
            reversal.Method,
            reversal.PriorState,
            reversal.ChangesToReverse,
            reversal.Operation,
            ReversalEntryStatus.Pending,
            ClosedAt: null);
    }
}
