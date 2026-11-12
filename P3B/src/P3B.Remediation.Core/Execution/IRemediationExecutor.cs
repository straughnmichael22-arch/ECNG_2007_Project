using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.Core.Execution;

/// <summary>
/// Performs one kind of remediation action.
/// </summary>
/// <remarks>
/// INTERFACE ONLY: no production implementation exists yet. Real executors (WinGet,
/// official archives, links to installers) are blocked on the real manifest and approved
/// sources; see docs/BLOCKERS.md. Implementations must:
/// <list type="bullet">
/// <item>refuse to run unless <c>approval.Approves(preview)</c> is true;</item>
/// <item>never install or license Vivado (3B-DC-004);</item>
/// <item>install only <see cref="RemediationAction.TargetVersion"/>, never another version (3B-DC-005);</item>
/// <item>throw <see cref="OperationCanceledException"/> when cancelled.</item>
/// </list>
/// </remarks>
public interface IRemediationExecutor
{
    bool CanHandle(RemediationAction action);

    ActionPreview CreatePreview(RemediationAction action);

    Task<ExecutionReport> ExecuteAsync(
        ActionPreview preview,
        ApprovalDecision approval,
        CancellationToken cancellationToken);
}
