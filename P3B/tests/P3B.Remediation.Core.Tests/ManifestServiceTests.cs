using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// ManifestService against TEST fixtures written in the PROVISIONAL format. Re-target these
/// at real fixtures once the Local EDA Tool Manifest v1 is supplied.
/// </summary>
public class ManifestServiceTests
{
    [Fact]
    public void Valid_manifest_loads_tool_definitions()
    {
        var result = new ManifestService(TestManifests.FixturePath("valid.provisional.json")).Load();

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.True(result.IsUsable);
        Assert.Empty(result.Errors);
        Assert.Equal(ProvisionalManifestSchema.SupportedVersion, result.Manifest.ManifestVersion);
        Assert.Equal(2, result.Manifest.Tools.Count);

        var tool = Assert.IsType<ManifestTool>(result.Manifest.FindTool(new ToolReference(TestManifests.ToolA)));
        Assert.Equal("Test tool A", tool.DisplayName);
        Assert.Equal(TestManifests.TargetVersion, tool.ApprovedVersions[0]);
        Assert.Equal(RemediationMethod.WinGet, tool.Remediation.Method);
        Assert.Equal("TEST-SOURCE-A", tool.Remediation.Source);

        var noFix = Assert.IsType<ManifestTool>(result.Manifest.FindTool(new ToolReference(TestManifests.NoFixTool)));
        Assert.Equal(RemediationMethod.None, noFix.Remediation.Method);
        Assert.Null(noFix.Remediation.Source);
    }

    [Fact]
    public void Missing_manifest_file_is_reported_as_missing()
    {
        using var temp = new TempDirectory();
        var path = temp.PathOf("does-not-exist.json");

        var result = new ManifestService(path).Load();

        Assert.Equal(ManifestLoadStatus.Missing, result.Status);
        Assert.False(result.IsUsable);
        Assert.Null(result.Manifest);
        Assert.Contains(path, result.Description);
    }

    [Fact]
    public void Malformed_json_is_reported_as_malformed()
    {
        var result = new ManifestService(TestManifests.FixturePath("malformed.json")).Load();

        Assert.Equal(ManifestLoadStatus.Malformed, result.Status);
        Assert.False(result.IsUsable);
    }

    [Fact]
    public void Missing_required_fields_are_reported_with_their_locations()
    {
        var result = new ManifestService(TestManifests.FixturePath("missing-required-fields.json")).Load();

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.False(result.IsUsable);
        Assert.Contains(result.Errors, e => e.Path == "$.tools[0].id");
        Assert.Contains(result.Errors, e => e.Path == "$.tools[0].approvedVersions");
        Assert.Contains(result.Errors, e => e.Path == "$.tools[0].remediation.source");
        Assert.Contains("$.tools[0].id", result.Description);
    }

    [Fact]
    public void Unsupported_manifest_version_is_rejected_rather_than_guessed()
    {
        var result = new ManifestService(TestManifests.FixturePath("unsupported-version.json")).Load();

        Assert.Equal(ManifestLoadStatus.UnsupportedVersion, result.Status);
        Assert.Contains("TEST-UNKNOWN-VERSION", result.Description);
    }

    [Fact]
    public void Unknown_remediation_method_is_invalid()
    {
        var result = new ManifestService(TestManifests.FixturePath("unknown-remediation-method.json")).Load();

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("$.tools[0].remediation.method", error.Path);
        Assert.Contains("TEST-UNKNOWN-METHOD", error.Message);
    }

    [Theory]
    [InlineData("[]", "$")]
    [InlineData("{ \"tools\": [] }", "$.manifestVersion")]
    [InlineData("{ \"manifestVersion\": \"p3b-provisional-0\" }", "$.tools")]
    [InlineData("{ \"manifestVersion\": \"p3b-provisional-0\", \"tools\": [] }", "$.tools")]
    [InlineData("{ \"manifestVersion\": \"p3b-provisional-0\", \"tools\": [ 42 ] }", "$.tools[0]")]
    [InlineData("{ \"manifestVersion\": \"p3b-provisional-0\", \"tools\": [ { \"id\": \"TEST-TOOL-A\", \"approvedVersions\": \"TEST-VERSION-1\", \"remediation\": { \"method\": \"none\" } } ] }", "$.tools[0].approvedVersions")]
    public void Structurally_invalid_manifest_reports_the_offending_location(string json, string expectedPath)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteText("manifest.json", json);

        var result = new ManifestService(path).Load();

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Path == expectedPath);
    }

    [Fact]
    public void Duplicate_tool_ids_are_invalid()
    {
        using var temp = new TempDirectory();
        const string Tool = "{ \"id\": \"TEST-TOOL-A\", \"approvedVersions\": [ \"TEST-VERSION-1\" ], \"remediation\": { \"method\": \"none\" } }";
        var path = temp.WriteText("manifest.json", $"{{ \"manifestVersion\": \"p3b-provisional-0\", \"tools\": [ {Tool}, {Tool} ] }}");

        var result = new ManifestService(path).Load();

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Path == "$.tools[1].id");
    }

    [Fact]
    public void Manifest_with_utf8_byte_order_mark_loads()
    {
        using var temp = new TempDirectory();
        var path = temp.PathOf("manifest.json");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(TestManifests.FixturePath("valid.provisional.json"))]);

        var result = new ManifestService(path).Load();

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
    }

    [Fact]
    public void Manifest_locked_by_another_writer_is_reported_as_unreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.CopyFixture("valid.provisional.json");

        ManifestLoadResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = new ManifestService(path).Load();
        }

        Assert.Equal(ManifestLoadStatus.Unreadable, result.Status);
        Assert.False(result.IsUsable);
    }

    [Fact]
    public void Unconfigured_source_reports_not_configured()
    {
        var result = new UnconfiguredManifestSource().Load();

        Assert.Equal(ManifestLoadStatus.NotConfigured, result.Status);
        Assert.False(result.IsUsable);
    }
}
