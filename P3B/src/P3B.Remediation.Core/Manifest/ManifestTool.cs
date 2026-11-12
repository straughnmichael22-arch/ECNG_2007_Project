using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Manifest;

/// <summary>Whether a manifest entry is a tool or a group of editor extensions.</summary>
public enum ManifestComponentKind
{
    Tool,
    Extension,
}

/// <summary>
/// One supported tool (or extension group), as P3B reads it from the manifest. Immutable
/// (P3B-DR-02). Read from P3A's manifest format 1.0 by <see cref="P3AManifestSchema"/>, or from
/// the provisional test format by <see cref="ProvisionalManifestSchema"/>.
/// </summary>
public sealed record ManifestTool
{
    public ManifestTool(
        ToolReference id,
        string? displayName,
        IEnumerable<string> approvedVersions,
        ManifestRemediation remediation,
        IEnumerable<SmokeCheckDefinition>? smokeChecks = null,
        ApprovedVersionRange? approvedRange = null,
        IEnumerable<string>? instructions = null,
        ManifestComponentKind kind = ManifestComponentKind.Tool,
        IEnumerable<string>? approvedExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(approvedVersions);
        ArgumentNullException.ThrowIfNull(remediation);

        var versions = approvedVersions.ToArray();
        if (versions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("No approved version may be blank.", nameof(approvedVersions));
        }

        // P3B only installs an exact version, so an installing method needs one.
        if (versions.Length == 0 && remediation.Method is RemediationMethod.WinGet or RemediationMethod.OfficialArchive or RemediationMethod.VsCodeExtension)
        {
            throw new ArgumentException(
                "At least one approved version is required when the manifest gives an installation source.",
                nameof(approvedVersions));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown component kind.");
        }

        Id = id;
        DisplayName = displayName;
        ApprovedVersions = Array.AsReadOnly(versions);
        Remediation = remediation;
        SmokeChecks = Array.AsReadOnly((smokeChecks ?? []).ToArray());
        ApprovedRange = approvedRange;
        Instructions = Array.AsReadOnly((instructions ?? []).ToArray());
        Kind = kind;
        ApprovedExtensions = Array.AsReadOnly((approvedExtensions ?? []).ToArray());
    }

    public ToolReference Id { get; }

    public string? DisplayName { get; }

    /// <summary>
    /// Exact course-approved versions in manifest order; the first entry is the version P3B
    /// installs. Empty when the manifest only gives an approved range (P3A's format).
    /// </summary>
    public IReadOnlyList<string> ApprovedVersions { get; }

    /// <summary>The approved version range, when the manifest gives one (P3A's format).</summary>
    public ApprovedVersionRange? ApprovedRange { get; }

    public ManifestRemediation Remediation { get; }

    /// <summary>Manual remediation steps from the manifest, in order. P3B shows them; it never performs them.</summary>
    public IReadOnlyList<string> Instructions { get; }

    public ManifestComponentKind Kind { get; }

    /// <summary>For an extension group: the approved extension identifiers, in manifest order.</summary>
    public IReadOnlyList<string> ApprovedExtensions { get; }

    /// <summary>The post-remediation checks declared for this tool, in manifest order. Empty if none are declared.</summary>
    public IReadOnlyList<SmokeCheckDefinition> SmokeChecks { get; }

    public string Name => DisplayName ?? Id.Value;

    /// <summary>The approved versions as a short phrase for the user, or null if the manifest sets none.</summary>
    public string? ApprovedVersionText =>
        ApprovedVersions.Count > 0 ? string.Join(", ", ApprovedVersions) : ApprovedRange?.Describe();
}

/// <summary>
/// An approved version range exactly as the manifest gives it. Either bound may be absent.
/// </summary>
public sealed record ApprovedVersionRange
{
    public ApprovedVersionRange(string? minimum, string? maximum)
    {
        Minimum = string.IsNullOrWhiteSpace(minimum) ? null : minimum;
        Maximum = string.IsNullOrWhiteSpace(maximum) ? null : maximum;
    }

    public string? Minimum { get; }

    public string? Maximum { get; }

    public bool HasBounds => Minimum is not null || Maximum is not null;

    /// <summary>For example "2019.2 to 2026.1.1"; null when neither bound is set.</summary>
    public string? Describe() => (Minimum, Maximum) switch
    {
        (not null, not null) => $"{Minimum} to {Maximum}",
        (not null, null) => $"{Minimum} or later",
        (null, not null) => $"up to {Maximum}",
        _ => null,
    };
}
