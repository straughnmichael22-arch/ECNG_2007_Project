using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;

namespace P3B.Remediation.Core.Execution;

/// <summary>
/// Entry point for remediation: preview, explicit approval, execution, reversal recording,
/// then smoke test. At most one action is active at a time (P3B-PR-01).
/// </summary>
public interface IRemediationService
{
    bool IsBusy { get; }

    /// <summary>Validates an audit issue from P3A, then remediates it.</summary>
    Task<AuditRemediationResult> RemediateAsync(AuditIssue issue, CancellationToken cancellationToken = default);

    Task<RemediationRunResult> RemediateAsync(
        RemediationRequest request,
        CancellationToken cancellationToken = default);
}
