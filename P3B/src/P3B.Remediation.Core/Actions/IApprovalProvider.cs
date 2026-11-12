namespace P3B.Remediation.Core.Actions;

/// <summary>
/// Shows an <see cref="ActionPreview"/> to the user and returns their explicit decision.
/// </summary>
/// <remarks>
/// Implementations must never approve automatically. The WinForms app implements this with
/// a dialog; tests use a fake.
/// </remarks>
public interface IApprovalProvider
{
    Task<ApprovalDecision> RequestApprovalAsync(ActionPreview preview, CancellationToken cancellationToken);
}
