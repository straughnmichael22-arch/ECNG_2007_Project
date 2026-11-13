using EDAEnvironmentManager;
using EDAEnvironmentManager.Audit;

namespace EDAEnvironmentManager.Audit.Tests;

/// <summary>
/// P3A's audit rules that need no detection: version approval, manifest loading, the overall
/// Ready rule and the findings reached without looking at the computer. These tests start no
/// process and change nothing; they only read P3A's manifest and temporary files.
/// </summary>
public class AuditRulesTests
{
    [Theory]
    [InlineData("1.144.1", "1.100.0", "1.145.2", true)]
    [InlineData("1.100.0", "1.100.0", "1.145.2", true)]
    [InlineData("1.145.2", "1.100.0", "1.145.2", true)]
    [InlineData("1.99.9", "1.100.0", "1.145.2", false)]
    [InlineData("1.146.0", "1.100.0", "1.145.2", false)]
    [InlineData("2026.1", "2019.2", "2026.1.1", true)]
    [InlineData("not-a-version", "1.0", "2.0", false)]
    [InlineData("5.1.1", "", "", true)]
    public void Version_is_approved_only_inside_the_manifest_range(string installed, string minimum, string maximum, bool approved)
    {
        Assert.Equal(approved, EnvironmentAuditor.IsVersionApproved(installed, minimum, maximum));
    }

    [Theory]
    [InlineData("1.99.0", "1.100.0", "1.145.2", AuditFinding.VersionBelowMinimum, "below the minimum approved version (1.100.0)")]
    [InlineData("1.146.0", "1.100.0", "1.145.2", AuditFinding.VersionAboveMaximum, "above the maximum approved version (1.145.2)")]
    [InlineData("abc", "1.0", "2.0", AuditFinding.VersionUnparseable, "could not be interpreted")]
    [InlineData("1.5", "one", "2.0", AuditFinding.InvalidMinimumVersion, "minimum approved version in the manifest is invalid")]
    [InlineData("1.5", "1.0", "two", AuditFinding.InvalidMaximumVersion, "maximum approved version in the manifest is invalid")]
    [InlineData("1.5", "1.0", "2.0", AuditFinding.None, "within the approved range")]
    public void Each_version_problem_has_its_own_finding_and_reason(string installed, string minimum, string maximum, AuditFinding finding, string reason)
    {
        var result = EnvironmentAuditor.ClassifyVersion(installed, minimum, maximum, out var message);

        Assert.Equal(finding, result);
        Assert.Contains(reason, message);
    }

    [Fact]
    public void Real_manifest_loads_with_every_tool_and_extension_group()
    {
        var manifest = EdaManifestLoader.TryLoad(Path.Combine(AppContext.BaseDirectory, "eda-tool-manifest.json"), out var status);

        Assert.NotNull(manifest);
        Assert.Equal("Manifest Found. Ready to audit.", status);
        Assert.Equal("1.0", manifest.ManifestVersion);
        Assert.Equal(new[] { "vscode", "vivado", "ghdl", "surfer" }, manifest.Tools.Select(t => t.Type));
        Assert.Single(manifest.Extensions);
        Assert.All(manifest.Tools.Where(t => t.Type != "surfer"), t => Assert.False(string.IsNullOrWhiteSpace(t.MinimumApprovedVersion)));
    }

    [Fact]
    public void Missing_manifest_is_reported_and_nothing_is_audited()
    {
        var manifest = EdaManifestLoader.TryLoad(Path.Combine(Path.GetTempPath(), $"p3a-missing-{Guid.NewGuid():N}.json"), out var status);

        Assert.Null(manifest);
        Assert.Equal("Manifest Not Found.", status);
    }

    [Fact]
    public void Malformed_manifest_is_reported()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p3a-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"manifestVersion\": ");
        try
        {
            var manifest = EdaManifestLoader.TryLoad(path, out var status);

            Assert.Null(manifest);
            Assert.StartsWith("Error loading manifest:", status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Manifest_file_is_not_changed_by_loading()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "eda-tool-manifest.json");
        var before = File.ReadAllBytes(path);

        EdaManifestLoader.TryLoad(path, out _);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Tool_with_no_executable_or_search_locations_is_not_installed()
    {
        // No where command and no paths: nothing is searched and no process is started.
        var tool = new EdaTool { Name = "TEST tool", Type = "test", MinimumApprovedVersion = "1.0" };

        var result = new EnvironmentAuditor().AuditTool(tool);

        Assert.Equal(AuditFinding.NotInstalled, result.Finding);
        Assert.Equal("Not Installed", result.InstallationStatus);
        Assert.Equal("Not Ready", result.ReadinessStatus);
        Assert.False(result.CountsAsReady);
        Assert.Contains("not found", result.Reason);
    }

    [Fact]
    public void Disabled_extension_check_counts_as_ready_and_says_why()
    {
        var extension = new EdaExtension { Name = "TEST extensions", Type = "test-extension", CheckEnabled = false };

        var result = new EnvironmentAuditor().AuditExtension(extension, new EdaManifest());

        Assert.Equal(AuditFinding.CheckDisabled, result.Finding);
        Assert.True(result.CountsAsReady);
        Assert.Equal("Not Checked", result.ReadinessStatus);
    }

    [Fact]
    public void Extensions_cannot_be_checked_without_their_editor()
    {
        var extension = new EdaExtension { Name = "TEST extensions", Type = "test-extension", Editor = "TEST editor", CheckEnabled = true };

        var result = new EnvironmentAuditor().AuditExtension(extension, new EdaManifest());

        Assert.Equal(AuditFinding.EditorNotDetected, result.Finding);
        Assert.Equal("Not Detected", result.InstallationStatus);
        Assert.False(result.CountsAsReady);
    }

    [Fact]
    public void Overall_result_is_Ready_only_when_every_component_counts_as_ready()
    {
        ComponentAuditResult Ready() => new() { CountsAsReady = true };
        ComponentAuditResult NotReady() => new() { CountsAsReady = false };

        Assert.Equal("Ready", new AuditReport("1.0", DateTimeOffset.Now, [Ready(), Ready()]).OverallStatus);
        Assert.Equal("Not Ready", new AuditReport("1.0", DateTimeOffset.Now, [Ready(), NotReady()]).OverallStatus);
        Assert.True(new AuditReport("1.0", DateTimeOffset.Now, []).OverallReady);
    }

    [Fact]
    public void Audit_of_a_manifest_reports_every_tool_and_extension_in_order()
    {
        var manifest = new EdaManifest
        {
            ManifestVersion = "1.0",
            Tools = [new EdaTool { Name = "TEST A", Type = "a" }, new EdaTool { Name = "TEST B", Type = "b" }],
            Extensions = [new EdaExtension { Name = "TEST ext", Type = "ext", CheckEnabled = false }],
        };

        var report = new EnvironmentAuditor().Audit(manifest);

        Assert.Equal(new[] { "a", "b", "ext" }, report.Components.Select(c => c.Type));
        Assert.Equal("Not Ready", report.OverallStatus);
    }
}
