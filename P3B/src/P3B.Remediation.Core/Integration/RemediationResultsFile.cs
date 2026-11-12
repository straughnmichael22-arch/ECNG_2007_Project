using System.Text.Json;

namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Writes P3B's results for P3A to re-audit from: the latest <see cref="RemediationReport"/> per
/// issue, plus a statement that readiness is P3A's decision.
/// </summary>
/// <remarks>PROVISIONAL – the file hand-off is P3B's proposal for the shared contract.</remarks>
public static class RemediationResultsFile
{
    public const string FormatVersion = "p3b-provisional-0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAsync(
        string path,
        IEnumerable<RemediationReport> reports,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reports);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var document = new ResultsDocument(FormatVersion, generatedAt, RemediationReport.ReadinessStatement, reports.ToArray());
        var temporaryPath = fullPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public sealed record ResultsDocument(
        string FormatVersion,
        DateTimeOffset GeneratedAt,
        string Readiness,
        IReadOnlyList<RemediationReport> Reports);
}
