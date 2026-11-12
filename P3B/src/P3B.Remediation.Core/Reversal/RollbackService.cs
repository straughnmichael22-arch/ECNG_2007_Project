using P3B.Remediation.Core.Execution;

namespace P3B.Remediation.Core.Reversal;

/// <summary>
/// Reverses recorded changes, one at a time and only after explicit user confirmation.
/// Only <see cref="ReversalKind.Automatic"/> entries are reversed; manual-only entries are
/// explained, never attempted. An entry stays pending unless the reversal is verified.
/// </summary>
public sealed class RollbackService
{
    private readonly IReversalJournal _journal;
    private readonly IReadOnlyList<IReversalHandler> _handlers;
    private readonly OperationGate _gate;

    /// <param name="gate">Share the remediation service's gate so rollback never runs alongside remediation.</param>
    public RollbackService(IReversalJournal journal, IEnumerable<IReversalHandler> handlers, OperationGate gate)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(gate);
        _journal = journal;
        _handlers = handlers.ToArray();
        _gate = gate;
    }

    public async Task<RollbackPreview?> PreviewAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _journal.FindAsync(entryId, cancellationToken).ConfigureAwait(false);
        return entry is null
            ? null
            : new RollbackPreview(entry.EntryId, entry.ToolId, entry.Kind, entry.Method, entry.ChangesToReverse, entry.Status);
    }

    public async Task<RollbackResult> ReverseAsync(
        RollbackPreview preview,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var id = preview.EntryId;

        if (!userConfirmed)
        {
            return new RollbackResult(id, RollbackOutcome.NotConfirmed, "Reversal was not confirmed, so nothing was changed.");
        }

        using var lease = _gate.TryAcquire();
        if (lease is null)
        {
            return new RollbackResult(id, RollbackOutcome.Busy, "Another remediation or reversal is in progress.");
        }

        var entry = await _journal.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new RollbackResult(id, RollbackOutcome.NotFound, "No reversal record was found.");
        }

        if (entry.Status != ReversalEntryStatus.Pending)
        {
            return new RollbackResult(id, RollbackOutcome.AlreadyClosed, $"This change is already {entry.Status.ToString().ToLowerInvariant()}.");
        }

        switch (entry.Kind)
        {
            case ReversalKind.NothingToReverse:
                return new RollbackResult(id, RollbackOutcome.NothingToReverse, entry.Method);
            case ReversalKind.ManualOnly:
                return new RollbackResult(id, RollbackOutcome.ManualOnly, $"P3B cannot reverse this change automatically. {entry.Method}");
        }

        if (entry.Operation is null)
        {
            return new RollbackResult(id, RollbackOutcome.Failed, "The reversal record has no reversal operation, so nothing was changed.");
        }

        var handlers = _handlers.Where(h => h.CanHandle(entry.Operation)).ToArray();
        if (handlers.Length != 1)
        {
            return new RollbackResult(id, RollbackOutcome.NoHandler, "No single handler can reverse this change, so nothing was changed.");
        }

        ReversalAttempt attempt;
        try
        {
            attempt = await handlers[0].ReverseAsync(entry.Operation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new RollbackResult(id, RollbackOutcome.Cancelled, "The reversal was cancelled. The record is kept so you can try again.");
        }
        catch (Exception ex)
        {
            return new RollbackResult(id, RollbackOutcome.Failed, $"The reversal failed: {ex.Message}. The record is kept.");
        }

        switch (attempt.Status)
        {
            case ReversalAttemptStatus.Conflict:
                return new RollbackResult(id, RollbackOutcome.Conflict, $"{attempt.Message} The record is kept.");
            case ReversalAttemptStatus.Failed:
                return new RollbackResult(id, RollbackOutcome.Failed, $"{attempt.Message} The record is kept.");
        }

        try
        {
            await _journal.MarkReversedAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new RollbackResult(id, RollbackOutcome.Reversed, $"{attempt.Message} WARNING: the record could not be closed ({ex.Message}).");
        }

        return new RollbackResult(id, RollbackOutcome.Reversed, attempt.Message);
    }
}
