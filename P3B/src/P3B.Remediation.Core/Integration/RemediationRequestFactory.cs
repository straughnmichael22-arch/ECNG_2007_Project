using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Validates an <see cref="AuditIssue"/> from P3A and turns it into a
/// <see cref="RemediationRequest"/>, reporting any missing audit information.
/// </summary>
/// <remarks>PROVISIONAL – follows docs/proposals/audit-remediation-contract.PROVISIONAL.md.</remarks>
public static class RemediationRequestFactory
{
    private static readonly Dictionary<string, AuditIssueState> States = new(StringComparer.Ordinal)
    {
        ["missing"] = AuditIssueState.Missing,
        ["misconfigured"] = AuditIssueState.Misconfigured,
        ["unapprovedVersion"] = AuditIssueState.UnapprovedVersion,
    };

    public static RequestCreationResult FromAuditIssue(AuditIssue? issue)
    {
        if (issue is null)
        {
            return RequestCreationResult.Invalid(["No audit issue was supplied."]);
        }

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(issue.IssueId))
        {
            problems.Add("The audit issue has no issueId.");
        }

        if (string.IsNullOrWhiteSpace(issue.ToolId))
        {
            problems.Add("The audit issue has no toolId.");
        }

        AuditIssueState state = default;
        if (string.IsNullOrWhiteSpace(issue.State))
        {
            problems.Add("The audit issue has no state.");
        }
        else if (!States.TryGetValue(issue.State, out state))
        {
            problems.Add(
                $"The audit issue state '{issue.State}' is not recognised. Expected one of: {string.Join(", ", States.Keys)}.");
        }
        else if (state == AuditIssueState.UnapprovedVersion && string.IsNullOrWhiteSpace(issue.DetectedVersion))
        {
            problems.Add("The audit issue reports an unapproved version but gives no detectedVersion.");
        }

        if (problems.Count > 0)
        {
            return RequestCreationResult.Invalid(problems);
        }

        return RequestCreationResult.Valid(new RemediationRequest(
            new ToolReference(issue.ToolId!),
            new IssueReference(issue.IssueId!),
            state,
            issue.DetectedVersion));
    }
}
