using System.Diagnostics;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.WinForms.Presentation;
using Xunit.Abstractions;

namespace P3B.Remediation.WinForms.Tests;

/// <summary>
/// P3B-PR-02 and P3B-PR-03, 20 trials each, at the dashboard-controller level (the layer the UI
/// calls). These are development-machine measurements, not the "agreed reference Windows
/// machine" trials the SRS requires; tools/Measure-UiResponsiveness.ps1 measures the real window.
/// </summary>
public class PerformanceTrialTests(ITestOutputHelper output)
{
    private const int Trials = 20;
    private const int RequiredPasses = 19;
    private static readonly TimeSpan AcknowledgeLimit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CompletionLimit = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task PR02_commands_are_acknowledged_within_one_second_during_installation()
    {
        var passes = 0;
        for (var trial = 1; trial <= Trials; trial++)
        {
            using var harness = new DashboardHarness(new FakeExecutor { Mode = FakeMode.WaitForRelease }).LoadStandard();
            var item = harness.Issue("TEST-ISSUE-A");

            var stopwatch = Stopwatch.StartNew();
            var running = harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);
            var acknowledged = item.Status == IssueStatus.InProgress && harness.Controller.IsBusy;
            var fixAcknowledged = stopwatch.Elapsed;

            // While the installation runs, the dashboard still answers other commands promptly.
            await harness.Executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopwatch.Restart();
            var banner = StatusText.Banner(harness.Controller);
            await harness.Controller.GetPendingReversalsAsync(CancellationToken.None);
            var duringInstall = stopwatch.Elapsed;

            harness.Executor.Release.SetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(10));

            var passed = acknowledged && banner.Kind == BannerKind.InProgress
                && fixAcknowledged < AcknowledgeLimit && duringInstall < AcknowledgeLimit;
            passes += passed ? 1 : 0;
            output.WriteLine($"Trial {trial,2}: fix acknowledged in {fixAcknowledged.TotalMilliseconds,7:0.0} ms; "
                + $"command during install answered in {duringInstall.TotalMilliseconds,7:0.0} ms; {(passed ? "PASS" : "FAIL")}");
        }

        output.WriteLine($"P3B-PR-02: {passes} of {Trials} trials within {AcknowledgeLimit.TotalSeconds} s (required {RequiredPasses}).");
        Assert.True(passes >= RequiredPasses, $"{passes} of {Trials} trials passed.");
    }

    [Fact]
    public async Task PR03_outcome_is_available_within_two_seconds_after_the_operation_completes()
    {
        var passes = 0;
        for (var trial = 1; trial <= Trials; trial++)
        {
            var executor = new TimedExecutor();
            using var harness = new DashboardHarness(executor).LoadStandard();
            var item = harness.Issue("TEST-ISSUE-A");

            var run = await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);
            var shownAfter = Stopwatch.GetElapsedTime(executor.CompletedAt);

            var passed = run is not null
                && StatusText.Status(item) == StatusText.Successful
                && StatusText.Verification(item.LastRun?.SmokeTest) == "Passed"
                && shownAfter < CompletionLimit;
            passes += passed ? 1 : 0;
            output.WriteLine($"Trial {trial,2}: outcome and smoke-test result available {shownAfter.TotalMilliseconds,7:0.0} ms after completion; {(passed ? "PASS" : "FAIL")}");
        }

        output.WriteLine($"P3B-PR-03: {passes} of {Trials} trials within {CompletionLimit.TotalSeconds} s (required {RequiredPasses}).");
        Assert.True(passes >= RequiredPasses, $"{passes} of {Trials} trials passed.");
    }

    /// <summary>Records the moment the simulated installation completes.</summary>
    private sealed class TimedExecutor : FakeExecutorBase
    {
        public long CompletedAt { get; private set; }

        protected override void OnCompleted() => CompletedAt = Stopwatch.GetTimestamp();
    }

    private abstract class FakeExecutorBase : IRemediationExecutor
    {
        private readonly FakeExecutor _inner = new();

        public bool CanHandle(RemediationAction action) => _inner.CanHandle(action);

        public ActionPreview CreatePreview(RemediationAction action) => _inner.CreatePreview(action);

        public async Task<ExecutionReport> ExecuteAsync(ActionPreview preview, ApprovalDecision approval, CancellationToken cancellationToken)
        {
            var report = await _inner.ExecuteAsync(preview, approval, cancellationToken);
            OnCompleted();
            return report;
        }

        protected abstract void OnCompleted();
    }
}
