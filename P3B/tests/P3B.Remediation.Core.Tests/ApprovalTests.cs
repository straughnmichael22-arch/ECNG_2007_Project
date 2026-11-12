using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

public class ApprovalTests
{
    [Fact]
    public async Task Rejected_action_is_never_executed()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider { Decide = p => ApprovalDecision.Reject(p, "TEST rejection") };
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Rejected, run.Remediation.Outcome);
        Assert.Equal("TEST rejection", run.Remediation.Message);
        Assert.Equal(1, approvals.Requests);
        Assert.Equal(0, executor.ExecuteCalls);
        Assert.Null(run.SmokeTest);
    }

    [Fact]
    public async Task Missing_decision_is_treated_as_rejection()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider { Decide = _ => null };
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Rejected, run.Remediation.Outcome);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Approval_of_a_different_preview_does_not_authorise_execution()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider
        {
            // Approves a freshly created preview, not the one actually shown.
            Decide = shown => ApprovalDecision.Approve(executor.CreatePreview(shown.Action)),
        };
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Rejected, run.Remediation.Outcome);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Approved_action_runs_once_after_the_full_preview_is_shown()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider();
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Equal(1, executor.ExecuteCalls);

        // 3B-UR-001: the preview shown before approval carries tool, version, changes and reversal method.
        var shown = Assert.IsType<ActionPreview>(approvals.LastPreview);
        Assert.Equal(TestManifests.ToolA, shown.Tool.Value);
        Assert.Equal(TestManifests.TargetVersion, shown.TargetVersion);
        Assert.NotEmpty(shown.ProposedChanges);
        Assert.False(string.IsNullOrWhiteSpace(shown.ReversalMethod));
        Assert.Equal(shown.PreviewId, run.Remediation.Preview?.PreviewId);
    }

    [Fact]
    public void Preview_without_reversal_method_or_changes_cannot_be_created()
    {
        var action = TestSupport.Action();

        Assert.ThrowsAny<ArgumentException>(() => new ActionPreview(action, ["TEST change"], " "));
        Assert.ThrowsAny<ArgumentException>(() => new ActionPreview(action, [], "TEST reversal"));
    }
}
