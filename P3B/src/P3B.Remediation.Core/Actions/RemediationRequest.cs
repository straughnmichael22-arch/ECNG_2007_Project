using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// A validated request to remediate one audit issue for one tool. Create one from P3A data
/// with <see cref="RemediationRequestFactory"/>.
/// </summary>
public sealed record RemediationRequest
{
    public RemediationRequest(
        ToolReference tool,
        IssueReference issue,
        AuditIssueState state,
        string? detectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(issue);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown audit issue state.");
        }

        Tool = tool;
        Issue = issue;
        State = state;
        DetectedVersion = detectedVersion;
    }

    public Guid RequestId { get; } = Guid.NewGuid();

    public ToolReference Tool { get; }

    /// <summary>The originating audit issue (P3B-DR-01).</summary>
    public IssueReference Issue { get; }

    public AuditIssueState State { get; }

    /// <summary>The version P3A detected, if any.</summary>
    public string? DetectedVersion { get; }
}
