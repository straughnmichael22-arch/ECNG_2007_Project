using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

public class RollbackServiceTests
{
    private static readonly ReversalOperation TestOperation = new WingetUninstallReversal("TEST-SOURCE-A", TestManifests.TargetVersion);

    private static async Task<(RollbackService Service, RollbackPreview Preview, InMemoryReversalJournal Journal, FakeReversalHandler Handler)> Prepare(
        ReversalKind kind = ReversalKind.Automatic,
        FakeReversalHandler? handler = null,
        OperationGate? gate = null)
    {
        var journal = new InMemoryReversalJournal();
        var entry = journal.Add(kind, kind == ReversalKind.Automatic ? TestOperation : null);
        handler ??= new FakeReversalHandler();
        var service = new RollbackService(journal, [handler], gate ?? new OperationGate());
        var preview = await service.PreviewAsync(entry.EntryId);
        return (service, preview!, journal, handler);
    }

    [Fact]
    public async Task Preview_describes_the_reversal()
    {
        var (_, preview, _, _) = await Prepare();

        Assert.Equal(ReversalKind.Automatic, preview.Kind);
        Assert.Equal("TEST reversal method", preview.Method);
        Assert.Equal(ReversalEntryStatus.Pending, preview.Status);
    }

    [Fact]
    public async Task Unconfirmed_reversal_changes_nothing()
    {
        var (service, preview, journal, handler) = await Prepare();

        var result = await service.ReverseAsync(preview, userConfirmed: false);

        Assert.Equal(RollbackOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(0, handler.Calls);
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Confirmed_automatic_reversal_runs_the_handler_and_closes_the_record()
    {
        var (service, preview, journal, handler) = await Prepare();

        var result = await service.ReverseAsync(preview, userConfirmed: true);

        Assert.Equal(RollbackOutcome.Reversed, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Empty(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Manual_only_change_is_explained_and_never_attempted()
    {
        var (service, preview, journal, handler) = await Prepare(ReversalKind.ManualOnly);

        var result = await service.ReverseAsync(preview, userConfirmed: true);

        Assert.Equal(RollbackOutcome.ManualOnly, result.Outcome);
        Assert.Contains("cannot reverse this change automatically", result.Message);
        Assert.Equal(0, handler.Calls);
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ReversalAttemptStatus.Conflict, RollbackOutcome.Conflict)]
    [InlineData(ReversalAttemptStatus.Failed, RollbackOutcome.Failed)]
    public async Task Unsuccessful_reversal_keeps_the_record(ReversalAttemptStatus attempt, RollbackOutcome expected)
    {
        var (service, preview, journal, _) = await Prepare(handler: new FakeReversalHandler { Result = new ReversalAttempt(attempt, "TEST not reversed") });

        var result = await service.ReverseAsync(preview, userConfirmed: true);

        Assert.Equal(expected, result.Outcome);
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Handler_exception_is_a_failure_and_keeps_the_record()
    {
        var (service, preview, journal, _) = await Prepare(handler: new FakeReversalHandler { Throw = true });

        var result = await service.ReverseAsync(preview, userConfirmed: true);

        Assert.Equal(RollbackOutcome.Failed, result.Outcome);
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_reversal_keeps_the_record()
    {
        var (service, preview, journal, _) = await Prepare(handler: new FakeReversalHandler { WaitForCancellation = true });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await service.ReverseAsync(preview, userConfirmed: true, cts.Token);

        Assert.Equal(RollbackOutcome.Cancelled, result.Outcome);
        Assert.Single(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reversal_is_refused_while_remediation_holds_the_shared_gate()
    {
        var gate = new OperationGate();
        var (service, preview, _, handler) = await Prepare(gate: gate);

        using (gate.TryAcquire())
        {
            var result = await service.ReverseAsync(preview, userConfirmed: true);
            Assert.Equal(RollbackOutcome.Busy, result.Outcome);
        }

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Already_reversed_change_is_not_reversed_again()
    {
        var (service, preview, _, handler) = await Prepare();
        await service.ReverseAsync(preview, userConfirmed: true);

        var second = await service.ReverseAsync(preview, userConfirmed: true);

        Assert.Equal(RollbackOutcome.AlreadyClosed, second.Outcome);
        Assert.Equal(1, handler.Calls);
    }
}
