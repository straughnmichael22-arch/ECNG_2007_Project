using System.Text.Json;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.WinForms.Presentation;

namespace P3B.Remediation.WinForms.Tests;

/// <summary>The dashboard workflow, end to end, with fakes in place of anything that changes the computer.</summary>
public class DashboardControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Before_loading_audit_results_the_banner_asks_for_them()
    {
        using var harness = new DashboardHarness();

        var banner = StatusText.Banner(harness.Controller);

        Assert.Equal(BannerKind.Neutral, banner.Kind);
        Assert.Contains("Load P3A's audit results", banner.Text);
    }

    [Fact]
    public void Loaded_audit_shows_every_issue_and_a_not_ready_banner()
    {
        using var harness = new DashboardHarness().LoadStandard();

        var banner = StatusText.Banner(harness.Controller);

        Assert.Equal(4, harness.Controller.Issues.Count);
        Assert.Equal(BannerKind.NotReady, banner.Kind);
        Assert.StartsWith(StatusText.NotReady, banner.Text);
        Assert.Contains("can fix 1 of them", banner.Text);
    }

    [Fact]
    public void Audit_with_no_issues_repeats_p3a_ready_verdict()
    {
        using var harness = new DashboardHarness();
        harness.Controller.LoadAuditResults(harness.Write("audit.json", """{ "issues": [] }"""));

        var banner = StatusText.Banner(harness.Controller);

        Assert.Equal(BannerKind.Ready, banner.Kind);
        Assert.Contains("P3A's audit reported no problems", banner.Text);
    }

    [Fact]
    public void Supported_and_unsupported_issues_are_labelled_differently_from_failures()
    {
        using var harness = new DashboardHarness().LoadStandard();

        var supported = harness.Issue("TEST-ISSUE-A");
        var noFix = harness.Issue("TEST-ISSUE-NO-FIX");
        var unknown = harness.Issue("TEST-ISSUE-UNKNOWN");
        var incomplete = harness.Issue("TEST-ISSUE-INCOMPLETE");

        Assert.Equal(FixSupport.Supported, supported.Support);
        Assert.Equal("TEST-VERSION-1", supported.TargetVersion);
        Assert.Equal("Not started", StatusText.Status(supported));

        Assert.Equal(FixSupport.NotSupported, noFix.Support);
        Assert.Contains("no automatic remediation", noFix.SupportReason);
        Assert.Equal(FixSupport.NotSupported, unknown.Support);
        Assert.Contains("not defined", unknown.SupportReason);
        Assert.Equal(FixSupport.InvalidAuditData, incomplete.Support);
        Assert.Contains("toolId", incomplete.SupportReason);

        Assert.All([noFix, unknown, incomplete], item =>
        {
            Assert.Equal("No automatic fix", StatusText.Status(item));
            Assert.NotEqual(StatusText.Failed, StatusText.Status(item));
        });
    }

    [Fact]
    public void Without_a_manifest_support_cannot_be_decided()
    {
        using var harness = new DashboardHarness();
        harness.Controller.LoadAuditResults(harness.Write("audit.json", DashboardHarness.StandardAudit));

        Assert.All(
            harness.Controller.Issues.Where(i => i.Creation.IsValid),
            item => Assert.Equal(FixSupport.ManifestNeeded, item.Support));
    }

    [Fact]
    public async Task Successful_fix_updates_status_verification_reversals_and_saved_results()
    {
        using var harness = new DashboardHarness().LoadStandard();
        var item = harness.Issue("TEST-ISSUE-A");

        var run = await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(StatusText.Successful, StatusText.Status(item));
        Assert.Equal("Passed", StatusText.Verification(item.LastRun?.SmokeTest));
        Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));

        var banner = StatusText.Banner(harness.Controller);
        Assert.Equal(BannerKind.ReauditNeeded, banner.Kind);
        Assert.DoesNotContain("Ready:", banner.Text);

        using var saved = JsonDocument.Parse(File.ReadAllText(harness.ResultsPath));
        Assert.Equal(RemediationReport.ReadinessStatement, saved.RootElement.GetProperty("readiness").GetString());
        var report = Assert.Single(saved.RootElement.GetProperty("reports").EnumerateArray().ToArray());
        Assert.Equal("TEST-ISSUE-A", report.GetProperty("issueId").GetString());
        Assert.Equal("Succeeded", report.GetProperty("outcome").GetString());
        Assert.True(report.GetProperty("reauditRequired").GetBoolean());
    }

    [Fact]
    public async Task Failed_installation_is_labelled_failed()
    {
        using var harness = new DashboardHarness(new FakeExecutor { Mode = FakeMode.Fail }).LoadStandard();
        var item = harness.Issue("TEST-ISSUE-A");

        await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);

        Assert.Equal(StatusText.Failed, StatusText.Status(item));
        Assert.Contains("0x8A150011", item.LastRun?.Remediation.Message);
        Assert.Equal("Not run", StatusText.Verification(item.LastRun?.SmokeTest));
    }

    [Fact]
    public async Task Rejected_approval_changes_nothing_and_keeps_the_not_ready_banner()
    {
        using var harness = new DashboardHarness().LoadStandard();
        harness.Approvals.Approve = false;
        var item = harness.Issue("TEST-ISSUE-A");

        await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);

        Assert.Equal("Not approved", StatusText.Status(item));
        Assert.Equal(BannerKind.NotReady, StatusText.Banner(harness.Controller).Kind);
        Assert.Empty(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Progress_reports_waiting_for_approval_then_installing()
    {
        using var harness = new DashboardHarness().LoadStandard();
        var progress = new ListProgress();

        await harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-A"), progress, CancellationToken.None);

        Assert.Contains(progress.Messages, m => m.StartsWith("Waiting for your approval", StringComparison.Ordinal));
        Assert.Contains(progress.Messages, m => m.StartsWith("Installing and checking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_one_operation_runs_at_a_time_and_the_ui_can_see_it()
    {
        using var harness = new DashboardHarness(new FakeExecutor { Mode = FakeMode.WaitForRelease }).LoadStandard();
        var item = harness.Issue("TEST-ISSUE-A");

        var first = harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None);
        await harness.Executor.Started.Task.WaitAsync(Timeout);

        Assert.True(harness.Controller.IsBusy);
        Assert.Equal(StatusText.InProgress, StatusText.Status(item));
        Assert.Equal(BannerKind.InProgress, StatusText.Banner(harness.Controller).Kind);
        Assert.Null(await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => harness.Controller.LoadAuditResults(harness.PathOf("audit.json")));

        harness.Executor.Release.SetResult();
        await first.WaitAsync(Timeout);

        Assert.False(harness.Controller.IsBusy);
        Assert.Equal(StatusText.Successful, StatusText.Status(item));
    }

    [Fact]
    public async Task Cancelled_fix_is_labelled_cancelled()
    {
        using var harness = new DashboardHarness(new FakeExecutor { Mode = FakeMode.WaitForCancellation }).LoadStandard();
        var item = harness.Issue("TEST-ISSUE-A");
        using var cts = new CancellationTokenSource();

        var running = harness.Controller.RemediateAsync(item, new ListProgress(), cts.Token);
        await harness.Executor.Started.Task.WaitAsync(Timeout);
        await cts.CancelAsync();
        await running.WaitAsync(Timeout);

        Assert.Equal("Cancelled", StatusText.Status(item));
        Assert.False(harness.Controller.IsBusy);
    }

    [Fact]
    public async Task Unsupported_issue_cannot_be_started()
    {
        using var harness = new DashboardHarness().LoadStandard();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-NO-FIX"), new ListProgress(), CancellationToken.None));
    }

    [Fact]
    public async Task Confirmed_reversal_closes_the_record()
    {
        using var harness = new DashboardHarness().LoadStandard();
        await harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-A"), new ListProgress(), CancellationToken.None);
        var entry = Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
        var preview = await harness.Controller.PreviewReversalAsync(entry.EntryId, CancellationToken.None);

        var result = await harness.Controller.ReverseAsync(preview!, userConfirmed: true, CancellationToken.None);

        Assert.Equal(RollbackOutcome.Reversed, result.Outcome);
        Assert.Equal("Reversed", StatusText.Rollback(result.Outcome));
        Assert.Empty(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_reversal_is_never_reported_as_reversed_and_keeps_the_record()
    {
        using var harness = new DashboardHarness(reversalHandler: new FakeReversalHandler { WaitForCancellation = true }).LoadStandard();
        await harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-A"), new ListProgress(), CancellationToken.None);
        var entry = Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
        var preview = await harness.Controller.PreviewReversalAsync(entry.EntryId, CancellationToken.None);
        using var cts = new CancellationTokenSource();

        var running = harness.Controller.ReverseAsync(preview!, userConfirmed: true, cts.Token);
        await harness.ReversalHandler.Started.Task.WaitAsync(Timeout);
        await cts.CancelAsync();
        var result = await running.WaitAsync(Timeout);

        Assert.Equal(RollbackOutcome.Cancelled, result.Outcome);
        Assert.Contains("NOT", StatusText.Rollback(result.Outcome));
        Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Discarding_requires_acknowledgement()
    {
        using var harness = new DashboardHarness().LoadStandard();
        await harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-A"), new ListProgress(), CancellationToken.None);
        var entry = Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));

        Assert.False(await harness.Controller.DiscardReversalAsync(entry.EntryId, userAcknowledgedWarning: false, CancellationToken.None));
        Assert.Single(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
        Assert.True(await harness.Controller.DiscardReversalAsync(entry.EntryId, userAcknowledgedWarning: true, CancellationToken.None));
        Assert.Empty(await harness.Controller.GetPendingReversalsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Results_can_be_saved_for_p3a_to_a_chosen_file()
    {
        using var harness = new DashboardHarness().LoadStandard();
        await harness.Controller.RemediateAsync(harness.Issue("TEST-ISSUE-A"), new ListProgress(), CancellationToken.None);
        var path = harness.PathOf("chosen.json");

        await harness.Controller.SaveResultsAsync(path, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.True(harness.Controller.HasResults);
    }

    [Fact]
    public void Unreadable_audit_file_is_reported_and_keeps_the_previous_issues()
    {
        using var harness = new DashboardHarness().LoadStandard();

        var result = harness.Controller.LoadAuditResults(harness.Write("bad.json", "{ not json"));

        Assert.False(result.IsLoaded);
        Assert.Contains("not in the expected format", result.Description);
        Assert.Equal(4, harness.Controller.Issues.Count);
    }
}
