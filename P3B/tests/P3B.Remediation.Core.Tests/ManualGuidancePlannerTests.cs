using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Safety;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// The planner branches added for P3A's manifest: manual guidance, approved ranges and
/// extension groups. Fake identifiers only.
/// </summary>
public class ManualGuidancePlannerTests
{
    private const string Url = "https://example.invalid/TEST-download";

    private static ManifestTool RangeTool(
        string id = "test-tool",
        string? url = Url,
        string[]? steps = null,
        string? minimum = "1.0",
        string? maximum = "2.0") => new(
        new ToolReference(id),
        $"Test {id}",
        [],
        url is null ? new ManifestRemediation(RemediationMethod.None, null) : new ManifestRemediation(RemediationMethod.LinkToInstaller, url),
        approvedRange: new ApprovedVersionRange(minimum, maximum),
        instructions: steps ?? ["TEST step 1", "TEST step 2"]);

    private static RemediationPlan Plan(ManifestTool tool, AuditIssueState state = AuditIssueState.Missing) =>
        RemediationPlanner.Plan(
            TestSupport.Request(tool.Id.Value, state: state, detectedVersion: state == AuditIssueState.UnapprovedVersion ? "9.9" : null),
            new LocalEdaToolManifest(P3AManifestSchema.SupportedVersion, [tool]));

    [Theory]
    [InlineData(AuditIssueState.Missing)]
    [InlineData(AuditIssueState.UnapprovedVersion)]
    public void Link_with_steps_is_unsupported_but_gives_the_download_page_and_steps(AuditIssueState state)
    {
        var plan = Plan(RangeTool(), state);

        Assert.False(plan.IsSupported);
        Assert.NotNull(plan.Guidance);
        Assert.Equal(Url, plan.Guidance.DownloadUrl);
        Assert.Equal(new[] { "TEST step 1", "TEST step 2" }, plan.Guidance.Steps);
        Assert.Contains("an approved version (1.0 to 2.0)", plan.UnsupportedReason);
        Assert.Contains(Url, plan.UnsupportedReason);
    }

    [Fact]
    public void Link_without_a_version_range_says_the_version_is_not_restricted()
    {
        var plan = Plan(RangeTool(minimum: null, maximum: null));

        Assert.Contains("does not restrict the version", plan.UnsupportedReason);
    }

    [Fact]
    public void No_method_with_steps_gives_manual_steps()
    {
        var plan = Plan(RangeTool(url: null));

        Assert.False(plan.IsSupported);
        Assert.NotNull(plan.Guidance);
        Assert.Null(plan.Guidance.DownloadUrl);
        Assert.Equal(2, plan.Guidance.Steps.Count);
        Assert.Contains("Follow the manual steps", plan.UnsupportedReason);
    }

    [Fact]
    public void No_method_and_no_steps_is_simply_unsupported()
    {
        var plan = Plan(RangeTool(url: null, steps: []));

        Assert.False(plan.IsSupported);
        Assert.Null(plan.Guidance);
        Assert.Contains("no automatic remediation", plan.UnsupportedReason);
    }

    [Fact]
    public void Vivado_stays_prohibited_but_the_manifest_steps_are_still_shown()
    {
        var plan = Plan(RangeTool(id: "test-vivado"));

        Assert.False(plan.IsSupported);
        Assert.Equal(ProhibitedSoftwarePolicy.VivadoMessage, plan.UnsupportedReason);
        Assert.NotNull(plan.Guidance);
        Assert.Equal(Url, plan.Guidance.DownloadUrl);
    }

    [Fact]
    public void Extension_group_is_unsupported_and_lists_the_approved_extensions()
    {
        var extension = new ManifestTool(
            new ToolReference("test-extension"),
            "Test extensions",
            [],
            new ManifestRemediation(RemediationMethod.None, null),
            kind: ManifestComponentKind.Extension,
            approvedExtensions: ["test.one", "test.two"]);

        var plan = Plan(extension);

        Assert.False(plan.IsSupported);
        Assert.Null(plan.Guidance);
        Assert.Contains("one exact approved version", plan.UnsupportedReason);
        Assert.Contains("test.one, test.two", plan.UnsupportedReason);
    }

    [Fact]
    public void Extension_group_with_no_approved_extensions_says_so()
    {
        var extension = new ManifestTool(
            new ToolReference("test-extension"), null, [], new ManifestRemediation(RemediationMethod.None, null),
            kind: ManifestComponentKind.Extension);

        var plan = Plan(extension);

        Assert.Contains("lists no approved extensions", plan.UnsupportedReason);
    }

    [Fact]
    public void Misconfigured_state_is_still_unsupported_with_any_manifest_steps()
    {
        var tool = new ManifestTool(
            new ToolReference("test-tool"), null, ["TEST-VERSION-1"], new ManifestRemediation(RemediationMethod.WinGet, "TEST.Package"),
            instructions: ["TEST step"]);

        var plan = Plan(tool, AuditIssueState.Misconfigured);

        Assert.False(plan.IsSupported);
        Assert.Contains("misconfigured", plan.UnsupportedReason);
        Assert.Equal(new[] { "TEST step" }, plan.Guidance!.Steps);
    }

    [Fact]
    public void A_link_that_is_not_a_web_address_is_never_offered_as_a_download_page()
    {
        var tool = new ManifestTool(
            new ToolReference("test-tool"), null, ["TEST-VERSION-1"], new ManifestRemediation(RemediationMethod.LinkToInstaller, "TEST-SOURCE"),
            instructions: ["TEST step"]);

        var guidance = ManualGuidance.From(tool);

        Assert.NotNull(guidance);
        Assert.Null(guidance.DownloadUrl);
    }

    [Fact]
    public void Supported_plan_carries_no_manual_guidance()
    {
        var plan = RemediationPlanner.Plan(TestSupport.Request(), TestManifests.Default());

        Assert.True(plan.IsSupported);
        Assert.Null(plan.Guidance);
    }
}
