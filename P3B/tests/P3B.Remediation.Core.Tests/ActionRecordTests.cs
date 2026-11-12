using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// P3B-DR-01: each action is associated with its originating audit issue and affected tool,
/// and its approved target, prior state, proposed and observed changes, completion outcome
/// and smoke-test result are all preserved.
/// </summary>
public class ActionRecordTests
{
    [Fact]
    public async Task Valid_request_from_an_audit_issue_produces_a_complete_action_record()
    {
        var created = RemediationRequestFactory.FromAuditIssue(new AuditIssue
        {
            IssueId = "TEST-ISSUE-42",
            ToolId = TestManifests.ToolA,
            State = "missing",
        });
        Assert.True(created.IsValid);
        var service = TestSupport.Service(new FakeExecutor());

        var run = await service.RemediateAsync(created.Request);
        var result = run.Remediation;

        // Association with the originating issue and the affected tool
        Assert.Equal("TEST-ISSUE-42", result.Request.Issue.Value);
        Assert.Equal(TestManifests.ToolA, result.Request.Tool.Value);
        var action = Assert.IsType<RemediationAction>(result.Action);
        Assert.Equal(result.Request.Issue, action.Issue);
        Assert.Equal(result.Request.Tool, action.Tool.Id);

        // Approved target, prior state, proposed and observed changes, completion outcome
        Assert.Equal(TestManifests.TargetVersion, action.TargetVersion);
        Assert.Equal(FakeExecutor.SimulatedPriorState, result.PriorState);
        var preview = Assert.IsType<ActionPreview>(result.Preview);
        Assert.Contains(FakeExecutor.SimulatedChange, preview.ProposedChanges);
        Assert.Contains(FakeExecutor.SimulatedChange, result.ObservedChanges);
        Assert.Equal(RemediationOutcome.Succeeded, result.Outcome);
        Assert.True(result.CompletedAt >= result.StartedAt);

        // Smoke-test result, kept separate but linked to the same action preview
        Assert.Equal(SmokeTestOutcome.Passed, run.SmokeTest?.Outcome);
        Assert.Equal(preview.PreviewId, run.SmokeTest?.PreviewId);
    }

    [Fact]
    public async Task Failed_action_still_records_its_association_and_prior_state()
    {
        var service = TestSupport.Service(new FakeExecutor { Mode = FakeExecutionMode.Fail });

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Failed, run.Remediation.Outcome);
        Assert.NotNull(run.Remediation.Action);
        Assert.NotNull(run.Remediation.Preview);
        Assert.Equal(FakeExecutor.SimulatedPriorState, run.Remediation.PriorState);
        Assert.Null(run.SmokeTest);
    }
}
