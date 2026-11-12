using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>In-memory journal double. Can be told to fail so the failure path can be tested.</summary>
internal sealed class InMemoryReversalJournal : IReversalJournal
{
    private readonly List<ReversalJournalEntry> _entries = [];
    private readonly object _sync = new();

    public bool FailOnRecord { get; init; }

    public IReadOnlyList<ReversalJournalEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public Task<ReversalJournalEntry> RecordAsync(ReversalInfo reversal, CancellationToken cancellationToken)
    {
        if (FailOnRecord)
        {
            throw new IOException("TEST journal unavailable");
        }

        var entry = ReversalJournalEntry.FromReversal(reversal, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            _entries.Add(entry);
        }

        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<ReversalJournalEntry>> GetPendingAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ReversalJournalEntry>>(
                _entries.Where(e => e.Status == ReversalEntryStatus.Pending).ToArray());
        }
    }

    public Task<ReversalJournalEntry?> FindAsync(Guid entryId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(_entries.FirstOrDefault(e => e.EntryId == entryId));
        }
    }

    public Task MarkReversedAsync(Guid entryId, CancellationToken cancellationToken)
    {
        Close(entryId, ReversalEntryStatus.Reversed);
        return Task.CompletedTask;
    }

    public Task<bool> DiscardAsync(Guid entryId, bool userAcknowledgedWarning, CancellationToken cancellationToken)
    {
        if (!userAcknowledgedWarning)
        {
            return Task.FromResult(false);
        }

        Close(entryId, ReversalEntryStatus.Discarded);
        return Task.FromResult(true);
    }

    /// <summary>Adds an entry directly, for rollback tests.</summary>
    public ReversalJournalEntry Add(ReversalKind kind, ReversalOperation? operation, string method = "TEST reversal method")
    {
        var entry = new ReversalJournalEntry(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), TestManifests.ToolA, "TEST-ISSUE-1",
            kind, method, "TEST prior state", ["TEST change"], operation, ReversalEntryStatus.Pending, null);
        lock (_sync)
        {
            _entries.Add(entry);
        }

        return entry;
    }

    private void Close(Guid entryId, ReversalEntryStatus status)
    {
        lock (_sync)
        {
            var index = _entries.FindIndex(e => e.EntryId == entryId);
            _entries[index] = _entries[index] with { Status = status, ClosedAt = DateTimeOffset.UtcNow };
        }
    }
}
