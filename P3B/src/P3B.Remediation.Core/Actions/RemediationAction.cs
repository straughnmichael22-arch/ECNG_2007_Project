using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// What P3B will do for one request, resolved against the manifest. Ties the action to its
/// originating audit issue and affected tool (P3B-DR-01). The target version always comes
/// from the manifest, never from code (3B-DC-005).
/// </summary>
public sealed record RemediationAction
{
    public RemediationAction(RemediationRequest request, ManifestTool tool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.Id != request.Tool)
        {
            throw new ArgumentException(
                $"The manifest tool '{tool.Id}' does not match the requested tool '{request.Tool}'.",
                nameof(tool));
        }

        Request = request;
        Tool = tool;
    }

    public Guid ActionId { get; } = Guid.NewGuid();

    public RemediationRequest Request { get; }

    /// <summary>The affected tool's manifest definition.</summary>
    public ManifestTool Tool { get; }

    /// <summary>The originating audit issue.</summary>
    public IssueReference Issue => Request.Issue;

    /// <summary>The approved target version (PROVISIONAL rule: the manifest's first approved version).</summary>
    public string TargetVersion => Tool.ApprovedVersions[0];

    public RemediationMethod Method => Tool.Remediation.Method;

    public string? Source => Tool.Remediation.Source;
}
