using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// In-code TEST manifests using the provisional model. All identifiers, versions and sources
/// are obviously fake; none is a real tool, version or package.
/// </summary>
internal static class TestManifests
{
    public const string ToolA = "TEST-TOOL-A";
    public const string ToolB = "TEST-TOOL-B";
    public const string NoFixTool = "TEST-TOOL-NO-FIX";
    public const string NotInManifestTool = "TEST-TOOL-NOT-IN-MANIFEST";
    public const string TargetVersion = "TEST-VERSION-1";

    public static LocalEdaToolManifest Default() => new(
        ProvisionalManifestSchema.SupportedVersion,
        [Tool(ToolA), Tool(ToolB), Tool(NoFixTool, RemediationMethod.None)]);

    public static ManifestTool Tool(
        string id,
        RemediationMethod method = RemediationMethod.WinGet,
        IEnumerable<SmokeCheckDefinition>? smokeChecks = null) => new(
        new ToolReference(id),
        $"Test tool {id}",
        [TargetVersion, "TEST-VERSION-0"],
        new ManifestRemediation(method, method == RemediationMethod.None ? null : $"TEST-SOURCE-{id}"),
        smokeChecks);

    public static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "manifests", fileName);
}
