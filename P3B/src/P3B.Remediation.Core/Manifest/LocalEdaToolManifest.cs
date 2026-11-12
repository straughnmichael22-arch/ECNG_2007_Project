using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// The parts of the Local EDA Tool Manifest that P3B uses. Immutable reference data:
/// P3B never modifies it (P3B-DR-02).
/// </summary>
/// <remarks>
/// PROVISIONAL – shaped by the P3B proposal, not by a supplied v1 schema. See
/// docs/proposals/local-eda-tool-manifest.PROVISIONAL.md.
/// </remarks>
public sealed record LocalEdaToolManifest
{
    public LocalEdaToolManifest(string manifestVersion, IEnumerable<ManifestTool> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestVersion);
        ArgumentNullException.ThrowIfNull(tools);

        var list = tools.ToArray();
        var duplicate = list.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Tool '{duplicate.Key}' is defined more than once.", nameof(tools));
        }

        ManifestVersion = manifestVersion;
        Tools = Array.AsReadOnly(list);
    }

    public string ManifestVersion { get; }

    public IReadOnlyList<ManifestTool> Tools { get; }

    public ManifestTool? FindTool(ToolReference id) => Tools.FirstOrDefault(t => t.Id == id);
}
