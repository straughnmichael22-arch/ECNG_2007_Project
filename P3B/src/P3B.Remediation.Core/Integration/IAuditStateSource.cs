namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Supplies the non-ready findings from a P3A audit (3B-IR-002). Callers convert each one
/// with <see cref="RemediationRequestFactory"/>, which reports incomplete audit data.
/// </summary>
/// <remarks>
/// The production implementation is P3B.Integration.P3A.P3AAuditAdapter, which runs P3A's
/// audit in-process and converts its findings. The audit-results file
/// (<see cref="AuditResultsFile"/>) remains a provisional alternative hand-off.
/// </remarks>
public interface IAuditStateSource
{
    Task<IReadOnlyList<AuditIssue>> GetAuditIssuesAsync(CancellationToken cancellationToken);
}
