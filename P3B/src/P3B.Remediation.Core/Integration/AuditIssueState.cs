namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Why P3A reported a tool as not ready.
/// </summary>
/// <remarks>
/// PROVISIONAL – values taken from the P3A brief, which requires P3A to "report missing,
/// misconfigured or unapproved-version states". The agreed contract may define more.
/// Wire values: <c>missing</c>, <c>misconfigured</c>, <c>unapprovedVersion</c>.
/// See docs/proposals/audit-remediation-contract.PROVISIONAL.md.
/// </remarks>
public enum AuditIssueState
{
    Missing,
    Misconfigured,
    UnapprovedVersion,
}
