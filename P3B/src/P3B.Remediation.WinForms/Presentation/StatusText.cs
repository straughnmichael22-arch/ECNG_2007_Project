using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.WinForms.Presentation;

public enum BannerKind
{
    Neutral,
    Ready,
    NotReady,
    InProgress,
    ReauditNeeded,
}

public sealed record Banner(string Text, BannerKind Kind);

/// <summary>
/// Every label the dashboard shows, in plain language. "Ready" and "Not Ready" only ever repeat
/// P3A's audit; after any change P3B asks for a re-audit instead of claiming readiness.
/// </summary>
public static class StatusText
{
    public const string Ready = "Ready";
    public const string NotReady = "Not Ready";
    public const string InProgress = "In Progress";
    public const string Successful = "Successful";
    public const string Failed = "Failed";

    public static Banner Banner(DashboardController dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        if (dashboard.IsBusy)
        {
            return new Banner($"{InProgress}: {dashboard.CurrentActivity ?? "P3B is working"}. Please wait.", BannerKind.InProgress);
        }

        if (dashboard.AuditStatus is not { IsLoaded: true })
        {
            return dashboard.LastP3AAudit is { HasVerdict: false } failed
                ? new Banner($"P3A's audit could not run: {failed.Summary}", BannerKind.Neutral)
                : dashboard.CanRunP3AAudit
                    ? new Banner("Run P3A's audit to see which problems P3B can fix.", BannerKind.Neutral)
                    : new Banner("Load P3A's audit results to see which problems P3B can fix.", BannerKind.Neutral);
        }

        if (dashboard.AnyChangeAttempted)
        {
            return new Banner("Re-audit needed: run P3A's audit to confirm whether this computer is now Ready.", BannerKind.ReauditNeeded);
        }

        var issues = dashboard.Issues;
        var fixable = issues.Count(i => i.Support == FixSupport.Supported);
        var manual = issues.Count(i => i.Support == FixSupport.ManualSteps);

        if (dashboard.LastP3AAudit is { HasVerdict: false } notRun)
        {
            return new Banner($"P3A's audit could not run, so the list shows its previous findings. {notRun.Summary}", BannerKind.Neutral);
        }

        // Ready / Not Ready is P3A's verdict, repeated word for word.
        if (dashboard.LastP3AAudit is { HasVerdict: true } audit)
        {
            return audit.Verdict == ReadinessVerdict.Ready
                ? new Banner($"{Ready}: P3A's audit found every component ready.", BannerKind.Ready)
                : new Banner(
                    $"{NotReady}: P3A's audit found {issues.Count} problem(s). {FixSummary(fixable, manual, issues.Count)}",
                    BannerKind.NotReady);
        }
        if (issues.Count == 0)
        {
            return new Banner($"{Ready}: P3A's audit reported no problems.", BannerKind.Ready);
        }

        return new Banner(
            $"{NotReady}: P3A's audit reported {issues.Count} problem(s). P3B can fix {fixable} of them automatically.",
            BannerKind.NotReady);
    }

    private static string FixSummary(int fixable, int manual, int total)
    {
        var parts = new List<string> { $"P3B can fix {fixable} automatically" };
        if (manual > 0)
        {
            parts.Add($"{manual} have manual steps");
        }

        var rest = total - fixable - manual;
        if (rest > 0)
        {
            parts.Add($"{rest} have no automatic fix");
        }

        return string.Join("; ", parts) + ".";
    }

    /// <summary>P3A's verdict, in P3A's words.</summary>
    public static string Verdict(ReadinessVerdict verdict) => verdict switch
    {
        ReadinessVerdict.Ready => Ready,
        ReadinessVerdict.NotReady => NotReady,
        _ => "Could not run",
    };

    /// <summary>
    /// The P3A readiness panel: P3A's overall result and when it was produced, kept apart from
    /// P3B's remediation and smoke-test results.
    /// </summary>
    public static Banner P3AReadiness(DashboardController dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        if (dashboard.LastP3AAudit is not { } audit)
        {
            return new Banner(
                dashboard.CanRunP3AAudit ? "P3A overall environment: not audited yet" : "P3A overall environment: P3A's audit is not available",
                BannerKind.Neutral);
        }

        var when = audit.AuditedAt.ToLocalTime().ToString("t");
        if (!audit.HasVerdict)
        {
            return new Banner($"P3A overall environment: audit could not run ({when})", BannerKind.Neutral);
        }

        var text = $"P3A overall environment: {Verdict(audit.Verdict)}   (audited {when}, {audit.Components.Count} components)";
        if (dashboard.AnyChangeAttempted)
        {
            return new Banner($"{text}. Out of date: something changed since", BannerKind.ReauditNeeded);
        }

        return new Banner(text, audit.Verdict == ReadinessVerdict.Ready ? BannerKind.Ready : BannerKind.NotReady);
    }

    public static string Problem(string? state) => state switch
    {
        "missing" => "Not installed",
        "unapprovedVersion" => "Unapproved version",
        "misconfigured" => "Set up incorrectly",
        null or "" => "(not stated)",
        _ => state,
    };

    public static string Support(FixSupport support) => support switch
    {
        FixSupport.Supported => "Supported",
        FixSupport.NotSupported => "Not supported",
        FixSupport.ManualSteps => "Manual steps",
        FixSupport.InvalidAuditData => "Incomplete audit data",
        _ => "Manifest needed",
    };

    public static string Status(IssueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Status switch
        {
            IssueStatus.InProgress => InProgress,
            IssueStatus.Successful => Successful,
            IssueStatus.Failed => Failed,
            IssueStatus.Cancelled => "Cancelled",
            IssueStatus.NotApproved => "Not approved",
            IssueStatus.NotSupported => "Not supported",
            IssueStatus.CouldNotRun => "Could not run",
            _ => item.Support switch
            {
                FixSupport.Supported => "Not started",
                FixSupport.ManualSteps => "Waiting for you",
                _ => "No automatic fix",
            },
        };
    }

    /// <summary>Label for a remediation outcome. "Not supported" is never shown as "Failed".</summary>
    public static string Outcome(RemediationOutcome outcome) => outcome switch
    {
        RemediationOutcome.Succeeded => Successful,
        RemediationOutcome.Failed => Failed,
        RemediationOutcome.Cancelled => "Cancelled",
        RemediationOutcome.Rejected => "Not approved",
        RemediationOutcome.Unsupported => "Not supported",
        RemediationOutcome.InvalidConfiguration => "Could not run",
        _ => "Busy",
    };

    public static IssueStatus ToIssueStatus(RemediationOutcome outcome) => outcome switch
    {
        RemediationOutcome.Succeeded => IssueStatus.Successful,
        RemediationOutcome.Failed => IssueStatus.Failed,
        RemediationOutcome.Cancelled => IssueStatus.Cancelled,
        RemediationOutcome.Rejected => IssueStatus.NotApproved,
        RemediationOutcome.Unsupported => IssueStatus.NotSupported,
        RemediationOutcome.InvalidConfiguration => IssueStatus.CouldNotRun,
        _ => IssueStatus.NotStarted,
    };

    public static string Verification(SmokeTestResult? smokeTest) => smokeTest?.Outcome switch
    {
        null => "Not run",
        SmokeTestOutcome.Passed => "Passed",
        SmokeTestOutcome.Failed => Failed,
        SmokeTestOutcome.TimedOut => "Timed out",
        SmokeTestOutcome.Unsupported => "Not verified",
        _ => "Cancelled",
    };

    public static string Check(SmokeCheckStatus status) => status switch
    {
        SmokeCheckStatus.Passed => "Passed",
        SmokeCheckStatus.Failed => Failed,
        SmokeCheckStatus.TimedOut => "Timed out",
        _ => "Not supported",
    };

    public static string Reversibility(ReversalKind kind) => kind switch
    {
        ReversalKind.Automatic => "P3B can reverse this",
        ReversalKind.ManualOnly => "Manual reversal only",
        _ => "Nothing to reverse",
    };

    /// <summary>Never says "reversed" unless the reversal was completed and verified.</summary>
    public static string Rollback(RollbackOutcome outcome) => outcome switch
    {
        RollbackOutcome.Reversed => "Reversed",
        RollbackOutcome.Cancelled => "Cancelled: the change was NOT confirmed as reversed. The record is kept; check before trying again.",
        RollbackOutcome.Conflict => "Not reversed: something else has changed since P3B's change.",
        RollbackOutcome.Failed => "Failed: the change was NOT reversed. The record is kept.",
        RollbackOutcome.NotConfirmed => "Not reversed: you did not confirm.",
        RollbackOutcome.ManualOnly => "Manual reversal only: P3B cannot reverse this automatically.",
        RollbackOutcome.NothingToReverse => "Nothing to reverse.",
        RollbackOutcome.AlreadyClosed => "Already reversed or discarded.",
        RollbackOutcome.Busy => "Not reversed: another operation is in progress.",
        RollbackOutcome.NotFound => "Not reversed: the record was not found.",
        _ => "Not reversed: P3B has no way to reverse this kind of change.",
    };
}
