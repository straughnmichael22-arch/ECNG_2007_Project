namespace P3B.Remediation.Core.Reversal;

/// <summary>
/// Durable store of reversal information. Entries stay pending until the change is reversed or
/// the user explicitly discards them after being warned (P3B-DR-02).
/// </summary>
public interface IReversalJournal
{
    /// <summary>Records a change. Changes with nothing to reverse are not recorded.</summary>
    Task<ReversalJournalEntry> RecordAsync(ReversalInfo reversal, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReversalJournalEntry>> GetPendingAsync(CancellationToken cancellationToken);

    Task<ReversalJournalEntry?> FindAsync(Guid entryId, CancellationToken cancellationToken);

    Task MarkReversedAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Discards a pending entry. Refused (returns false) unless the user has acknowledged the
    /// warning that the change can then no longer be reversed by P3B.
    /// </summary>
    Task<bool> DiscardAsync(Guid entryId, bool userAcknowledgedWarning, CancellationToken cancellationToken);
}
