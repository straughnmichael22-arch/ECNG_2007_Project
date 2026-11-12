using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>3B-UR-002: remediation and smoke-test outcomes are distinct results.</summary>
public class ResultSeparationTests
{
    [Fact]
    public async Task Failed_smoke_test_does_not_change_a_successful_remediation()
    {
        var smokeTests = new FakeSmokeTestRunner { Outcome = SmokeTestOutcome.Failed };
        var service = TestSupport.Service(new FakeExecutor(), smokeTests: smokeTests);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Equal(SmokeTestOutcome.Failed, run.SmokeTest?.Outcome);
        Assert.Equal(run.Remediation.Preview?.PreviewId, run.SmokeTest?.PreviewId);
    }

    [Fact]
    public async Task Smoke_test_exception_is_reported_only_in_the_smoke_test_result()
    {
        var smokeTests = new FakeSmokeTestRunner { Throw = true };
        var service = TestSupport.Service(new FakeExecutor(), smokeTests: smokeTests);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Equal(SmokeTestOutcome.Failed, run.SmokeTest?.Outcome);
        Assert.Contains("TEST smoke-test exception", run.SmokeTest?.Message);
    }

    [Theory]
    [InlineData(FakeExecutionMode.Fail)]
    [InlineData(FakeExecutionMode.Throw)]
    public async Task Smoke_test_does_not_run_when_remediation_does_not_succeed(FakeExecutionMode mode)
    {
        var smokeTests = new FakeSmokeTestRunner();
        var service = TestSupport.Service(new FakeExecutor { Mode = mode }, smokeTests: smokeTests);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Failed, run.Remediation.Outcome);
        Assert.Null(run.SmokeTest);
        Assert.Equal(0, smokeTests.Calls);
    }

    [Fact]
    public void Remediation_and_smoke_test_results_are_unrelated_types()
    {
        Assert.False(typeof(RemediationResult).IsAssignableFrom(typeof(SmokeTestResult)));
        Assert.False(typeof(SmokeTestResult).IsAssignableFrom(typeof(RemediationResult)));
        Assert.DoesNotContain(
            typeof(RemediationResult).GetProperties(),
            p => p.PropertyType == typeof(SmokeTestResult) || p.PropertyType == typeof(SmokeTestOutcome));
    }
}
