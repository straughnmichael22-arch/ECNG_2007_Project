namespace P3B.Remediation.Core.Actions;

/// <summary>
/// The user's explicit decision about one specific <see cref="ActionPreview"/>.
/// </summary>
/// <remarks>
/// Can only be created through <see cref="Approve"/> or <see cref="Reject"/>, and is bound
/// to the preview it was made for: an approval of one preview never authorises another.
/// </remarks>
public sealed record ApprovalDecision
{
    private ApprovalDecision(Guid previewId, bool isApproved, string? reason)
    {
        PreviewId = previewId;
        IsApproved = isApproved;
        Reason = reason;
    }

    public Guid PreviewId { get; }

    public bool IsApproved { get; }

    public string? Reason { get; }

    public static ApprovalDecision Approve(ActionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new ApprovalDecision(preview.PreviewId, isApproved: true, reason: null);
    }

    public static ApprovalDecision Reject(ActionPreview preview, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new ApprovalDecision(preview.PreviewId, isApproved: false, reason);
    }

    /// <summary>True only if this is an approval of exactly <paramref name="preview"/>.</summary>
    public bool Approves(ActionPreview preview) =>
        IsApproved && preview is not null && PreviewId == preview.PreviewId;
}
