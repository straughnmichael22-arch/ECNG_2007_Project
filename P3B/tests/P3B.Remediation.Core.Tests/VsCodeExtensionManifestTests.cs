using System.Text;
using System.Text.Json;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests;

/// <summary>The provisional <c>vscodeExtension</c> method: manifest, planning and journal format. Fake values only.</summary>
public class VsCodeExtensionManifestTests
{
    private static ManifestLoadResult Parse(string source, string versions = "\"1.2.3\"") => ManifestFormats.Parse(
        Encoding.UTF8.GetBytes($$"""
            {
              "manifestVersion": "p3b-provisional-0",
              "tools": [
                { "id": "TEST-EXTENSION", "displayName": "Test extension", "approvedVersions": [ {{versions}} ],
                  "remediation": { "method": "vscodeExtension", "source": "{{source}}" } }
              ]
            }
            """),
        "TEST");

    [Fact]
    public void Extension_with_an_identifier_and_exact_version_is_read_as_an_installable_extension()
    {
        var result = Parse("test-publisher.test-extension");

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        var tool = result.Manifest!.Tools[0];
        Assert.Equal(RemediationMethod.VsCodeExtension, tool.Remediation.Method);
        Assert.Equal(ManifestComponentKind.Extension, tool.Kind);
        Assert.Equal("test-publisher.test-extension", tool.Remediation.Source);
    }

    [Theory]
    [InlineData("not-an-extension-id")]
    [InlineData("test&publisher.x")]
    [InlineData("--force")]
    public void Source_that_is_not_an_extension_identifier_is_invalid(string source)
    {
        var result = Parse(source);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Path == "$.tools[0].remediation.source");
    }

    [Fact]
    public void Planner_supports_an_extension_with_an_exact_version_and_targets_it()
    {
        var manifest = Parse("test-publisher.test-extension").Manifest!;

        var plan = RemediationPlanner.Plan(TestSupport.Request("TEST-EXTENSION"), manifest);

        Assert.True(plan.IsSupported);
        Assert.Equal("1.2.3", plan.Action.TargetVersion);
        Assert.Equal(RemediationMethod.VsCodeExtension, plan.Action.Method);
    }

    [Fact]
    public void Extension_install_cannot_be_defined_without_an_exact_version()
    {
        Assert.Throws<ArgumentException>(() => new ManifestTool(
            new ToolReference("TEST-EXTENSION"), null, [], new ManifestRemediation(RemediationMethod.VsCodeExtension, "test.extension")));
    }

    [Theory]
    [InlineData("publisher.name", true)]
    [InlineData("Publisher-1.Name-2", true)]
    [InlineData("publisher", false)]
    [InlineData("pub.name.extra", false)]
    [InlineData("pub.name@1.0.0", false)]
    [InlineData(" pub.name", false)]
    public void Only_plain_publisher_name_identifiers_are_accepted(string id, bool valid)
    {
        Assert.Equal(valid, VsCodeExtensionId.IsValid(id));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3-beta1", true)]
    [InlineData("1", false)]
    [InlineData("latest", false)]
    [InlineData("1.2.3 --force", false)]
    public void Only_plain_versions_are_accepted(string version, bool valid)
    {
        Assert.Equal(valid, VsCodeExtensionId.IsValidVersion(version));
    }

    [Fact]
    public void Extension_uninstall_reversal_survives_the_journal_format()
    {
        ReversalOperation operation = new VsCodeExtensionUninstallReversal("test-publisher.test-extension", "1.2.3");

        var json = JsonSerializer.Serialize(operation);
        var back = JsonSerializer.Deserialize<ReversalOperation>(json);

        Assert.Contains("vscodeExtensionUninstall", json);
        Assert.Equal(operation, back);
    }
}
