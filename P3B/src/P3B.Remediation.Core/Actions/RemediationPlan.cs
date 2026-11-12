using System.Diagnostics.CodeAnalysis;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// Either a supported <see cref="RemediationAction"/>, or the reason none is available together
/// with any manual steps the manifest gives.
/// </summary>
public sealed record RemediationPlan
{
    private RemediationPlan(RemediationAction? action, string? unsupportedReason, ManualGuidance? guidance)
    {
        Action = action;
        UnsupportedReason = unsupportedReason;
        Guidance = guidance;
    }

    public RemediationAction? Action { get; }

    public string? UnsupportedReason { get; }

    /// <summary>Manual steps from the manifest when there is no automatic fix; null if it gives none.</summary>
    public ManualGuidance? Guidance { get; }

    [MemberNotNullWhen(true, nameof(Action))]
    public bool IsSupported => Action is not null;

    public static RemediationPlan Supported(RemediationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new RemediationPlan(action, unsupportedReason: null, guidance: null);
    }

    public static RemediationPlan Unsupported(string reason, ManualGuidance? guidance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new RemediationPlan(action: null, reason, guidance);
    }
}
