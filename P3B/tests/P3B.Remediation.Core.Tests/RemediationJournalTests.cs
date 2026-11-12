using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>Workflow A and K: audit input, and recording reversal information.</summary>
public class RemediationJournalTests
{
    [Fact]
    public async Task Successful_change_is_recorded_in_the_journal()
    {
        var journal = new InMemoryReversalJournal();
        var service = TestSupport.Service(new FakeExecutor(), journal: journal);

        var run = await service.RemediateAsync(TestSupport.Request());

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(run.Remediation.ReversalJournalEntryId, entry.EntryId);
        Assert.Equal(ReversalEntryStatus.Pending, entry.Status);
        Assert.Equal("TEST-ISSUE-1", entry.IssueId);
    }

    [Fact]
    public async Task Cancelled_action_with_partial_changes_is_cancelled_and_still_recorded()
    {
        var journal = new InMemoryReversalJournal();
        var service = TestSupport.Service(new FakeExecutor { Mode = FakeExecutionMode.ReportCancelledWithPartialChange }, journal: journal);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Cancelled, run.Remediation.Outcome);
        Assert.Contains(FakeExecutor.SimulatedChange, run.Remediation.ObservedChanges);
        Assert.Single(journal.Entries);
        Assert.Null(run.SmokeTest);
    }

    [Fact]
    public async Task Journal_failure_is_reported_without_hiding_the_outcome()
    {
        var service = TestSupport.Service(new FakeExecutor(), journal: new InMemoryReversalJournal { FailOnRecord = true });

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Null(run.Remediation.ReversalJournalEntryId);
        Assert.Contains("WARNING: the reversal information could not be saved", run.Remediation.Message);
    }

    [Fact]
    public async Task Rejected_action_records_nothing()
    {
        var journal = new InMemoryReversalJournal();
        var approvals = new FakeApprovalProvider { Decide = p => Actions.ApprovalDecision.Reject(p) };
        var service = TestSupport.Service(new FakeExecutor(), approvals, journal: journal);

        await service.RemediateAsync(TestSupport.Request());

        Assert.Empty(journal.Entries);
    }

    [Fact]
    public async Task Valid_audit_issue_is_remediated()
    {
        var service = TestSupport.Service(new FakeExecutor());

        var result = await service.RemediateAsync(new AuditIssue { IssueId = "TEST-ISSUE-7", ToolId = TestManifests.ToolA, State = "missing" });

        Assert.Empty(result.Problems);
        Assert.Equal(RemediationOutcome.Succeeded, result.Run?.Remediation.Outcome);
        Assert.Equal("TEST-ISSUE-7", result.Run?.Remediation.Request.Issue.Value);
    }

    [Fact]
    public async Task Incomplete_audit_issue_is_rejected_before_any_remediation()
    {
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor);

        var result = await service.RemediateAsync(new AuditIssue { ToolId = TestManifests.ToolA });

        Assert.Null(result.Run);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Remediation_is_refused_while_a_shared_gate_is_held_by_a_rollback()
    {
        var gate = new OperationGate();
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor, gate: gate);

        using (gate.TryAcquire())
        {
            var run = await service.RemediateAsync(TestSupport.Request());
            Assert.Equal(RemediationOutcome.Busy, run.Remediation.Outcome);
        }

        Assert.Equal(0, executor.ExecuteCalls);
        Assert.False(service.IsBusy);
    }
}
