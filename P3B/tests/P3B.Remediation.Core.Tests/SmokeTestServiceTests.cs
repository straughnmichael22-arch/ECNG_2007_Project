using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

public class SmokeTestServiceTests
{
    private static ActionPreview PreviewWith(params SmokeCheckDefinition[] checks)
    {
        var tool = TestManifests.Tool(TestManifests.ToolA, smokeChecks: checks);
        var action = new RemediationAction(TestSupport.Request(), tool);
        return new ActionPreview(action, ["TEST change"], "TEST reversal");
    }

    private static (SmokeTestService Service, FakeSmokeCheckHandler Handler) Service(TimeSpan? checkTimeout = null)
    {
        var handler = new FakeSmokeCheckHandler();
        return (new SmokeTestService([handler], checkTimeout), handler);
    }

    [Fact]
    public async Task Successful_smoke_test_when_every_check_passes()
    {
        var (service, handler) = Service();

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A"), new TestOnlyCheck("B")), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Passed, result.Outcome);
        Assert.Equal(2, result.Checks.Count);
        Assert.All(result.Checks, c => Assert.Equal(SmokeCheckStatus.Passed, c.Status));
        Assert.Equal(new[] { "A", "B" }, handler.Ran);
        Assert.Equal(TestManifests.TargetVersion, result.TargetVersion);
    }

    [Fact]
    public async Task Unsuccessful_smoke_test_explains_which_check_failed()
    {
        var (service, handler) = Service();
        handler.Behaviours["B"] = FakeCheckBehaviour.Fail;

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A"), new TestOnlyCheck("B")), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Failed, result.Outcome);
        Assert.Contains("1 of 2", result.Message);
        Assert.Contains("TEST B failed", result.Message);
        Assert.Equal(SmokeCheckStatus.Failed, result.Checks[1].Status);
    }

    [Fact]
    public async Task Unsupported_when_the_manifest_declares_no_smoke_tests()
    {
        var (service, _) = Service();

        var result = await service.RunAsync(PreviewWith(), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Unsupported, result.Outcome);
        Assert.Empty(result.Checks);
        Assert.Contains("defines no smoke tests", result.Message);
    }

    [Fact]
    public async Task Unsupported_when_a_check_kind_is_not_recognised()
    {
        var (service, _) = Service();

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A"), new UnrecognisedSmokeCheck("TEST-unknown-kind")), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Unsupported, result.Outcome);
        Assert.Equal(SmokeCheckStatus.Unsupported, result.Checks[1].Status);
        Assert.Contains("does not recognise", result.Checks[1].Explanation);
    }

    [Fact]
    public async Task Unsupported_when_no_handler_can_run_a_check()
    {
        var service = new SmokeTestService([]);

        var result = await service.RunAsync(PreviewWith(new CommandOnPathCheck("TEST-tool")), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Unsupported, result.Outcome);
        Assert.Contains("no way to run", result.Checks[0].Explanation);
    }

    [Fact]
    public async Task Timed_out_check_is_reported_and_later_checks_still_run()
    {
        var (service, handler) = Service(TimeSpan.FromMilliseconds(200));
        handler.Behaviours["slow"] = FakeCheckBehaviour.HangUntilCancelled;

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("slow"), new TestOnlyCheck("next")), CancellationToken.None)
            .WaitAsync(TestSupport.Timeout);

        Assert.Equal(SmokeTestOutcome.TimedOut, result.Outcome);
        Assert.Equal(SmokeCheckStatus.TimedOut, result.Checks[0].Status);
        Assert.Contains("did not finish", result.Checks[0].Explanation);
        Assert.Equal(SmokeCheckStatus.Passed, result.Checks[1].Status);
    }

    [Fact]
    public async Task Failure_outranks_timeout_and_unsupported()
    {
        var (service, handler) = Service(TimeSpan.FromMilliseconds(200));
        handler.Behaviours["slow"] = FakeCheckBehaviour.HangUntilCancelled;
        handler.Behaviours["bad"] = FakeCheckBehaviour.Fail;
        handler.Behaviours["na"] = FakeCheckBehaviour.Unsupported;

        var result = await service.RunAsync(
            PreviewWith(new TestOnlyCheck("na"), new TestOnlyCheck("slow"), new TestOnlyCheck("bad")),
            CancellationToken.None).WaitAsync(TestSupport.Timeout);

        Assert.Equal(SmokeTestOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Handler_exception_is_a_failed_check_with_its_reason()
    {
        var (service, handler) = Service();
        handler.Behaviours["A"] = FakeCheckBehaviour.Throw;

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A")), CancellationToken.None);

        Assert.Equal(SmokeTestOutcome.Failed, result.Outcome);
        Assert.Contains("TEST A exception", result.Checks[0].Explanation);
    }

    [Fact]
    public async Task User_cancellation_returns_cancelled_with_the_checks_completed_so_far()
    {
        var (service, handler) = Service();
        handler.Behaviours["slow"] = FakeCheckBehaviour.HangUntilCancelled;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A"), new TestOnlyCheck("slow"), new TestOnlyCheck("never")), cts.Token)
            .WaitAsync(TestSupport.Timeout);

        Assert.Equal(SmokeTestOutcome.Cancelled, result.Outcome);
        Assert.Single(result.Checks);
        Assert.DoesNotContain("never", handler.Ran);
    }

    [Fact]
    public async Task Result_never_claims_the_environment_is_ready()
    {
        var (service, _) = Service();

        var result = await service.RunAsync(PreviewWith(new TestOnlyCheck("A")), CancellationToken.None);

        Assert.Equal(SmokeTestResult.ReadinessDisclaimer, result.ReadinessNote);
        Assert.Contains("P3A", result.ReadinessNote);
        Assert.DoesNotContain("environment is ready", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remediation_success_and_failed_smoke_test_stay_distinct_in_the_run()
    {
        var (smokeTests, handler) = Service();
        handler.Behaviours["A"] = FakeCheckBehaviour.Fail;
        var manifest = new InMemoryManifestSource(Manifest.ManifestLoadResult.Loaded(
            new LocalEdaToolManifest(
                ProvisionalManifestSchema.SupportedVersion,
                [TestManifests.Tool(TestManifests.ToolA, smokeChecks: [new TestOnlyCheck("A")])]),
            "TEST manifest"));
        var service = TestSupport.Service(new FakeExecutor(), smokeTests: smokeTests, manifest: manifest);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Equal(SmokeTestOutcome.Failed, run.SmokeTest?.Outcome);
        Assert.Single(run.SmokeTest!.Checks);
    }
}
