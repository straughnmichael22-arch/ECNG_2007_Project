using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Configuration;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>User-level PATH changes against an in-memory environment only.</summary>
public class ConfigurationServiceTests
{
    private const string PriorPath = @"%USERPROFILE%\TEST-existing;C:\TEST-other";
    private const string NewDirectory = @"C:\TEST-tools\bin";

    private static (ConfigurationService Service, FakeUserEnvironment Environment, ActionPreview Preview, UserPathAdditionPlan Plan) Prepare(
        FakeUserEnvironment? environment = null)
    {
        environment ??= new FakeUserEnvironment();
        environment.SetExternally(ConfigurationService.PathVariable, PriorPath);
        var service = new ConfigurationService(environment);
        var plan = service.PlanUserPathAddition(NewDirectory);
        var preview = new ActionPreview(TestSupport.Action(), plan.ProposedChanges, plan.ReversalMethod);
        return (service, environment, preview, plan);
    }

    [Theory]
    [InlineData(@"relative\path")]
    [InlineData(@"C:\TEST;C:\other")]
    [InlineData("C:\\TEST\"quoted")]
    [InlineData(@"%USERPROFILE%\bin")]
    [InlineData(" ")]
    public void Unsafe_or_relative_directories_are_rejected(string directory)
    {
        var service = new ConfigurationService(new FakeUserEnvironment());

        Assert.ThrowsAny<ArgumentException>(() => service.PlanUserPathAddition(directory));
    }

    [Fact]
    public void Plan_describes_a_user_only_change_and_its_reversal()
    {
        var (_, _, _, plan) = Prepare();

        Assert.False(plan.AlreadyPresent);
        Assert.Contains("current user only", Assert.Single(plan.ProposedChanges));
        Assert.Contains("restore", plan.ReversalMethod);
    }

    [Fact]
    public void Without_approval_nothing_is_written()
    {
        var (service, environment, preview, plan) = Prepare();

        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Reject(preview), plan);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(0, environment.Writes);
        Assert.Equal(PriorPath, environment.GetUserVariable(ConfigurationService.PathVariable));
    }

    [Fact]
    public void Approved_change_appends_the_directory_and_records_the_prior_value()
    {
        var (service, environment, preview, plan) = Prepare();

        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Approve(preview), plan);

        Assert.Equal(ExecutionStatus.Succeeded, report.Status);
        Assert.Equal($"{PriorPath};{NewDirectory}", environment.GetUserVariable(ConfigurationService.PathVariable));
        var restore = Assert.IsType<RestoreUserEnvironmentVariableReversal>(report.Reversal?.Operation);
        Assert.Equal(PriorPath, restore.PriorValue);
        Assert.Equal(ReversalKind.Automatic, report.Reversal?.Kind);
    }

    [Fact]
    public void Directory_already_present_changes_nothing()
    {
        var environment = new FakeUserEnvironment();
        var (service, _, preview, _) = Prepare(environment);
        environment.SetExternally(ConfigurationService.PathVariable, $"{PriorPath};{NewDirectory}\\");
        var plan = service.PlanUserPathAddition(NewDirectory);

        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Approve(preview), plan);

        Assert.True(plan.AlreadyPresent);
        Assert.Equal(ExecutionStatus.Succeeded, report.Status);
        Assert.Equal(ReversalKind.NothingToReverse, report.Reversal?.Kind);
        Assert.Equal(0, environment.Writes);
    }

    [Fact]
    public void Unverified_change_is_reported_as_failure()
    {
        var (service, _, preview, plan) = Prepare(new FakeUserEnvironment { IgnoreWrites = true });

        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Approve(preview), plan);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("could not be verified", report.Message);
    }

    [Fact]
    public async Task Reversal_restores_the_prior_value()
    {
        var (service, environment, preview, plan) = Prepare();
        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Approve(preview), plan);

        var attempt = await new UserEnvironmentReversalHandler(environment).ReverseAsync(report.Reversal!.Operation!, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(PriorPath, environment.GetUserVariable(ConfigurationService.PathVariable));
    }

    [Fact]
    public async Task Reversal_refuses_when_something_else_changed_the_variable()
    {
        var (service, environment, preview, plan) = Prepare();
        var report = service.ApplyUserPathAddition(preview, ApprovalDecision.Approve(preview), plan);
        environment.SetExternally(ConfigurationService.PathVariable, @"C:\TEST-changed-by-someone-else");

        var attempt = await new UserEnvironmentReversalHandler(environment).ReverseAsync(report.Reversal!.Operation!, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Conflict, attempt.Status);
        Assert.Equal(@"C:\TEST-changed-by-someone-else", environment.GetUserVariable(ConfigurationService.PathVariable));
        Assert.Contains(PriorPath, attempt.Message);
    }

    [Fact]
    public async Task Reversal_removes_a_variable_that_did_not_exist_before()
    {
        var environment = new FakeUserEnvironment();
        environment.SetExternally("TEST_VARIABLE", "TEST_VALUE");

        var attempt = await new UserEnvironmentReversalHandler(environment).ReverseAsync(
            new RestoreUserEnvironmentVariableReversal("TEST_VARIABLE", null, "TEST_VALUE"),
            CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Reversed, attempt.Status);
        Assert.Null(environment.GetUserVariable("TEST_VARIABLE"));
    }
}
