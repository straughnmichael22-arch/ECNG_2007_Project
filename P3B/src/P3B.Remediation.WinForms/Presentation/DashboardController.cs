using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.WinForms.Composition;

namespace P3B.Remediation.WinForms.Presentation;

/// <summary>
/// The dashboard's workflow, without any WinForms controls so it can be unit tested:
/// load audit results and manifest, decide which issues are supported, remediate one issue at a
/// time off the UI thread, manage reversal records, and save results for P3A's re-audit.
/// </summary>
public sealed class DashboardController
{
    private readonly SwitchableManifestSource _manifest;
    private readonly IRemediationService _remediation;
    private readonly IReadOnlyList<IRemediationExecutor> _executors;
    private readonly RollbackService _rollback;
    private readonly IReversalJournal _journal;
    private readonly ProgressApprovalProvider _approvals;
    private readonly OperationGate _gate;
    private readonly TimeProvider _time;
    private readonly IReadinessAuditor? _auditor;
    private readonly List<IssueItem> _issues = [];
    private readonly Dictionary<string, RemediationReport> _reports = new(StringComparer.Ordinal);
    private volatile bool _running;

    public DashboardController(
        SwitchableManifestSource manifest,
        IRemediationService remediation,
        IReadOnlyList<IRemediationExecutor> executors,
        RollbackService rollback,
        IReversalJournal journal,
        ProgressApprovalProvider approvals,
        OperationGate gate,
        string resultsPath,
        TimeProvider? time = null,
        IReadinessAuditor? readinessAuditor = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(remediation);
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(rollback);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsPath);

        _manifest = manifest;
        _remediation = remediation;
        _executors = executors;
        _rollback = rollback;
        _journal = journal;
        _approvals = approvals;
        _gate = gate;
        ResultsPath = resultsPath;
        _time = time ?? TimeProvider.System;
        _auditor = readinessAuditor;
        ManifestStatus = _manifest.Load();
    }

    public IReadOnlyList<IssueItem> Issues => _issues;

    public ManifestLoadResult ManifestStatus { get; private set; }

    public string? ManifestPath => _manifest.ManifestPath;

    public AuditResultsLoad? AuditStatus { get; private set; }

    /// <summary>The audit results file currently shown, if the issues came from a file.</summary>
    public string? AuditPath { get; private set; }

    /// <summary>True once something was attempted that may have changed the computer.</summary>
    public bool AnyChangeAttempted { get; private set; }

    /// <summary>True while a remediation or reversal is running (P3B-PR-01).</summary>
    public bool IsBusy => _running || _gate.IsBusy;

    public string? CurrentActivity { get; private set; }

    /// <summary>Where results are saved automatically for P3A to re-audit from.</summary>
    public string ResultsPath { get; }

    /// <summary>Set if the last automatic save of results failed.</summary>
    public string? LastSaveProblem { get; private set; }

    public bool HasResults => _reports.Count > 0;

    /// <summary>True when P3A's audit can be run from the dashboard.</summary>
    public bool CanRunP3AAudit => _auditor is not null;

    /// <summary>
    /// The latest run of P3A's audit. Its verdict is the only Ready / Not Ready the dashboard
    /// shows; P3B never decides readiness itself.
    /// </summary>
    public ReadinessAudit? LastP3AAudit { get; private set; }

    public ManifestLoadResult OpenManifest(string path)
    {
        ThrowIfBusy();
        _manifest.UseFile(path);
        RefreshSupport();
        return ManifestStatus;
    }

    public AuditResultsLoad LoadAuditResults(string path)
    {
        ThrowIfBusy();
        var load = AuditResultsFile.Load(path);
        if (load.IsLoaded)
        {
            ReceiveAuditIssues(load.Issues);
            AuditPath = Path.GetFullPath(path);
        }

        AuditStatus = load.IsLoaded ? load : AuditStatus ?? load;
        return load;
    }

    /// <summary>Receives audit issues directly, for example from a P3A adapter.</summary>
    public void ReceiveAuditIssues(IEnumerable<AuditIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ThrowIfBusy();

        _issues.Clear();
        _issues.AddRange(issues.Select((issue, index) => new IssueItem(issue, index + 1)));
        _reports.Clear();
        AnyChangeAttempted = false;
        LastP3AAudit = null;
        AuditPath = null;
        AuditStatus = new AuditResultsLoad(true, $"Received {_issues.Count} issue(s).", []);
        RefreshSupport();
    }

    /// <summary>
    /// Runs P3A's audit and shows its findings and its Ready / Not Ready verdict. Returns null if
    /// another operation is running.
    /// </summary>
    public async Task<ReadinessAudit?> RunP3AAuditAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (_auditor is null)
        {
            throw new InvalidOperationException("P3A's audit is not available in this dashboard.");
        }

        if (IsBusy)
        {
            return null;
        }

        _running = true;
        try
        {
            return await RunP3AAuditCoreAsync(progress, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<ReadinessAudit> RunP3AAuditCoreAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        CurrentActivity = "running P3A's audit";
        progress?.Report("Running P3A's audit. This usually takes a few seconds.");

        ReadinessAudit audit;
        try
        {
            audit = await Task.Run(() => _auditor!.RunAuditAsync(cancellationToken), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            audit = ReadinessAudit.CouldNotRun("P3A's audit was cancelled before it started.", _time.GetLocalNow());
        }
        catch (Exception ex)
        {
            audit = ReadinessAudit.CouldNotRun($"P3A's audit stopped with an error: {ex.Message}", _time.GetLocalNow());
        }
        finally
        {
            CurrentActivity = null;
        }

        LastP3AAudit = audit;
        if (audit.HasVerdict)
        {
            ApplyP3AFindings(audit);
        }

        progress?.Report(audit.HasVerdict
            ? $"P3A's audit finished: {StatusText.Verdict(audit.Verdict)}."
            : $"P3A's audit could not run: {audit.Summary}");
        return audit;
    }

    /// <summary>
    /// Replaces the issue list with P3A's latest findings. P3B's own result is kept for any issue
    /// P3A still reports, so a fix that did not make the tool pass stays visible.
    /// </summary>
    private void ApplyP3AFindings(ReadinessAudit audit)
    {
        var previous = _issues.ToDictionary(i => i.Key, StringComparer.Ordinal);
        _issues.Clear();
        _issues.AddRange(audit.Issues.Select((issue, index) => new IssueItem(issue, index + 1)));
        foreach (var item in _issues)
        {
            if (previous.TryGetValue(item.Key, out var earlier))
            {
                item.CarryOver(earlier);
            }
        }

        AuditPath = null;
        AuditStatus = new AuditResultsLoad(true, audit.Summary, audit.Issues);
        AnyChangeAttempted = false;
        RefreshSupport();
    }

    /// <summary>Re-reads the manifest and decides, for each issue, whether P3B can fix it.</summary>
    public void RefreshSupport()
    {
        ManifestStatus = _manifest.Load();
        foreach (var item in _issues)
        {
            Evaluate(item);
        }
    }

    public async Task<RemediationRunResult?> RemediateAsync(IssueItem item, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progress);

        var request = item.Creation.Request;
        if (item.Support != FixSupport.Supported || request is null)
        {
            throw new InvalidOperationException("This issue has no supported automatic fix.");
        }

        if (IsBusy)
        {
            return null;
        }

        _running = true;
        var previousStatus = item.Status;
        item.Status = IssueStatus.InProgress;
        CurrentActivity = $"fixing {item.ToolLabel}";
        progress.Report($"Preparing to fix {item.ToolLabel}.");
        _approvals.Progress = progress;

        RemediationRunResult run;
        try
        {
            // Off the UI thread: the window stays responsive during long installations.
            run = await Task.Run(() => _remediation.RemediateAsync(request, cancellationToken), CancellationToken.None);
        }
        catch
        {
            item.Status = previousStatus;
            throw;
        }
        finally
        {
            _approvals.Progress = null;
            CurrentActivity = null;
            _running = false;
        }

        var outcome = run.Remediation.Outcome;
        item.LastRun = run;
        item.Status = outcome == RemediationOutcome.Busy ? previousStatus : StatusText.ToIssueStatus(outcome);

        if (outcome is RemediationOutcome.Succeeded or RemediationOutcome.Failed or RemediationOutcome.Cancelled)
        {
            AnyChangeAttempted = true;
        }

        if (outcome != RemediationOutcome.Busy)
        {
            _reports[item.Key] = RemediationReport.From(run);
            await SaveResultsQuietlyAsync().ConfigureAwait(true);
        }

        // Audit → fix → re-audit: after anything that may have changed the computer, P3A decides.
        if (AnyChangeAttempted && _auditor is not null && outcome is RemediationOutcome.Succeeded or RemediationOutcome.Failed or RemediationOutcome.Cancelled)
        {
            _running = true;
            try
            {
                await RunP3AAuditCoreAsync(progress, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                _running = false;
            }
        }

        return run;
    }

    public Task<IReadOnlyList<ReversalJournalEntry>> GetPendingReversalsAsync(CancellationToken cancellationToken) =>
        _journal.GetPendingAsync(cancellationToken);

    public Task<RollbackPreview?> PreviewReversalAsync(Guid entryId, CancellationToken cancellationToken) =>
        _rollback.PreviewAsync(entryId, cancellationToken);

    public async Task<RollbackResult> ReverseAsync(RollbackPreview preview, bool userConfirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (IsBusy)
        {
            return new RollbackResult(preview.EntryId, RollbackOutcome.Busy, "Another operation is in progress.");
        }

        _running = true;
        CurrentActivity = $"reversing a change to {preview.ToolId}";
        try
        {
            var result = await Task.Run(() => _rollback.ReverseAsync(preview, userConfirmed, cancellationToken), CancellationToken.None);
            if (result.Outcome != RollbackOutcome.NotConfirmed)
            {
                AnyChangeAttempted = true;
            }

            return result;
        }
        finally
        {
            CurrentActivity = null;
            _running = false;
        }
    }

    public Task<bool> DiscardReversalAsync(Guid entryId, bool userAcknowledgedWarning, CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        return _journal.DiscardAsync(entryId, userAcknowledgedWarning, cancellationToken);
    }

    /// <summary>Saves the latest result for every remediated issue, for P3A's re-audit.</summary>
    public Task SaveResultsAsync(string path, CancellationToken cancellationToken) =>
        RemediationResultsFile.WriteAsync(path, _reports.Values, _time.GetUtcNow(), cancellationToken);

    private async Task SaveResultsQuietlyAsync()
    {
        try
        {
            await SaveResultsAsync(ResultsPath, CancellationToken.None).ConfigureAwait(true);
            LastSaveProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveProblem = $"Results could not be saved to {ResultsPath}: {ex.Message}";
        }
    }

    private void Evaluate(IssueItem item)
    {
        item.TargetVersion = null;
        item.Guidance = null;

        // Names and approved versions come from the manifest even when P3A's data is incomplete.
        var tool = ManifestStatus.IsUsable && !string.IsNullOrWhiteSpace(item.Issue.ToolId)
            ? ManifestStatus.Manifest.FindTool(new ToolReference(item.Issue.ToolId))
            : null;
        item.ToolName = tool?.Name;
        item.ApprovedText = tool?.ApprovedVersionText;

        var request = item.Creation.Request;
        if (request is null)
        {
            Set(item, FixSupport.InvalidAuditData, $"P3A's information about this issue is incomplete: {string.Join(" ", item.Creation.Problems)}");
            return;
        }

        if (!ManifestStatus.IsUsable)
        {
            Set(item, FixSupport.ManifestNeeded, $"Open a valid manifest to see whether P3B can fix this. {ManifestStatus.Description}");
            return;
        }

        var plan = RemediationPlanner.Plan(request, ManifestStatus.Manifest);
        if (!plan.IsSupported)
        {
            item.Guidance = plan.Guidance;
            Set(
                item,
                plan.Guidance is null ? FixSupport.NotSupported : FixSupport.ManualSteps,
                plan.UnsupportedReason ?? "No supported automatic fix is available.");
            return;
        }

        RemediationAction action = plan.Action;
        if (!_executors.Any(e => e.CanHandle(action)))
        {
            Set(item, FixSupport.NotSupported, $"P3B has no automatic installer for the '{action.Method}' method.");
            return;
        }

        item.TargetVersion = action.TargetVersion;
        Set(item, FixSupport.Supported, $"P3B can install the approved version {action.TargetVersion} of {action.Tool.Name} for you.");
    }

    private static void Set(IssueItem item, FixSupport support, string reason)
    {
        item.Support = support;
        item.SupportReason = reason;
    }

    private void ThrowIfBusy()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Wait for the current operation to finish.");
        }
    }
}
