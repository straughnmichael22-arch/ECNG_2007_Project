using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Integration;

/// <summary>
/// The manifest source used when no manifest location is configured. Always reports that
/// no manifest is available, so every remediation request is refused as invalid configuration.
/// </summary>
public sealed class UnconfiguredManifestSource : IManifestSource
{
    public const string Description =
        "No Local EDA Tool Manifest is configured. Remediation is disabled until the course manifest is integrated.";

    public ManifestLoadResult Load() => ManifestLoadResult.Failed(ManifestLoadStatus.NotConfigured, Description);
}
