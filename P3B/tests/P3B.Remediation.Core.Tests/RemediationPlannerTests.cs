using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests;

public class RemediationPlannerTests
{
    [Theory]
    [InlineData(AuditIssueState.Missing)]
    [InlineData(AuditIssueState.UnapprovedVersion)]
    public void Valid_request_for_a_manifest_tool_gets_an_action_targeting_the_approved_version(AuditIssueState state)
    {
        var request = TestSupport.Request(state: state, detectedVersion: "TEST-VERSION-UNAPPROVED");

        var plan = RemediationPlanner.Plan(request, TestManifests.Default());

        Assert.True(plan.IsSupported);
        Assert.Same(request, plan.Action.Request);
        Assert.Equal(request.Issue, plan.Action.Issue);
        Assert.Equal(request.Tool, plan.Action.Tool.Id);
        Assert.Equal(TestManifests.TargetVersion, plan.Action.TargetVersion);
        Assert.Equal(RemediationMethod.WinGet, plan.Action.Method);
        Assert.Equal($"TEST-SOURCE-{TestManifests.ToolA}", plan.Action.Source);
    }

    [Fact]
    public void Tool_not_in_the_manifest_is_unsupported()
    {
        var plan = RemediationPlanner.Plan(TestSupport.Request(TestManifests.NotInManifestTool), TestManifests.Default());

        Assert.False(plan.IsSupported);
        Assert.Null(plan.Action);
        Assert.Contains("not defined in the Local EDA Tool Manifest", plan.UnsupportedReason);
    }

    [Fact]
    public void Tool_with_remediation_method_none_is_unsupported()
    {
        var plan = RemediationPlanner.Plan(TestSupport.Request(TestManifests.NoFixTool), TestManifests.Default());

        Assert.False(plan.IsSupported);
        Assert.Contains("no automatic remediation", plan.UnsupportedReason);
    }

    [Fact]
    public void Misconfigured_tool_is_unsupported_until_configuration_fixes_are_specified()
    {
        var plan = RemediationPlanner.Plan(
            TestSupport.Request(state: AuditIssueState.Misconfigured),
            TestManifests.Default());

        Assert.False(plan.IsSupported);
        Assert.Contains("misconfigured", plan.UnsupportedReason);
    }

    [Theory]
    [InlineData("TEST-Vivado-Tool")]
    [InlineData("TEST-XILINX-TOOL")]
    public void Vivado_is_always_unsupported_whatever_the_manifest_says(string toolId)
    {
        var manifest = new LocalEdaToolManifest(ProvisionalManifestSchema.SupportedVersion, [TestManifests.Tool(toolId)]);

        var plan = RemediationPlanner.Plan(TestSupport.Request(toolId), manifest);

        Assert.False(plan.IsSupported);
        Assert.Equal(Safety.ProhibitedSoftwarePolicy.VivadoMessage, plan.UnsupportedReason);
    }

    [Fact]
    public void Link_to_installer_tool_is_unsupported_with_manual_instructions()
    {
        var manifest = new LocalEdaToolManifest(
            ProvisionalManifestSchema.SupportedVersion,
            [TestManifests.Tool(TestManifests.ToolA, RemediationMethod.LinkToInstaller)]);

        var plan = RemediationPlanner.Plan(TestSupport.Request(), manifest);

        Assert.False(plan.IsSupported);
        Assert.Contains("yourself", plan.UnsupportedReason);
        Assert.Contains($"TEST-SOURCE-{TestManifests.ToolA}", plan.UnsupportedReason);
    }

    [Fact]
    public void Action_cannot_pair_a_request_with_a_different_tool()
    {
        Assert.Throws<ArgumentException>(() =>
            new RemediationAction(TestSupport.Request(TestManifests.ToolA), TestManifests.Tool(TestManifests.ToolB)));
    }
}
