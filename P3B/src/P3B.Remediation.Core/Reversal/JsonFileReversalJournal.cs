using System.Text.Json;
using System.Text.Json.Serialization;

namespace P3B.Remediation.Core.Reversal;

/// <summary>
/// Reversal journal stored as a JSON file. Writes go to a temporary file that then replaces the
/// journal, so an interrupted write never leaves a half-written journal. A journal that cannot
/// be read is reported and never overwritten.
/// </summary>
public sealed class JsonFileReversalJournal : IReversalJournal
{
    public const string DiscardWarning =
        "Discarding this record means P3B can no longer reverse the change. The change itself stays in place.";

    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeProvider _time;

    public JsonFileReversalJournal(string journalPath, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        JournalPath = Path.GetFullPath(journalPath);
        _time = time ?? TimeProvider.System;
    }

    public string JournalPath { get; }

    public async Task<ReversalJournalEntry> RecordAsync(ReversalInfo reversal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        if (reversal.Kind == ReversalKind.NothingToReverse)
        {
            throw new ArgumentException("Nothing was changed, so there is nothing to record.", nameof(reversal));
        }

        var entry = ReversalJournalEntry.FromReversal(reversal, _time.GetUtcNow());
        await UpdateAsync(entries => entries.Add(entry), cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task<IReadOnlyList<ReversalJournalEntry>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return entries.Where(e => e.Status == ReversalEntryStatus.Pending).ToArray();
    }

    public async Task<ReversalJournalEntry?> FindAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault(e => e.EntryId == entryId);
    }

    public Task MarkReversedAsync(Guid entryId, CancellationToken cancellationToken) =>
        UpdateAsync(entries => Close(entries, entryId, ReversalEntryStatus.Reversed), cancellationToken);

    public async Task<bool> DiscardAsync(Guid entryId, bool userAcknowledgedWarning, CancellationToken cancellationToken)
    {
        if (!userAcknowledgedWarning)
        {
            return false;
        }

        await UpdateAsync(entries => Close(entries, entryId, ReversalEntryStatus.Discarded), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void Close(List<ReversalJournalEntry> entries, Guid entryId, ReversalEntryStatus status)
    {
        var index = entries.FindIndex(e => e.EntryId == entryId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"No reversal record {entryId} exists in {JournalPath}.");
        }

        if (entries[index].Status != ReversalEntryStatus.Pending)
        {
            throw new InvalidOperationException($"Reversal record {entryId} is already {entries[index].Status}.");
        }

        entries[index] = entries[index] with { Status = status, ClosedAt = _time.GetUtcNow() };
    }

    private async Task<List<ReversalJournalEntry>> ReadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Load();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task UpdateAsync(Action<List<ReversalJournalEntry>> change, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = Load();
            change(entries);
            Save(entries);
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<ReversalJournalEntry> Load()
    {
        if (!File.Exists(JournalPath))
        {
            return [];
        }

        JournalDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<JournalDocument>(File.ReadAllText(JournalPath), SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The reversal journal at {JournalPath} could not be read. It has not been changed.", ex);
        }

        if (document is null || document.FormatVersion != FormatVersion || document.Entries is null)
        {
            throw new InvalidDataException($"The reversal journal at {JournalPath} has an unknown format. It has not been changed.");
        }

        return document.Entries;
    }

    private void Save(List<ReversalJournalEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        var temporaryPath = JournalPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new JournalDocument(FormatVersion, entries), SerializerOptions));
        File.Move(temporaryPath, JournalPath, overwrite: true);
    }

    internal sealed record JournalDocument(int FormatVersion, List<ReversalJournalEntry> Entries);
}
