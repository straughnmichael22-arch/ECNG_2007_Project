using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>Returns a fixed load result. Holds only fake test tools.</summary>
internal sealed class InMemoryManifestSource(ManifestLoadResult result) : IManifestSource
{
    public static InMemoryManifestSource WithTestTools() =>
        new(ManifestLoadResult.Loaded(TestManifests.Default(), "TEST in-memory manifest (not the Local EDA Tool Manifest v1)"));

    public static InMemoryManifestSource Failing(ManifestLoadStatus status, string description) =>
        new(ManifestLoadResult.Failed(status, description));

    public ManifestLoadResult Load() => result;
}
