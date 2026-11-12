using P3B.Remediation.Core.Results;

namespace P3B.Remediation.Core.Integration;

/// <summary>
/// What P3B returns to P3A through the shared remediation interface: the remediation outcome
/// and, separately, the verification report. Enum values are strings so it serialises cleanly.
/// </summary>
/// <remarks>
/// PROVISIONAL – P3B → P3A part of docs/proposals/audit-remediation-contract.PROVISIONAL.md.
/// P3B never reports overall readiness; <see cref="ReauditRequired"/> tells P3A to re-audit.
/// </remarks>
public sealed record RemediationReport(
    string ToolId,
    string IssueId,
    string Outcome,
    string Message,
    string? TargetVersion,
    IReadOnlyList<string> ObservedChanges,
    string? Reversibility,
    Guid? ReversalJournalEntryId,
    VerificationReport? Verification,
    bool ReauditRequired)
{
    public const string ReadinessStatement = "P3B does not determine overall readiness. P3A must re-audit the environment.";

    public string Readiness => ReadinessStatement;

    public static RemediationReport From(RemediationRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = run.Remediation;

        return new RemediationReport(
            result.Request.Tool.Value,
            result.Request.Issue.Value,
            result.Outcome.ToString(),
            result.Message,
            result.Action?.TargetVersion,
            result.ObservedChanges,
            result.Reversal?.Kind.ToString(),
            result.ReversalJournalEntryId,
            run.SmokeTest is null ? null : VerificationReport.From(run.SmokeTest),
            ReauditRequired: result.Outcome is RemediationOutcome.Succeeded or RemediationOutcome.Failed or RemediationOutcome.Cancelled);
    }
}

/// <summary>The smoke-test (verification) part of a <see cref="RemediationReport"/>.</summary>
public sealed record VerificationReport(
    string Outcome,
    string Message,
    IReadOnlyList<VerificationCheckReport> Checks,
    string Scope)
{
    public static VerificationReport From(SmokeTestResult smokeTest)
    {
        ArgumentNullException.ThrowIfNull(smokeTest);
        return new VerificationReport(
            smokeTest.Outcome.ToString(),
            smokeTest.Message,
            smokeTest.Checks
                .Select(c => new VerificationCheckReport(c.Kind, c.Description, c.Status.ToString(), c.Explanation))
                .ToArray(),
            smokeTest.ReadinessNote);
    }
}

public sealed record VerificationCheckReport(string Kind, string Description, string Status, string Explanation);
