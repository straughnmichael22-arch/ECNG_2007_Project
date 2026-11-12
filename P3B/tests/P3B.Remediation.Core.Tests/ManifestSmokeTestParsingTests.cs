using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests;

/// <summary>The PROVISIONAL <c>smokeTests</c> section of the manifest proposal.</summary>
public class ManifestSmokeTestParsingTests
{
    private static ManifestLoadResult Load(string smokeTestsJson)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteText(
            "manifest.json",
            $$"""
            {
              "manifestVersion": "p3b-provisional-0",
              "tools": [ {
                "id": "TEST-TOOL-A",
                "approvedVersions": [ "TEST-VERSION-1" ],
                "remediation": { "method": "winget", "source": "TEST-SOURCE-A" },
                "smokeTests": {{smokeTestsJson}}
              } ]
            }
            """);
        return new ManifestService(path).Load();
    }

    private static IReadOnlyList<SmokeCheckDefinition> Checks(ManifestLoadResult result)
    {
        Assert.True(result.IsUsable, result.Description);
        return result.Manifest.FindTool(new ToolReference("TEST-TOOL-A"))!.SmokeChecks;
    }

    [Fact]
    public void Fixture_manifest_declares_tool_specific_smoke_tests()
    {
        var manifest = new ManifestService(TestManifests.FixturePath("valid.provisional.json")).Load().Manifest;

        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.FindTool(new ToolReference(TestManifests.ToolA))!.SmokeChecks);
        Assert.Empty(manifest.FindTool(new ToolReference(TestManifests.NoFixTool))!.SmokeChecks);
    }

    [Fact]
    public void Every_supported_kind_is_read_with_its_settings()
    {
        var checks = Checks(Load("""
            [
              { "kind": "installedVersion" },
              { "kind": "commandOnPath", "command": "TEST-tool" },
              { "kind": "fileExists", "path": "%LOCALAPPDATA%\\TEST\\tool.exe" },
              { "kind": "versionCommand", "command": "TEST-tool.exe", "arguments": [ "--version" ], "timeoutSeconds": 15 },
              { "kind": "userEnvironmentVariable", "name": "Path", "contains": "C:\\TEST\\bin" }
            ]
            """));

        Assert.IsType<InstalledVersionCheck>(checks[0]);
        Assert.Equal("TEST-tool", Assert.IsType<CommandOnPathCheck>(checks[1]).Command);
        Assert.Equal(@"%LOCALAPPDATA%\TEST\tool.exe", Assert.IsType<FileExistsCheck>(checks[2]).Path);
        var version = Assert.IsType<VersionCommandCheck>(checks[3]);
        Assert.Equal("--version", Assert.Single(version.Arguments));
        Assert.Equal(TimeSpan.FromSeconds(15), version.Timeout);
        Assert.Equal(@"C:\TEST\bin", Assert.IsType<UserEnvironmentVariableCheck>(checks[4]).ExpectedContent);
    }

    [Fact]
    public void Version_command_defaults_to_no_arguments_and_the_default_timeout()
    {
        var check = Assert.IsType<VersionCommandCheck>(Assert.Single(Checks(Load("""[ { "kind": "versionCommand", "command": "TEST-tool" } ]"""))));

        Assert.Empty(check.Arguments);
        Assert.Equal(ProvisionalManifestSchema.DefaultSmokeTestTimeout, check.Timeout);
    }

    [Fact]
    public void Unknown_kind_is_kept_as_unrecognised_not_guessed()
    {
        var check = Assert.Single(Checks(Load("""[ { "kind": "TEST-future-kind" } ]""")));

        Assert.Equal("TEST-future-kind", Assert.IsType<UnrecognisedSmokeCheck>(check).Kind);
    }

    [Fact]
    public void Absent_smoke_tests_mean_none_are_declared()
    {
        Assert.Empty(Checks(Load("null")));
    }

    [Theory]
    [InlineData("""[ { "kind": "commandOnPath", "command": "..\\evil.exe" } ]""", "$.tools[0].smokeTests[0].command")]
    [InlineData("""[ { "kind": "commandOnPath", "command": "tool & calc" } ]""", "$.tools[0].smokeTests[0].command")]
    [InlineData("""[ { "kind": "versionCommand" } ]""", "$.tools[0].smokeTests[0].command")]
    [InlineData("""[ { "kind": "versionCommand", "command": "tool", "timeoutSeconds": 0 } ]""", "$.tools[0].smokeTests[0].timeoutSeconds")]
    [InlineData("""[ { "kind": "versionCommand", "command": "tool", "timeoutSeconds": 9999 } ]""", "$.tools[0].smokeTests[0].timeoutSeconds")]
    [InlineData("""[ { "kind": "versionCommand", "command": "tool", "arguments": [ 1 ] } ]""", "$.tools[0].smokeTests[0].arguments")]
    [InlineData("""[ { "kind": "fileExists", "path": "relative\\tool.exe" } ]""", "$.tools[0].smokeTests[0].path")]
    [InlineData("""[ { "kind": "userEnvironmentVariable", "name": "Path" } ]""", "$.tools[0].smokeTests[0].contains")]
    [InlineData("""[ { "command": "tool" } ]""", "$.tools[0].smokeTests[0].kind")]
    [InlineData("""{ "kind": "installedVersion" }""", "$.tools[0].smokeTests")]
    public void Invalid_smoke_test_makes_the_manifest_invalid_with_its_location(string smokeTests, string expectedPath)
    {
        var result = Load(smokeTests);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Path == expectedPath);
    }
}
