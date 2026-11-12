using System.Text.Json;

namespace P3B.Remediation.Core.Integration;

/// <summary>The result of reading a P3A audit-results file.</summary>
public sealed record AuditResultsLoad(bool IsLoaded, string Description, IReadOnlyList<AuditIssue> Issues);

/// <summary>
/// Reads P3A audit results from a JSON file: <c>{ "issues": [ { "issueId", "toolId", "state",
/// "detectedVersion", "detail" } ] }</c>. Each issue is validated later by
/// <see cref="RemediationRequestFactory"/>, so one incomplete issue does not hide the others.
/// </summary>
/// <remarks>
/// PROVISIONAL – a file hand-off from P3A is P3B's proposal (see
/// docs/proposals/audit-remediation-contract.PROVISIONAL.md). The file is only read.
/// </remarks>
public static class AuditResultsFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static AuditResultsLoad Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return Failed($"No audit results file was found at {fullPath}.");
        }

        AuditDocument? document;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = JsonSerializer.Deserialize<AuditDocument>(stream, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return Failed($"The audit results file at {fullPath} is not in the expected format: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed($"The audit results file at {fullPath} could not be read: {ex.Message}");
        }

        if (document?.Issues is null)
        {
            return Failed($"The audit results file at {fullPath} has no \"issues\" list.");
        }

        var issues = document.Issues.Select(issue => issue ?? new AuditIssue()).ToArray();
        return new AuditResultsLoad(true, $"Loaded {issues.Length} issue(s) from {fullPath}.", issues);
    }

    private static AuditResultsLoad Failed(string description) => new(false, description, []);

    internal sealed record AuditDocument(List<AuditIssue?>? Issues);
}
