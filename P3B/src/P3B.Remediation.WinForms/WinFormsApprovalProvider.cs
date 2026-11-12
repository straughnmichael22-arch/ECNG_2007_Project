using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.WinForms;

/// <summary>
/// Asks the user for an explicit decision using <see cref="ApprovalDialog"/>. Closing the
/// dialog any way other than clicking Approve is a rejection.
/// </summary>
internal sealed class WinFormsApprovalProvider : IApprovalProvider
{
    private readonly Control _owner;

    public WinFormsApprovalProvider(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public Task<ApprovalDecision> RequestApprovalAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();

        var decision = _owner.InvokeRequired
            ? (ApprovalDecision)_owner.Invoke(new Func<ApprovalDecision>(() => Ask(preview)))!
            : Ask(preview);

        return Task.FromResult(decision);
    }

    private ApprovalDecision Ask(ActionPreview preview)
    {
        using var dialog = new ApprovalDialog(preview);
        return dialog.ShowDialog(_owner) == DialogResult.OK
            ? ApprovalDecision.Approve(preview)
            : ApprovalDecision.Reject(preview, "The user chose not to apply this change.");
    }
}
