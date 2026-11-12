namespace P3B.Remediation.Core.Integration;

/// <summary>
/// One non-ready finding handed from P3A to P3B, exactly as received (3B-IR-002).
/// </summary>
/// <remarks>
/// P3B's internal form of a finding. P3B.Integration.P3A converts P3A's real audit results to
/// it (IssueId "{type}/{finding}", ToolId = the manifest type, State from P3A's finding code).
/// Every member is optional so that incomplete audit data is reported by
/// <see cref="RemediationRequestFactory"/> instead of failing. The JSON file form of this record
/// is still provisional: see docs/proposals/audit-remediation-contract.PROVISIONAL.md.
/// </remarks>
public sealed record AuditIssue
{
    /// <summary>P3A's identifier for this finding (originating audit issue, P3B-DR-01).</summary>
    public string? IssueId { get; init; }

    /// <summary>The manifest tool identity the finding is about.</summary>
    public string? ToolId { get; init; }

    /// <summary><c>missing</c>, <c>misconfigured</c> or <c>unapprovedVersion</c>.</summary>
    public string? State { get; init; }

    /// <summary>The version P3A detected, if any. Required when State is <c>unapprovedVersion</c>.</summary>
    public string? DetectedVersion { get; init; }

    /// <summary>Optional human-readable explanation from P3A.</summary>
    public string? Detail { get; init; }
}
