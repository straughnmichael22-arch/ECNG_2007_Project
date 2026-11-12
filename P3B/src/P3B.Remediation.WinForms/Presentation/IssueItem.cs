using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Results;

namespace P3B.Remediation.WinForms.Presentation;

/// <summary>Whether P3B can fix an issue automatically.</summary>
public enum FixSupport
{
    Supported,

    /// <summary>No supported automatic fix exists. This is not a failure.</summary>
    NotSupported,

    /// <summary>No automatic fix, but the manifest gives manual steps for the user to follow.</summary>
    ManualSteps,

    /// <summary>P3A's information about the issue is incomplete.</summary>
    InvalidAuditData,

    /// <summary>A valid manifest must be loaded before support can be decided.</summary>
    ManifestNeeded,
}

public enum IssueStatus
{
    NotStarted,
    InProgress,
    Successful,
    Failed,
    Cancelled,
    NotApproved,
    NotSupported,

    /// <summary>Could not run because of a configuration problem, such as an invalid manifest.</summary>
    CouldNotRun,
}

/// <summary>One issue from P3A's audit, with what P3B can do about it and what happened.</summary>
public sealed class IssueItem
{
    public IssueItem(AuditIssue issue, int position)
    {
        ArgumentNullException.ThrowIfNull(issue);
        Issue = issue;
        Creation = RemediationRequestFactory.FromAuditIssue(issue);
        Key = string.IsNullOrWhiteSpace(issue.IssueId) ? $"#{position}" : issue.IssueId;
    }

    public AuditIssue Issue { get; }

    public RequestCreationResult Creation { get; }

    /// <summary>Identifies the issue in saved results.</summary>
    public string Key { get; }

    public string ToolLabel => ToolName
        ?? (string.IsNullOrWhiteSpace(Issue.ToolId) ? "(tool not stated)" : Issue.ToolId);

    /// <summary>The tool's name from the manifest, when the manifest defines it.</summary>
    public string? ToolName { get; internal set; }

    /// <summary>The approved version or range from the manifest, for display.</summary>
    public string? ApprovedText { get; internal set; }

    /// <summary>Manual steps from the manifest when there is no automatic fix.</summary>
    public ManualGuidance? Guidance { get; internal set; }

    public FixSupport Support { get; internal set; } = FixSupport.ManifestNeeded;

    /// <summary>Why the issue is or is not supported, in plain language.</summary>
    public string SupportReason { get; internal set; } = string.Empty;

    /// <summary>The approved version P3B would install, when supported.</summary>
    public string? TargetVersion { get; internal set; }

    public IssueStatus Status { get; internal set; } = IssueStatus.NotStarted;

    /// <summary>Keeps P3B's own result for an issue P3A still reports after a re-audit.</summary>
    internal void CarryOver(IssueItem previous)
    {
        Status = previous.Status;
        LastRun = previous.LastRun;
    }

    public RemediationRunResult? LastRun { get; internal set; }
}
