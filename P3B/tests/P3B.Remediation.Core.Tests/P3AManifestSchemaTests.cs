using System.Text;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// Reading P3A's manifest format 1.0. The JSON here uses P3A's real property names but fake
/// values; the real manifest is tested in P3B.Integration.P3A.Tests.
/// </summary>
public class P3AManifestSchemaTests
{
    private const string Valid = """
        {
          "manifestVersion": "1.0",
          "tools": [
            {
              "name": "Test tool A", "type": "test-a", "executable": "test-a.exe",
              "minimumApprovedVersion": "1.2.0", "maximumApprovedVersion": "1.9.9",
              "searchRoots": [ "%TEST%" ],
              "remediation": { "downloadUrl": "https://example.invalid/test-a", "instructions": [ "TEST step 1", "TEST step 2" ] }
            },
            { "name": "Test tool B", "type": "test-b", "minimumApprovedVersion": "", "maximumApprovedVersion": "" }
          ],
          "extensions": [
            { "name": "Test extensions", "type": "test-extension", "editor": "Test tool A",
              "approvedExtensions": [ "test.one", "test.two" ], "checkEnabled": true }
          ]
        }
        """;

    private static ManifestLoadResult Parse(string json) => ManifestFormats.Parse(Encoding.UTF8.GetBytes(json), "TEST");

    [Fact]
    public void P3A_manifest_is_loaded_with_tools_extensions_ranges_and_manual_steps()
    {
        var result = Parse(Valid);

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Contains("P3A manifest format 1.0", result.Description);
        var manifest = result.Manifest!;
        Assert.Equal(P3AManifestSchema.SupportedVersion, manifest.ManifestVersion);
        Assert.Equal(3, manifest.Tools.Count);

        var a = manifest.FindTool(new ToolReference("test-a"))!;
        Assert.Equal("Test tool A", a.Name);
        Assert.Empty(a.ApprovedVersions);
        Assert.Equal("1.2.0", a.ApprovedRange!.Minimum);
        Assert.Equal("1.9.9", a.ApprovedRange.Maximum);
        Assert.Equal("1.2.0 to 1.9.9", a.ApprovedVersionText);
        Assert.Equal(RemediationMethod.LinkToInstaller, a.Remediation.Method);
        Assert.Equal("https://example.invalid/test-a", a.Remediation.Source);
        Assert.Equal(new[] { "TEST step 1", "TEST step 2" }, a.Instructions);
        Assert.Equal(ManifestComponentKind.Tool, a.Kind);

        var b = manifest.FindTool(new ToolReference("test-b"))!;
        Assert.False(b.ApprovedRange!.HasBounds);
        Assert.Null(b.ApprovedVersionText);
        Assert.Equal(RemediationMethod.None, b.Remediation.Method);
        Assert.Empty(b.Instructions);

        var extension = manifest.FindTool(new ToolReference("test-extension"))!;
        Assert.Equal(ManifestComponentKind.Extension, extension.Kind);
        Assert.Equal(new[] { "test.one", "test.two" }, extension.ApprovedExtensions);
        Assert.Equal(RemediationMethod.None, extension.Remediation.Method);
    }

    [Fact]
    public void Property_names_are_matched_without_regard_to_case_as_P3A_does()
    {
        var result = Parse("""{ "ManifestVersion": "1.0", "Tools": [ { "Name": "Test", "TYPE": "test-a", "MinimumApprovedVersion": "2.0" } ] }""");

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal("2.0 or later", result.Manifest!.Tools[0].ApprovedVersionText);
    }

    [Fact]
    public void Extension_falls_back_to_extensionId_when_no_approved_list_is_given()
    {
        var result = Parse("""{ "manifestVersion": "1.0", "extensions": [ { "name": "Test", "type": "test-extension", "extensionId": "test.only" } ] }""");

        Assert.Equal(new[] { "test.only" }, result.Manifest!.Tools[0].ApprovedExtensions);
    }

    [Theory]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "type": "test-a" } ] }""", "$.tools[0].name")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "name": "Test" } ] }""", "$.tools[0].type")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "name": "Test", "type": "test-a", "minimumApprovedVersion": "TEST-NOT-A-VERSION" } ] }""", "$.tools[0].minimumApprovedVersion")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "name": "Test", "type": "test-a", "minimumApprovedVersion": "3.0", "maximumApprovedVersion": "2.0" } ] }""", "$.tools[0].minimumApprovedVersion")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "name": "Test", "type": "test-a", "remediation": { "downloadUrl": "C:\\TEST\\setup.exe" } } ] }""", "$.tools[0].remediation.downloadUrl")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": [ { "name": "Test", "type": "test-a", "remediation": { "instructions": [ "" ] } } ] }""", "$.tools[0].remediation.instructions")]
    [InlineData("""{ "manifestVersion": "1.0", "tools": "TEST" }""", "$.tools")]
    [InlineData("""{ "manifestVersion": "1.0", "extensions": [ { "name": "Test", "type": "test-extension", "approvedExtensions": [ 1 ] } ] }""", "$.extensions[0].approvedExtensions")]
    public void Invalid_P3A_manifest_is_reported_with_the_path_of_the_problem(string json, string path)
    {
        var result = Parse(json);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.False(result.IsUsable);
        Assert.Contains(result.Errors, e => e.Path == path);
    }

    [Fact]
    public void Two_components_with_the_same_type_are_invalid()
    {
        var result = Parse("""{ "manifestVersion": "1.0", "tools": [ { "name": "One", "type": "test-a" }, { "name": "Two", "type": "test-a" } ] }""");

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains("test-a", result.Description);
    }

    [Fact]
    public void A_manifest_with_no_tools_or_extensions_is_invalid()
    {
        var result = Parse("""{ "manifestVersion": "1.0", "tools": [], "extensions": [] }""");

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void Unknown_version_names_both_supported_formats()
    {
        var result = Parse("""{ "manifestVersion": "TEST-9.9", "tools": [] }""");

        Assert.Equal(ManifestLoadStatus.UnsupportedVersion, result.Status);
        Assert.Contains("'1.0'", result.Description);
        Assert.Contains(ProvisionalManifestSchema.SupportedVersion, result.Description);
    }

    [Fact]
    public void P3A_manifest_file_is_read_without_being_changed()
    {
        using var folder = new TempDirectory();
        var path = folder.WriteText("eda-tool-manifest.json", Valid);
        var before = File.ReadAllBytes(path);
        var written = File.GetLastWriteTimeUtc(path);

        var result = new ManifestService(path).Load();

        Assert.True(result.IsUsable);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Range_only_tool_cannot_be_given_an_installing_method()
    {
        Assert.Throws<ArgumentException>(() => new ManifestTool(
            new ToolReference("test-a"), "Test", [], new ManifestRemediation(RemediationMethod.WinGet, "TEST.Package")));
    }
}
