using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// The PROVISIONAL audit-state contract: turning P3A findings into remediation requests.
/// </summary>
public class AuditContractTests
{
    private static AuditIssue ValidIssue() => new()
    {
        IssueId = "TEST-ISSUE-1",
        ToolId = TestManifests.ToolA,
        State = "unapprovedVersion",
        DetectedVersion = "TEST-VERSION-UNAPPROVED",
        Detail = "TEST detail",
    };

    [Fact]
    public void Valid_audit_issue_becomes_a_remediation_request()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue());

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
        Assert.Equal(TestManifests.ToolA, result.Request.Tool.Value);
        Assert.Equal("TEST-ISSUE-1", result.Request.Issue.Value);
        Assert.Equal(AuditIssueState.UnapprovedVersion, result.Request.State);
        Assert.Equal("TEST-VERSION-UNAPPROVED", result.Request.DetectedVersion);
    }

    [Fact]
    public void Missing_audit_issue_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(null);

        Assert.False(result.IsValid);
        Assert.Contains("No audit issue was supplied.", result.Problems);
    }

    [Fact]
    public void Missing_issue_id_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { IssueId = " " });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("issueId", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_tool_id_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { ToolId = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("toolId", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_state_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { State = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("no state", StringComparison.Ordinal));
    }

    [Fact]
    public void All_missing_fields_are_reported_together()
    {
        var result = RemediationRequestFactory.FromAuditIssue(new AuditIssue());

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Problems.Count);
    }

    [Fact]
    public void Unrecognised_state_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { State = "TEST-UNKNOWN-STATE" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("TEST-UNKNOWN-STATE", StringComparison.Ordinal));
    }

    [Fact]
    public void Unapproved_version_without_detected_version_is_reported()
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { DetectedVersion = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("detectedVersion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing", AuditIssueState.Missing)]
    [InlineData("misconfigured", AuditIssueState.Misconfigured)]
    [InlineData("unapprovedVersion", AuditIssueState.UnapprovedVersion)]
    public void Each_provisional_state_is_recognised(string wireValue, AuditIssueState expected)
    {
        var result = RemediationRequestFactory.FromAuditIssue(ValidIssue() with { State = wireValue });

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Request.State);
    }
}
