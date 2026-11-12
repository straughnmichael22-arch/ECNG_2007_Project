using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;

namespace P3B.Remediation.Core.Verification;

/// <summary>
/// Runs the smoke checks the manifest declares for the remediated tool, in manifest order, and
/// produces a structured <see cref="SmokeTestResult"/>. It verifies only that tool: overall
/// environment readiness is P3A's re-audit, never this service's conclusion.
/// </summary>
/// <remarks>
/// Overall outcome: Failed if any check failed; otherwise TimedOut if any timed out; otherwise
/// Unsupported if any could not be run (or none are declared); Passed only if every check passed.
/// </remarks>
public sealed class SmokeTestService : ISmokeTestRunner
{
    public static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ProcessTimeoutMargin = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<ISmokeCheckHandler> _handlers;
    private readonly TimeSpan _checkTimeout;
    private readonly TimeProvider _time;

    /// <param name="checkTimeout">Upper limit for any one check; version commands get at least their own timeout plus a margin.</param>
    public SmokeTestService(IEnumerable<ISmokeCheckHandler> handlers, TimeSpan? checkTimeout = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToArray();
        _checkTimeout = checkTimeout ?? DefaultCheckTimeout;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SmokeTestResult> RunAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var action = preview.Action;
        var checks = action.Tool.SmokeChecks;

        if (checks.Count == 0)
        {
            return Result(
                preview,
                SmokeTestOutcome.Unsupported,
                $"The manifest defines no smoke tests for {action.Tool.Name}, so P3B could not verify it beyond the installation check.",
                []);
        }

        var results = new List<SmokeCheckResult>();
        try
        {
            foreach (var check in checks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await RunCheckAsync(check, action, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(
                preview,
                SmokeTestOutcome.Cancelled,
                $"The smoke tests were cancelled after {results.Count} of {checks.Count} check(s); {action.Tool.Name} is not verified.",
                results);
        }

        var outcome = Combine(results);
        return Result(preview, outcome, Summarise(outcome, results, action), results);
    }

    private async Task<SmokeCheckResult> RunCheckAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        if (check is UnrecognisedSmokeCheck)
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Unsupported, $"P3B does not recognise the smoke test kind '{check.Kind}' declared in the manifest.");
        }

        var handlers = _handlers.Where(h => h.CanHandle(check)).ToArray();
        if (handlers.Length != 1)
        {
            return SmokeCheckResult.For(
                check,
                SmokeCheckStatus.Unsupported,
                handlers.Length == 0
                    ? $"P3B has no way to run a '{check.Kind}' check on this computer."
                    : $"More than one handler claims '{check.Kind}' checks, so none was run.");
        }

        var limit = check is VersionCommandCheck command && command.Timeout + ProcessTimeoutMargin > _checkTimeout
            ? command.Timeout + ProcessTimeoutMargin
            : _checkTimeout;

        using var timeout = new CancellationTokenSource(limit, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await handlers[0].RunAsync(check, action, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.TimedOut, $"The check did not finish within {limit.TotalSeconds:0} seconds, so nothing was concluded from it.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"The check could not run: {ex.Message}");
        }
    }

    private static SmokeTestOutcome Combine(IReadOnlyCollection<SmokeCheckResult> results) =>
        results.Any(r => r.Status == SmokeCheckStatus.Failed) ? SmokeTestOutcome.Failed
        : results.Any(r => r.Status == SmokeCheckStatus.TimedOut) ? SmokeTestOutcome.TimedOut
        : results.Any(r => r.Status == SmokeCheckStatus.Unsupported) ? SmokeTestOutcome.Unsupported
        : SmokeTestOutcome.Passed;

    private static string Summarise(SmokeTestOutcome outcome, IReadOnlyCollection<SmokeCheckResult> results, RemediationAction action)
    {
        var subject = $"{action.Tool.Name} {action.TargetVersion}";
        if (outcome == SmokeTestOutcome.Passed)
        {
            return $"All {results.Count} smoke test(s) passed for {subject}.";
        }

        var problems = results.Where(r => r.Status != SmokeCheckStatus.Passed).ToArray();
        return $"{problems.Length} of {results.Count} smoke test(s) did not pass for {subject}: "
            + string.Join(" ", problems.Select(p => $"[{p.Status}] {p.Description} {p.Explanation}"));
    }

    private SmokeTestResult Result(ActionPreview preview, SmokeTestOutcome outcome, string message, IEnumerable<SmokeCheckResult> checks) => new()
    {
        PreviewId = preview.PreviewId,
        Tool = preview.Tool,
        TargetVersion = preview.Action.TargetVersion,
        Outcome = outcome,
        Message = message,
        Checks = checks.ToArray(),
        CompletedAt = _time.GetUtcNow(),
    };
}
