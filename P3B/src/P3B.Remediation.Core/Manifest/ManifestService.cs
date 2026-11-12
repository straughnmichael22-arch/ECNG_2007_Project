using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// Loads the manifest from a file, read-only, every time <see cref="Load"/> is called
/// (3B-IR-001). The file is opened with read access and read sharing only; P3B never writes,
/// moves or renames it (P3B-DR-02).
/// </summary>
/// <remarks>
/// Validation is delegated to <see cref="ManifestFormats"/>: P3A's manifest format 1.0, or the
/// provisional test format.
/// </remarks>
public sealed class ManifestService : IManifestSource
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public ManifestService(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ManifestPath = Path.GetFullPath(manifestPath);
    }

    public string ManifestPath { get; }

    public ManifestLoadResult Load()
    {
        if (!File.Exists(ManifestPath))
        {
            return ManifestLoadResult.Failed(
                ManifestLoadStatus.Missing,
                $"No manifest file was found at {ManifestPath}.");
        }

        byte[] content;
        try
        {
            content = ReadReadOnly(ManifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ManifestLoadResult.Failed(
                ManifestLoadStatus.Unreadable,
                $"The manifest at {ManifestPath} could not be read: {ex.Message}");
        }

        return ManifestFormats.Parse(WithoutUtf8Bom(content), ManifestPath);
    }

    private static byte[] ReadReadOnly(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static ReadOnlyMemory<byte> WithoutUtf8Bom(byte[] content) =>
        content.AsSpan().StartsWith(Utf8Bom) ? content.AsMemory(Utf8Bom.Length) : content.AsMemory();
}
