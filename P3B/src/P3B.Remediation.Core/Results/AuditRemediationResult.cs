namespace P3B.Remediation.Core.Results;

/// <summary>
/// The result of remediating one audit issue received from P3A: either the problems that made
/// the audit information unusable, or the remediation run.
/// </summary>
public sealed record AuditRemediationResult
{
    private AuditRemediationResult(IEnumerable<string> problems, RemediationRunResult? run)
    {
        Problems = Array.AsReadOnly(problems.ToArray());
        Run = run;
    }

    public IReadOnlyList<string> Problems { get; }

    public RemediationRunResult? Run { get; }

    public static AuditRemediationResult InvalidAuditIssue(IEnumerable<string> problems) => new(problems, run: null);

    public static AuditRemediationResult Completed(RemediationRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new AuditRemediationResult([], run);
    }
}
