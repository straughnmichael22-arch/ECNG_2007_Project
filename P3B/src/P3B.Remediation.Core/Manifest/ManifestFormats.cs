using System.Text.Json;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// Chooses the reader for a manifest from its <c>manifestVersion</c>: P3A's format "1.0"
/// (<see cref="P3AManifestSchema"/>), otherwise the provisional format
/// (<see cref="ProvisionalManifestSchema"/>), which rejects any version it does not know.
/// </summary>
public static class ManifestFormats
{
    /// <param name="utf8Json">The manifest content (UTF-8, without a byte-order mark).</param>
    /// <param name="source">Where the content came from, for messages.</param>
    public static ManifestLoadResult Parse(ReadOnlyMemory<byte> utf8Json, string source)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json);
        }
        catch (JsonException ex)
        {
            return ManifestLoadResult.Failed(
                ManifestLoadStatus.Malformed,
                $"The manifest at {source} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            return P3AManifestSchema.ReadVersion(document.RootElement) == P3AManifestSchema.SupportedVersion
                ? P3AManifestSchema.Validate(document.RootElement, source)
                : ProvisionalManifestSchema.Validate(document.RootElement, source);
        }
    }
}
