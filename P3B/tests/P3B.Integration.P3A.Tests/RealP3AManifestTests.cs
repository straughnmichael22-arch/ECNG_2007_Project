using EDAEnvironmentManager.Audit;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Safety;

namespace P3B.Integration.P3A.Tests;

/// <summary>
/// What P3B makes of P3A's real eda-tool-manifest.json (copied from the group repository at build
/// time). These tests only read the file. They describe the realistic outcome for each component.
/// </summary>
public class RealP3AManifestTests
{
    private static readonly string RealManifest = Path.Combine(AppContext.BaseDirectory, EdaManifestLoader.FileName);

    private static LocalEdaToolManifest Load()
    {
        var result = new ManifestService(RealManifest).Load();
        Assert.True(result.IsUsable, result.Description);
        return result.Manifest;
    }

    private static RemediationPlan Plan(string type, AuditIssueState state = AuditIssueState.Missing) =>
        RemediationPlanner.Plan(
            new RemediationRequest(new ToolReference(type), new IssueReference($"{type}/TEST"), state, state == AuditIssueState.UnapprovedVersion ? "0.1" : null),
            Load());

    [Fact]
    public void Real_manifest_loads_in_P3A_format_with_every_tool_and_the_extension_group()
    {
        var manifest = Load();

        Assert.Equal(P3AManifestSchema.SupportedVersion, manifest.ManifestVersion);
        Assert.Equal(
            new[] { "vscode", "vivado", "ghdl", "surfer", "vscode-extension" },
            manifest.Tools.Select(t => t.Id.Value));
        Assert.Equal(ManifestComponentKind.Extension, manifest.Tools[^1].Kind);
    }

    [Fact]
    public void Nothing_in_the_real_manifest_can_be_installed_automatically()
    {
        Assert.All(Load().Tools, tool =>
            Assert.True(tool.Remediation.Method is RemediationMethod.LinkToInstaller or RemediationMethod.None, tool.Id.Value));
    }

    [Theory]
    [InlineData("vscode")]
    [InlineData("ghdl")]
    public void Tools_with_no_remediation_in_the_manifest_have_no_automatic_fix(string type)
    {
        var plan = Plan(type, AuditIssueState.UnapprovedVersion);

        Assert.False(plan.IsSupported);
        Assert.Null(plan.Guidance);
        Assert.Contains("no automatic remediation", plan.UnsupportedReason);
    }

    [Fact]
    public void Vivado_is_never_installed_but_P3A_manual_steps_are_shown()
    {
        var plan = Plan("vivado");

        Assert.False(plan.IsSupported);
        Assert.Equal(ProhibitedSoftwarePolicy.VivadoMessage, plan.UnsupportedReason);
        Assert.NotNull(plan.Guidance);
        Assert.NotEmpty(plan.Guidance.Steps);
        Assert.True(P3AManifestSchema.IsWebAddress(plan.Guidance.DownloadUrl));
    }

    [Fact]
    public void Surfer_gets_its_download_page_and_steps_with_no_version_restriction()
    {
        var plan = Plan("surfer");

        Assert.False(plan.IsSupported);
        Assert.NotNull(plan.Guidance);
        Assert.NotEmpty(plan.Guidance.Steps);
        Assert.Contains("does not restrict the version", plan.UnsupportedReason);
    }

    [Fact]
    public void Extensions_are_not_installed_and_the_approved_ones_are_listed()
    {
        var plan = Plan("vscode-extension");

        Assert.False(plan.IsSupported);
        Assert.Contains("teros-technology.teroshdl", plan.UnsupportedReason);
    }

    [Fact]
    public void Approved_ranges_are_shown_exactly_as_the_manifest_gives_them()
    {
        var manifest = Load();
        var ghdl = manifest.FindTool(new ToolReference("ghdl"))!;

        Assert.Equal(
            $"{ghdl.ApprovedRange!.Minimum} to {ghdl.ApprovedRange.Maximum}",
            ghdl.ApprovedVersionText);
        Assert.Empty(ghdl.ApprovedVersions);
    }
}
