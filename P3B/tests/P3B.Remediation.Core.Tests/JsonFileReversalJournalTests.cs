using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests;

/// <summary>P3B-DR-02: reversal information survives restarts until reversed or explicitly discarded.</summary>
public class JsonFileReversalJournalTests
{
    private static ReversalInfo AutomaticReversal() => ReversalInfo.Automatic(
        new Actions.ActionPreview(TestSupport.Action(), ["TEST change"], "TEST reversal"),
        "TEST reversal",
        "TEST prior state",
        ["TEST change"],
        new WingetUninstallReversal("TEST-SOURCE-A", TestManifests.TargetVersion));

    [Fact]
    public async Task Recorded_entry_survives_a_new_journal_instance_with_its_operation()
    {
        using var temp = new TempDirectory();
        var path = temp.PathOf("journal.json");

        var recorded = await new JsonFileReversalJournal(path).RecordAsync(AutomaticReversal(), CancellationToken.None);
        var reloaded = await new JsonFileReversalJournal(path).FindAsync(recorded.EntryId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(ReversalEntryStatus.Pending, reloaded.Status);
        Assert.Equal(ReversalKind.Automatic, reloaded.Kind);
        Assert.Equal(new WingetUninstallReversal("TEST-SOURCE-A", TestManifests.TargetVersion), reloaded.Operation);
        Assert.Equal("TEST prior state", reloaded.PriorState);
        Assert.Equal("TEST change", Assert.Single(reloaded.ChangesToReverse));
    }

    [Fact]
    public async Task Reversed_entry_is_no_longer_pending()
    {
        using var temp = new TempDirectory();
        var journal = new JsonFileReversalJournal(temp.PathOf("journal.json"));
        var entry = await journal.RecordAsync(AutomaticReversal(), CancellationToken.None);

        await journal.MarkReversedAsync(entry.EntryId, CancellationToken.None);

        Assert.Empty(await journal.GetPendingAsync(CancellationToken.None));
        Assert.Equal(ReversalEntryStatus.Reversed, (await journal.FindAsync(entry.EntryId, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task Discarding_requires_the_user_to_acknowledge_the_warning()
    {
        using var temp = new TempDirectory();
        var journal = new JsonFileReversalJournal(temp.PathOf("journal.json"));
        var entry = await journal.RecordAsync(AutomaticReversal(), CancellationToken.None);

        Assert.False(await journal.DiscardAsync(entry.EntryId, userAcknowledgedWarning: false, CancellationToken.None));
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));

        Assert.True(await journal.DiscardAsync(entry.EntryId, userAcknowledgedWarning: true, CancellationToken.None));
        Assert.Empty(await journal.GetPendingAsync(CancellationToken.None));
        Assert.Equal(ReversalEntryStatus.Discarded, (await journal.FindAsync(entry.EntryId, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task Nothing_to_reverse_is_not_recorded()
    {
        using var temp = new TempDirectory();
        var journal = new JsonFileReversalJournal(temp.PathOf("journal.json"));
        var nothing = ReversalInfo.NothingToReverse(new Actions.ActionPreview(TestSupport.Action(), ["TEST change"], "TEST reversal"), null);

        await Assert.ThrowsAsync<ArgumentException>(() => journal.RecordAsync(nothing, CancellationToken.None));
    }

    [Fact]
    public async Task Unreadable_journal_is_reported_and_never_overwritten()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteText("journal.json", "{ this is not json");

        var journal = new JsonFileReversalJournal(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => journal.RecordAsync(AutomaticReversal(), CancellationToken.None));
        Assert.Equal("{ this is not json", File.ReadAllText(path));
    }

    [Fact]
    public async Task Missing_journal_file_means_no_pending_entries()
    {
        using var temp = new TempDirectory();

        var pending = await new JsonFileReversalJournal(temp.PathOf("absent.json")).GetPendingAsync(CancellationToken.None);

        Assert.Empty(pending);
    }
}
