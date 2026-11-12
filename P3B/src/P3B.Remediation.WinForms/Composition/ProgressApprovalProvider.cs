using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.WinForms.Composition;

/// <summary>
/// Passes approval requests to the user and reports progress around them, so the dashboard can
/// show "waiting for your approval" and then "installing…".
/// </summary>
public sealed class ProgressApprovalProvider : IApprovalProvider
{
    private readonly IApprovalProvider _inner;

    public ProgressApprovalProvider(IApprovalProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Set by the dashboard for the duration of one operation.</summary>
    public IProgress<string>? Progress { get; set; }

    public async Task<ApprovalDecision> RequestApprovalAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var subject = $"{preview.Action.Tool.Name} {preview.Action.TargetVersion}";

        Progress?.Report($"Waiting for your approval to install {subject}.");
        var decision = await _inner.RequestApprovalAsync(preview, cancellationToken).ConfigureAwait(false);

        Progress?.Report(decision is not null && decision.Approves(preview)
            ? $"Installing and checking {subject}. This can take several minutes; the window stays usable."
            : "Not approved. Nothing was changed.");
        return decision!;
    }
}
