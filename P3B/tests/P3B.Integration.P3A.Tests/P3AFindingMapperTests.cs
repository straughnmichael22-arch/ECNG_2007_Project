using EDAEnvironmentManager.Audit;
using P3B.Remediation.Core.Integration;

namespace P3B.Integration.P3A.Tests;

/// <summary>
/// The adapter's conversion of P3A's real audit results (P3A's own types, field names and
/// finding codes) into P3B audit issues.
/// </summary>
public class P3AFindingMapperTests
{
    internal static ComponentAuditResult Component(
        AuditFinding finding,
        string type = "ghdl",
        string version = "Not Available",
        string? path = null,
        string reason = "TEST reason from P3A",
        ComponentKind kind = ComponentKind.Tool,
        bool? ready = null) => new()
    {
        Kind = kind,
        Name = $"TEST {type}",
        Type = type,
        DisplayName = $"TEST {type}",
        InstallationStatus = path is null ? "Not Installed" : "Detected",
        ReadinessStatus = ready ?? finding == AuditFinding.None ? "Ready" : "Not Ready",
        Version = version,
        InstalledPath = path,
        Reason = reason,
        Finding = finding,
        CountsAsReady = ready ?? finding == AuditFinding.None,
    };

    [Fact]
    public void Not_installed_tool_becomes_a_valid_missing_issue()
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(AuditFinding.NotInstalled, "surfer"))!;

        Assert.Equal("surfer/NotInstalled", issue.IssueId);
        Assert.Equal("surfer", issue.ToolId);
        Assert.Equal("missing", issue.State);
        Assert.Null(issue.DetectedVersion);
        Assert.Equal("TEST reason from P3A", issue.Detail);

        var request = RemediationRequestFactory.FromAuditIssue(issue);
        Assert.True(request.IsValid);
        Assert.Equal(AuditIssueState.Missing, request.Request.State);
    }

    [Theory]
    [InlineData(AuditFinding.VersionBelowMinimum)]
    [InlineData(AuditFinding.VersionAboveMaximum)]
    public void Version_outside_the_range_becomes_a_valid_unapproved_version_issue(AuditFinding finding)
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(finding, "vscode", "1.99.0.0", @"C:\TEST\Code.exe"))!;

        Assert.Equal($"vscode/{finding}", issue.IssueId);
        Assert.Equal("unapprovedVersion", issue.State);
        Assert.Equal("1.99.0.0", issue.DetectedVersion);
        Assert.Contains(@"Found at: C:\TEST\Code.exe", issue.Detail);

        var request = RemediationRequestFactory.FromAuditIssue(issue);
        Assert.True(request.IsValid);
        Assert.Equal(AuditIssueState.UnapprovedVersion, request.Request.State);
        Assert.Equal("1.99.0.0", request.Request.DetectedVersion);
    }

    [Theory]
    [InlineData(AuditFinding.ExtensionNotInstalled)]
    [InlineData(AuditFinding.EditorNotDetected)]
    public void Extension_findings_become_valid_missing_issues(AuditFinding finding)
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(finding, "vscode-extension", kind: ComponentKind.Extension))!;

        Assert.Equal("missing", issue.State);
        Assert.Equal("vscode-extension", issue.ToolId);
        Assert.True(RemediationRequestFactory.FromAuditIssue(issue).IsValid);
    }

    [Theory]
    [InlineData(AuditFinding.VersionUnknown)]
    [InlineData(AuditFinding.VersionUnparseable)]
    [InlineData(AuditFinding.InvalidMinimumVersion)]
    [InlineData(AuditFinding.InvalidMaximumVersion)]
    public void Findings_with_no_P3B_state_are_reported_as_incomplete_audit_data(AuditFinding finding)
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(finding, "ghdl", path: @"C:\TEST\ghdl.exe"))!;

        // P3A's real finding code is passed on, never replaced by a guessed state.
        Assert.Equal(finding.ToString(), issue.State);
        var request = RemediationRequestFactory.FromAuditIssue(issue);
        Assert.False(request.IsValid);
        Assert.Contains(request.Problems, p => p.Contains($"'{finding}' is not recognised", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_P3A_finding_code_is_reported_as_incomplete_audit_data()
    {
        var future = (AuditFinding)99;

        var issue = P3AFindingMapper.ToAuditIssue(Component(future, ready: false))!;

        Assert.Equal("99", issue.State);
        Assert.False(RemediationRequestFactory.FromAuditIssue(issue).IsValid);
    }

    [Fact]
    public void Not_ready_component_with_no_finding_is_reported_as_incomplete_audit_data()
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(AuditFinding.None, ready: false))!;

        Assert.False(RemediationRequestFactory.FromAuditIssue(issue).IsValid);
    }

    [Fact]
    public void Component_without_a_type_is_reported_as_incomplete_audit_data()
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(AuditFinding.NotInstalled, type: " "))!;

        Assert.Null(issue.IssueId);
        Assert.Null(issue.ToolId);
        var request = RemediationRequestFactory.FromAuditIssue(issue);
        Assert.False(request.IsValid);
        Assert.Contains(request.Problems, p => p.Contains("issueId", StringComparison.Ordinal));
        Assert.Contains(request.Problems, p => p.Contains("toolId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Not Available")]
    [InlineData("Not Checked")]
    [InlineData("")]
    public void P3A_placeholders_are_never_passed_on_as_versions(string placeholder)
    {
        var issue = P3AFindingMapper.ToAuditIssue(Component(AuditFinding.NotInstalled, version: placeholder))!;

        Assert.Null(issue.DetectedVersion);
    }

    [Fact]
    public void Ready_and_disabled_components_produce_no_issue()
    {
        Assert.Null(P3AFindingMapper.ToAuditIssue(Component(AuditFinding.None, version: "5.1.1")));
        Assert.Null(P3AFindingMapper.ToAuditIssue(Component(AuditFinding.CheckDisabled, kind: ComponentKind.Extension, ready: true)));
    }

    [Fact]
    public void Verdict_is_P3A_overall_result_and_every_component_is_kept()
    {
        var notReady = new AuditReport("1.0", DateTimeOffset.Now, [Component(AuditFinding.None), Component(AuditFinding.NotInstalled, "surfer")]);
        var ready = new AuditReport("1.0", DateTimeOffset.Now, [Component(AuditFinding.None)]);

        var notReadyAudit = P3AFindingMapper.ToReadinessAudit(notReady);
        var readyAudit = P3AFindingMapper.ToReadinessAudit(ready);

        Assert.Equal(ReadinessVerdict.NotReady, notReadyAudit.Verdict);
        Assert.Equal(2, notReadyAudit.Components.Count);
        Assert.Equal("surfer/NotInstalled", Assert.Single(notReadyAudit.Issues).IssueId);
        Assert.Contains("Overall Environment: Not Ready", notReadyAudit.Summary);

        Assert.Equal(ReadinessVerdict.Ready, readyAudit.Verdict);
        Assert.Empty(readyAudit.Issues);
        Assert.Contains("Overall Environment: Ready", readyAudit.Summary);
    }
}
