using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// Manual remediation steps taken word for word from the manifest, for an issue P3B cannot fix
/// automatically. P3B shows them; the user performs them; P3A's re-audit checks the result.
/// </summary>
public sealed record ManualGuidance
{
    private ManualGuidance(string? downloadUrl, IReadOnlyList<string> steps)
    {
        DownloadUrl = downloadUrl;
        Steps = steps;
    }

    /// <summary>The official download page from the manifest, if any (always http or https).</summary>
    public string? DownloadUrl { get; }

    /// <summary>The manifest's steps, in order.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The manifest's guidance for a tool, or null when the manifest gives none.</summary>
    public static ManualGuidance? From(ManifestTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var url = tool.Remediation.Method == RemediationMethod.LinkToInstaller
            && P3AManifestSchema.IsWebAddress(tool.Remediation.Source)
                ? tool.Remediation.Source
                : null;

        return url is null && tool.Instructions.Count == 0
            ? null
            : new ManualGuidance(url, tool.Instructions);
    }
}
