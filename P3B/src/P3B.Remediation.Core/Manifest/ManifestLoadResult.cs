using System.Diagnostics.CodeAnalysis;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// The result of loading the manifest: either a validated manifest or a reason it cannot be
/// used, suitable for reporting before any remediation starts (3B-IR-003).
/// </summary>
public sealed record ManifestLoadResult
{
    private ManifestLoadResult(
        ManifestLoadStatus status,
        string description,
        LocalEdaToolManifest? manifest,
        IEnumerable<ManifestValidationError> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Status = status;
        Description = description;
        Manifest = manifest;
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public ManifestLoadStatus Status { get; }

    /// <summary>User-facing summary of the load result.</summary>
    public string Description { get; }

    /// <summary>The validated manifest; null unless <see cref="Status"/> is Loaded.</summary>
    public LocalEdaToolManifest? Manifest { get; }

    public IReadOnlyList<ManifestValidationError> Errors { get; }

    [MemberNotNullWhen(true, nameof(Manifest))]
    public bool IsUsable => Manifest is not null;

    public static ManifestLoadResult Loaded(LocalEdaToolManifest manifest, string description)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new ManifestLoadResult(ManifestLoadStatus.Loaded, description, manifest, Array.Empty<ManifestValidationError>());
    }

    public static ManifestLoadResult Failed(
        ManifestLoadStatus status,
        string description,
        IEnumerable<ManifestValidationError>? errors = null)
    {
        if (status == ManifestLoadStatus.Loaded)
        {
            throw new ArgumentException("A failed load cannot have the Loaded status.", nameof(status));
        }

        return new ManifestLoadResult(status, description, manifest: null, errors ?? Array.Empty<ManifestValidationError>());
    }
}
