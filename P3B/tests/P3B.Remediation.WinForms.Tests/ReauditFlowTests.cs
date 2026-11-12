using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;
using P3B.Remediation.WinForms.Presentation;

namespace P3B.Remediation.WinForms.Tests;

/// <summary>
/// The joint audit → fix → re-audit flow (P3-StR4.1, P3-AC4.1, SRS FR-004) with a fake P3A
/// auditor: the dashboard's Ready / Not Ready always comes from P3A's audit, kept apart from
/// P3B's own remediation and smoke-test results.
/// </summary>
public class ReauditFlowTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly AuditIssue IssueA = new() { IssueId = "TEST-ISSUE-A", ToolId = "TEST-TOOL-A", State = "missing", Detail = "TEST not found" };

    /// <summary>P3A's format with fake values: one tool with manual steps, one with none.</summary>
    private const string P3AFormatManifest = """
        {
          "manifestVersion": "1.0",
          "tools": [
            { "name": "Test manual tool", "type": "test-manual", "minimumApprovedVersion": "1.0", "maximumApprovedVersion": "2.0",
              "remediation": { "downloadUrl": "https://example.invalid/test-manual", "instructions": [ "TEST step 1", "TEST step 2" ] } },
            { "name": "Test tool with no fix", "type": "test-none" }
          ]
        }
        """;

    private static ReadinessAudit NotReady(params AuditIssue[] issues) => new(
        ReadinessVerdict.NotReady,
        "TEST audit completed. Overall Environment: Not Ready",
        DateTimeOffset.Now,
        issues,
        [new ReadinessComponent("TEST component", "Not Ready", "Not Installed", "Not Available", "TEST reason")]);

    private static ReadinessAudit Ready() => new(
        ReadinessVerdict.Ready,
        "TEST audit completed. Overall Environment: Ready",
        DateTimeOffset.Now,
        [],
        [new ReadinessComponent("TEST component", "Ready", "Detected", "TEST-VERSION-1", null)]);

    private static async Task<DashboardHarness> AuditedHarness(FakeReadinessAuditor auditor, FakeExecutor? executor = null)
    {
        var harness = new DashboardHarness(executor, auditor: auditor);
        harness.Controller.OpenManifest(harness.Write("manifest.json", DashboardHarness.StandardManifest));
        await harness.Controller.RunP3AAuditAsync(null, CancellationToken.None).WaitAsync(Timeout);
        return harness;
    }

    [Fact]
    public async Task Problem_then_fix_then_P3A_reaudit_reports_Ready()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA), Ready());
        using var harness = await AuditedHarness(auditor);

        Assert.Equal(BannerKind.NotReady, StatusText.Banner(harness.Controller).Kind);
        Assert.Equal(BannerKind.NotReady, StatusText.P3AReadiness(harness.Controller).Kind);
        var item = Assert.Single(harness.Controller.Issues);
        Assert.Equal(FixSupport.Supported, item.Support);

        var run = await harness.Controller.RemediateAsync(item, new ListProgress(), CancellationToken.None).WaitAsync(Timeout);

        // P3B's own results...
        Assert.Equal(RemediationOutcome.Succeeded, run!.Remediation.Outcome);
        Assert.Equal(SmokeTestOutcome.Passed, run.SmokeTest!.Outcome);

        // ...and, separately, P3A's re-audit, which alone decides readiness.
        Assert.Equal(2, auditor.Calls);
        Assert.Equal(ReadinessVerdict.Ready, harness.Controller.LastP3AAudit!.Verdict);
        Assert.Empty(harness.Controller.Issues);
        Assert.False(harness.Controller.AnyChangeAttempted);
        var banner = StatusText.Banner(harness.Controller);
        Assert.Equal(BannerKind.Ready, banner.Kind);
        Assert.StartsWith(StatusText.Ready, banner.Text);
        Assert.Contains("P3A's audit", banner.Text);
        Assert.StartsWith("P3A overall environment: Ready", StatusText.P3AReadiness(harness.Controller).Text);
        Assert.True(harness.Controller.HasResults);
    }

    [Fact]
    public async Task Fix_that_P3A_still_rejects_stays_Not_Ready_and_keeps_P3B_result_visible()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA), NotReady(IssueA));
        using var harness = await AuditedHarness(auditor);

        await harness.Controller.RemediateAsync(harness.Controller.Issues[0], new ListProgress(), CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(2, auditor.Calls);
        Assert.Equal(ReadinessVerdict.NotReady, harness.Controller.LastP3AAudit!.Verdict);
        Assert.Equal(BannerKind.NotReady, StatusText.Banner(harness.Controller).Kind);
        Assert.StartsWith("P3A overall environment: Not Ready", StatusText.P3AReadiness(harness.Controller).Text);

        // P3B reports its own success, but never turns it into a Ready verdict.
        var item = Assert.Single(harness.Controller.Issues);
        Assert.Equal(IssueStatus.Successful, item.Status);
        Assert.Equal(RemediationOutcome.Succeeded, item.LastRun!.Remediation.Outcome);
        Assert.StartsWith(StatusText.NotReady + ":", StatusText.Banner(harness.Controller).Text);
    }

    [Fact]
    public async Task Failed_fix_is_also_reaudited_by_P3A()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA), NotReady(IssueA));
        using var harness = await AuditedHarness(auditor, new FakeExecutor { Mode = FakeMode.Fail });

        var run = await harness.Controller.RemediateAsync(harness.Controller.Issues[0], new ListProgress(), CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(RemediationOutcome.Failed, run!.Remediation.Outcome);
        Assert.Equal(2, auditor.Calls);
        Assert.Equal(IssueStatus.Failed, harness.Controller.Issues[0].Status);
    }

    [Fact]
    public async Task Rejected_fix_changes_nothing_so_P3A_is_not_rerun()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA));
        using var harness = await AuditedHarness(auditor);
        harness.Approvals.Approve = false;

        await harness.Controller.RemediateAsync(harness.Controller.Issues[0], new ListProgress(), CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(1, auditor.Calls);
        Assert.Equal(BannerKind.NotReady, StatusText.Banner(harness.Controller).Kind);
    }

    [Fact]
    public async Task If_the_reaudit_cannot_run_the_dashboard_asks_for_one_instead_of_guessing()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA), ReadinessAudit.CouldNotRun("TEST manifest missing", DateTimeOffset.Now));
        using var harness = await AuditedHarness(auditor);

        await harness.Controller.RemediateAsync(harness.Controller.Issues[0], new ListProgress(), CancellationToken.None).WaitAsync(Timeout);

        Assert.True(harness.Controller.AnyChangeAttempted);
        Assert.Equal(BannerKind.ReauditNeeded, StatusText.Banner(harness.Controller).Kind);
        Assert.Contains("could not run", StatusText.P3AReadiness(harness.Controller).Text);
    }

    [Fact]
    public async Task Auditor_exception_is_shown_as_could_not_run()
    {
        using var harness = new DashboardHarness(auditor: new FakeReadinessAuditor { Throw = true });
        harness.Controller.OpenManifest(harness.Write("manifest.json", DashboardHarness.StandardManifest));

        var audit = await harness.Controller.RunP3AAuditAsync(null, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(ReadinessVerdict.CouldNotRun, audit!.Verdict);
        Assert.Contains("TEST auditor failure", audit.Summary);
        var banner = StatusText.Banner(harness.Controller);
        Assert.Equal(BannerKind.Neutral, banner.Kind);
        Assert.DoesNotContain(StatusText.Ready, banner.Text);
    }

    [Fact]
    public async Task Manual_steps_issue_shows_the_manifest_steps_then_P3A_rechecks_it()
    {
        var manual = new AuditIssue { IssueId = "test-manual/NotInstalled", ToolId = "test-manual", State = "missing" };
        var none = new AuditIssue { IssueId = "test-none/NotInstalled", ToolId = "test-none", State = "missing" };
        var auditor = new FakeReadinessAuditor(NotReady(manual, none), NotReady(none));
        using var harness = new DashboardHarness(auditor: auditor);
        harness.Controller.OpenManifest(harness.Write("eda-tool-manifest.json", P3AFormatManifest));

        await harness.Controller.RunP3AAuditAsync(null, CancellationToken.None).WaitAsync(Timeout);

        var manualItem = harness.Controller.Issues.Single(i => i.Key == "test-manual/NotInstalled");
        Assert.Equal(FixSupport.ManualSteps, manualItem.Support);
        Assert.Equal("Manual steps", StatusText.Support(manualItem.Support));
        Assert.Equal("Test manual tool", manualItem.ToolLabel);
        Assert.Equal("1.0 to 2.0", manualItem.ApprovedText);
        Assert.Equal("https://example.invalid/test-manual", manualItem.Guidance!.DownloadUrl);
        Assert.Equal(new[] { "TEST step 1", "TEST step 2" }, manualItem.Guidance.Steps);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Controller.RemediateAsync(manualItem, new ListProgress(), CancellationToken.None));

        var noneItem = harness.Controller.Issues.Single(i => i.Key == "test-none/NotInstalled");
        Assert.Equal(FixSupport.NotSupported, noneItem.Support);
        Assert.Contains("1 have manual steps; 1 have no automatic fix", StatusText.Banner(harness.Controller).Text);

        // The user follows the steps, then asks P3A to check again.
        await harness.Controller.RunP3AAuditAsync(null, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(2, auditor.Calls);
        Assert.Equal("test-none/NotInstalled", Assert.Single(harness.Controller.Issues).Key);
    }

    [Fact]
    public async Task P3A_audit_is_not_started_while_a_fix_is_running()
    {
        var auditor = new FakeReadinessAuditor(NotReady(IssueA), Ready());
        var executor = new FakeExecutor { Mode = FakeMode.WaitForRelease };
        using var harness = await AuditedHarness(auditor, executor);

        var fixing = harness.Controller.RemediateAsync(harness.Controller.Issues[0], new ListProgress(), CancellationToken.None);
        await executor.Started.Task.WaitAsync(Timeout);

        Assert.Null(await harness.Controller.RunP3AAuditAsync(null, CancellationToken.None));
        Assert.Equal(1, auditor.Calls);

        executor.Release.SetResult();
        await fixing.WaitAsync(Timeout);
        Assert.Equal(2, auditor.Calls);
    }

    [Fact]
    public async Task Without_P3A_the_panel_says_the_audit_is_unavailable_and_never_shows_a_verdict()
    {
        using var harness = new DashboardHarness();

        var panel = StatusText.P3AReadiness(harness.Controller);

        Assert.False(harness.Controller.CanRunP3AAudit);
        Assert.Equal(BannerKind.Neutral, panel.Kind);
        Assert.DoesNotContain(StatusText.Ready, panel.Text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Controller.RunP3AAuditAsync(null, CancellationToken.None));
    }
}

/// <summary>Stands in for P3A's audit; returns scripted results and detects nothing.</summary>
internal sealed class FakeReadinessAuditor : IReadinessAuditor
{
    private readonly Queue<ReadinessAudit> _results;
    private ReadinessAudit? _last;

    public FakeReadinessAuditor(params ReadinessAudit[] results) => _results = new Queue<ReadinessAudit>(results);

    public bool Throw { get; init; }

    public int Calls { get; private set; }

    public Task<ReadinessAudit> RunAuditAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Throw)
        {
            throw new InvalidOperationException("TEST auditor failure");
        }

        // Repeats the last scripted result if asked more often than scripted.
        _last = _results.Count > 0 ? _results.Dequeue() : _last ?? throw new InvalidOperationException("TEST: no audit result scripted.");
        return Task.FromResult(_last);
    }
}
