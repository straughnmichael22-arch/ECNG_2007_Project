using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Read-only access to the Local EDA Tool Manifest.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ManifestService"/> (file) and <see cref="UnconfiguredManifestSource"/>.
/// <para>
/// Read-only by design (P3B-DR-02): this interface must never gain members that write,
/// save or otherwise modify the manifest. Enforced by ManifestReadOnlyTests.
/// </para>
/// </remarks>
public interface IManifestSource
{
    /// <summary>
    /// Loads and validates the manifest. Called before any remediation work so that a
    /// missing, unreadable or invalid manifest is reported first (3B-IR-003).
    /// </summary>
    ManifestLoadResult Load();
}
