using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

public class OutcomeTests
{
    [Fact]
    public async Task Successful_action_reports_changes_and_reversal_information()
    {
        var service = TestSupport.Service(new FakeExecutor());

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Contains(FakeExecutor.SimulatedChange, run.Remediation.ObservedChanges);
        var reversal = Assert.IsType<ReversalInfo>(run.Remediation.Reversal);
        Assert.Equal(TestManifests.ToolA, reversal.Tool.Value);
        Assert.Equal("TEST-ISSUE-1", reversal.Issue.Value);
        Assert.Equal(SmokeTestOutcome.Passed, run.SmokeTest?.Outcome);
    }

    [Fact]
    public async Task Executor_reporting_failure_gives_failed_outcome()
    {
        var service = TestSupport.Service(new FakeExecutor { Mode = FakeExecutionMode.Fail });

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Failed, run.Remediation.Outcome);
        Assert.Equal("TEST failure", run.Remediation.Message);
    }

    [Fact]
    public async Task Executor_exception_gives_failed_outcome()
    {
        var service = TestSupport.Service(new FakeExecutor { Mode = FakeExecutionMode.Throw });

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Failed, run.Remediation.Outcome);
        Assert.Contains("TEST exception", run.Remediation.Message);
    }

    [Fact]
    public async Task Cancellation_during_execution_gives_cancelled_outcome()
    {
        var executor = new FakeExecutor { Mode = FakeExecutionMode.WaitForCancellation };
        var service = TestSupport.Service(executor);
        using var cts = new CancellationTokenSource();

        var pending = service.RemediateAsync(TestSupport.Request(), cts.Token);
        await executor.Started.Task.WaitAsync(TestSupport.Timeout);
        await cts.CancelAsync();
        var run = await pending.WaitAsync(TestSupport.Timeout);

        Assert.Equal(RemediationOutcome.Cancelled, run.Remediation.Outcome);
        Assert.Null(run.SmokeTest);
        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Cancellation_during_approval_gives_cancelled_outcome_without_execution()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider { WaitForCancellation = true };
        var service = TestSupport.Service(executor, approvals);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var run = await service.RemediateAsync(TestSupport.Request(), cts.Token);

        Assert.Equal(RemediationOutcome.Cancelled, run.Remediation.Outcome);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Tool_not_defined_in_the_manifest_is_unsupported_and_nothing_runs()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider();
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request(TestManifests.NotInManifestTool));

        // 3B-UR-003
        Assert.Equal(RemediationOutcome.Unsupported, run.Remediation.Outcome);
        Assert.StartsWith(RemediationService.UnsupportedMessage, run.Remediation.Message);
        Assert.Contains(TestManifests.NotInManifestTool, run.Remediation.Message);
        Assert.Null(run.Remediation.Action);
        Assert.Equal(0, approvals.Requests);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Tool_with_no_manifest_remediation_is_unsupported()
    {
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor);

        var run = await service.RemediateAsync(TestSupport.Request(TestManifests.NoFixTool));

        Assert.Equal(RemediationOutcome.Unsupported, run.Remediation.Outcome);
        Assert.StartsWith(RemediationService.UnsupportedMessage, run.Remediation.Message);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Action_with_no_matching_executor_is_unsupported()
    {
        var executor = new FakeExecutor { Handles = _ => false };
        var approvals = new FakeApprovalProvider();
        var service = TestSupport.Service(executor, approvals);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Unsupported, run.Remediation.Outcome);
        Assert.StartsWith(RemediationService.UnsupportedMessage, run.Remediation.Message);
        Assert.NotNull(run.Remediation.Action);
        Assert.Equal(0, approvals.Requests);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Unusable_manifest_is_reported_before_any_preview_or_execution()
    {
        var executor = new FakeExecutor();
        var approvals = new FakeApprovalProvider();
        var manifest = InMemoryManifestSource.Failing(ManifestLoadStatus.Invalid, "TEST manifest is invalid");
        var service = TestSupport.Service(executor, approvals, manifest: manifest);

        var run = await service.RemediateAsync(TestSupport.Request());

        // 3B-IR-003
        Assert.Equal(RemediationOutcome.InvalidConfiguration, run.Remediation.Outcome);
        Assert.Equal("TEST manifest is invalid", run.Remediation.Message);
        Assert.Null(run.Remediation.Preview);
        Assert.Equal(0, approvals.Requests);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Theory]
    [InlineData("malformed.json")]
    [InlineData("missing-required-fields.json")]
    [InlineData("unsupported-version.json")]
    public async Task Invalid_manifest_file_stops_remediation_before_execution(string fixture)
    {
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor, manifest: new ManifestService(TestManifests.FixturePath(fixture)));

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.InvalidConfiguration, run.Remediation.Outcome);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Unconfigured_manifest_refuses_every_request()
    {
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor, manifest: new UnconfiguredManifestSource());

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.InvalidConfiguration, run.Remediation.Outcome);
        Assert.Equal(UnconfiguredManifestSource.Description, run.Remediation.Message);
        Assert.Equal(0, executor.ExecuteCalls);
    }

    [Fact]
    public async Task More_than_one_matching_executor_is_invalid_configuration()
    {
        var first = new FakeExecutor();
        var second = new FakeExecutor();
        var service = new RemediationService(
            InMemoryManifestSource.WithTestTools(),
            [first, second],
            new FakeSmokeTestRunner(),
            new FakeApprovalProvider(),
            new InMemoryReversalJournal());

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.InvalidConfiguration, run.Remediation.Outcome);
        Assert.Equal(0, first.ExecuteCalls + second.ExecuteCalls);
    }
}
