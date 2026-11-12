using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Execution;

/// <summary>
/// Runs the remediation workflow for one request: manifest load, planning, executor selection,
/// preview, explicit approval, execution, reversal recording and smoke test.
/// </summary>
public sealed class RemediationService : IRemediationService
{
    public const string UnsupportedMessage = "No supported automatic fix is available for this issue.";
    public const string BusyMessage = "Another remediation action is already in progress.";

    private readonly IManifestSource _manifest;
    private readonly IReadOnlyList<IRemediationExecutor> _executors;
    private readonly ISmokeTestRunner _smokeTests;
    private readonly IApprovalProvider _approvals;
    private readonly IReversalJournal _journal;
    private readonly OperationGate _gate;
    private readonly TimeProvider _time;

    /// <param name="gate">Share with <see cref="RollbackService"/> so remediation and rollback never overlap.</param>
    public RemediationService(
        IManifestSource manifest,
        IEnumerable<IRemediationExecutor> executors,
        ISmokeTestRunner smokeTests,
        IApprovalProvider approvals,
        IReversalJournal journal,
        OperationGate? gate = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(smokeTests);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(journal);

        _manifest = manifest;
        _executors = executors.ToArray();
        _smokeTests = smokeTests;
        _approvals = approvals;
        _journal = journal;
        _gate = gate ?? new OperationGate();
        _time = time ?? TimeProvider.System;
    }

    public bool IsBusy => _gate.IsBusy;

    public async Task<AuditRemediationResult> RemediateAsync(AuditIssue issue, CancellationToken cancellationToken = default)
    {
        // Workflow A-B: receive the audit state and identify the tool and originating issue.
        var created = RemediationRequestFactory.FromAuditIssue(issue);
        if (!created.IsValid)
        {
            return AuditRemediationResult.InvalidAuditIssue(created.Problems);
        }

        var run = await RemediateAsync(created.Request, cancellationToken).ConfigureAwait(false);
        return AuditRemediationResult.Completed(run);
    }

    public async Task<RemediationRunResult> RemediateAsync(
        RemediationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = _time.GetUtcNow();

        // Workflow I / P3B-PR-01: one active action at a time. A second request is rejected, not queued.
        using var lease = _gate.TryAcquire();
        if (lease is null)
        {
            return Finish(request, startedAt, RemediationOutcome.Busy, BusyMessage);
        }

        return await RunExclusiveAsync(request, startedAt, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemediationRunResult> RunExclusiveAsync(
        RemediationRequest request,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        // Workflow C / 3B-IR-003: report a missing, unreadable or invalid manifest before anything depends on it.
        var manifest = _manifest.Load();
        if (!manifest.IsUsable)
        {
            return Finish(request, startedAt, RemediationOutcome.InvalidConfiguration, manifest.Description);
        }

        // Workflow D-E: is there an approved action, and what is the approved target version?
        var plan = RemediationPlanner.Plan(request, manifest.Manifest);
        if (!plan.IsSupported)
        {
            return Finish(request, startedAt, RemediationOutcome.Unsupported, $"{UnsupportedMessage} {plan.UnsupportedReason}");
        }

        RemediationAction action = plan.Action;
        var candidates = _executors.Where(e => e.CanHandle(action)).ToArray();
        if (candidates.Length == 0)
        {
            return Finish(
                request,
                startedAt,
                RemediationOutcome.Unsupported,
                $"{UnsupportedMessage} No executor is available for the '{action.Method}' remediation method.",
                action);
        }

        if (candidates.Length > 1)
        {
            return Finish(
                request,
                startedAt,
                RemediationOutcome.InvalidConfiguration,
                $"{candidates.Length} executors claim this action; exactly one is required.",
                action);
        }

        var executor = candidates[0];

        // Workflow F: proposed plan.
        ActionPreview preview;
        try
        {
            preview = executor.CreatePreview(action);
        }
        catch (Exception ex)
        {
            return Finish(request, startedAt, RemediationOutcome.Failed, $"Could not prepare the action preview: {ex.Message}", action);
        }

        if (preview.Action.ActionId != action.ActionId)
        {
            return Finish(request, startedAt, RemediationOutcome.Failed, "The action preview does not match the planned action.", action);
        }

        // Workflow G-H: show tool, target version, changes and reversal method; get explicit approval.
        ApprovalDecision? decision;
        try
        {
            decision = await _approvals.RequestApprovalAsync(preview, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Finish(request, startedAt, RemediationOutcome.Cancelled, "Cancelled before approval was given. Nothing was changed.", action, preview);
        }

        if (decision is null || !decision.Approves(preview))
        {
            return Finish(
                request,
                startedAt,
                RemediationOutcome.Rejected,
                decision?.Reason ?? "The action was not approved, so nothing was changed.",
                action,
                preview);
        }

        // Workflow I-J: execute and capture the result.
        ExecutionReport report;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            report = await executor.ExecuteAsync(preview, decision, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Finish(request, startedAt, RemediationOutcome.Cancelled, "The action was cancelled.", action, preview);
        }
        catch (Exception ex)
        {
            return Finish(request, startedAt, RemediationOutcome.Failed, $"The action failed: {ex.Message}", action, preview);
        }

        // Workflow K: record changes and reversal information, whatever the outcome.
        var (entryId, journalWarning) = await RecordReversalAsync(report.Reversal).ConfigureAwait(false);
        var message = journalWarning is null ? report.Message : $"{report.Message} {journalWarning}";
        var outcome = report.Status switch
        {
            ExecutionStatus.Succeeded => RemediationOutcome.Succeeded,
            ExecutionStatus.Cancelled => RemediationOutcome.Cancelled,
            _ => RemediationOutcome.Failed,
        };

        // Workflow L: return the result; smoke tests only follow a verified success.
        var run = Finish(request, startedAt, outcome, message, action, preview, report, entryId);
        if (outcome != RemediationOutcome.Succeeded)
        {
            return run;
        }

        var smokeTest = await RunSmokeTestAsync(preview, cancellationToken).ConfigureAwait(false);
        return run with { SmokeTest = smokeTest };
    }

    private async Task<(Guid? EntryId, string? Warning)> RecordReversalAsync(ReversalInfo? reversal)
    {
        if (reversal is null || reversal.Kind == ReversalKind.NothingToReverse)
        {
            return (null, null);
        }

        try
        {
            // Not cancellable: once something has changed, its reversal information must be kept.
            var entry = await _journal.RecordAsync(reversal, CancellationToken.None).ConfigureAwait(false);
            return (entry.EntryId, null);
        }
        catch (Exception ex)
        {
            return (null, $"WARNING: the reversal information could not be saved ({ex.Message}). Keep a note of it: {reversal.Method}");
        }
    }

    private async Task<SmokeTestResult> RunSmokeTestAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        // A smoke-test problem is reported in the smoke-test result only; it never changes
        // the remediation outcome (3B-UR-002).
        try
        {
            return await _smokeTests.RunAsync(preview, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SmokeTest(preview, SmokeTestOutcome.Cancelled, "The smoke test was cancelled.");
        }
        catch (Exception ex)
        {
            return SmokeTest(preview, SmokeTestOutcome.Failed, $"The smoke test could not run: {ex.Message}");
        }
    }

    private SmokeTestResult SmokeTest(ActionPreview preview, SmokeTestOutcome outcome, string message) => new()
    {
        PreviewId = preview.PreviewId,
        Tool = preview.Tool,
        Outcome = outcome,
        Message = message,
        CompletedAt = _time.GetUtcNow(),
    };

    private RemediationRunResult Finish(
        RemediationRequest request,
        DateTimeOffset startedAt,
        RemediationOutcome outcome,
        string message,
        RemediationAction? action = null,
        ActionPreview? preview = null,
        ExecutionReport? report = null,
        Guid? journalEntryId = null) =>
        new(
            new RemediationResult
            {
                Request = request,
                Outcome = outcome,
                Message = message,
                Action = action,
                Preview = preview,
                PriorState = report?.PriorState,
                ObservedChanges = report?.ObservedChanges ?? Array.Empty<string>(),
                Reversal = report?.Reversal,
                ReversalJournalEntryId = journalEntryId,
                StartedAt = startedAt,
                CompletedAt = _time.GetUtcNow(),
            },
            SmokeTest: null);
}
