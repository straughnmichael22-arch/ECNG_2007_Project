using EDAEnvironmentManager.Audit;
using P3B.Remediation.Core.Integration;

namespace P3B.Integration.P3A;

/// <summary>
/// Converts P3A's audit results into P3B's <see cref="AuditIssue"/> and P3A's overall verdict.
/// Only values P3A actually produced are passed on; nothing is invented.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>NotInstalled, ExtensionNotInstalled, EditorNotDetected</term><description><c>missing</c></description></item>
/// <item><term>VersionBelowMinimum, VersionAboveMaximum</term><description><c>unapprovedVersion</c>, with P3A's detected version</description></item>
/// <item><term>anything else that is not ready</term><description>P3A's finding code is passed on as the state, so
/// <see cref="RemediationRequestFactory"/> reports it as incomplete audit data</description></item>
/// </list>
/// IssueId is <c>"{type}/{finding}"</c>; ToolId is the manifest <c>type</c>.
/// </remarks>
public static class P3AFindingMapper
{
    /// <summary>P3A's placeholders for "no version", which are not versions.</summary>
    private static readonly string[] NoVersion = ["Not Available", "Not Checked", "Unknown"];

    public static AuditIssue? ToAuditIssue(ComponentAuditResult component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.CountsAsReady)
        {
            return null;
        }

        var type = string.IsNullOrWhiteSpace(component.Type) ? null : component.Type;
        return new AuditIssue
        {
            IssueId = type is null ? null : $"{type}/{component.Finding}",
            ToolId = type,
            State = component.Finding switch
            {
                AuditFinding.NotInstalled or AuditFinding.ExtensionNotInstalled or AuditFinding.EditorNotDetected => "missing",
                AuditFinding.VersionBelowMinimum or AuditFinding.VersionAboveMaximum => "unapprovedVersion",

                // No P3B state means this; RemediationRequestFactory reports it as incomplete audit data.
                _ => component.Finding.ToString(),
            },
            DetectedVersion = RealVersion(component.Version),
            Detail = Detail(component),
        };
    }

    public static IReadOnlyList<AuditIssue> ToAuditIssues(AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Components.Select(ToAuditIssue).OfType<AuditIssue>().ToArray();
    }

    /// <summary>P3A's verdict and results. The verdict is P3A's OverallReady, nothing else.</summary>
    public static ReadinessAudit ToReadinessAudit(AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new ReadinessAudit(
            report.OverallReady ? ReadinessVerdict.Ready : ReadinessVerdict.NotReady,
            $"Audit completed. {report.Components.Count} components checked. Overall Environment: {report.OverallStatus}",
            report.AuditedAt,
            ToAuditIssues(report),
            report.Components.Select(c => new ReadinessComponent(
                c.DisplayName,
                c.ReadinessStatus,
                c.InstallationStatus,
                c.Version,
                string.IsNullOrWhiteSpace(c.Reason) ? null : c.Reason)));
    }

    private static string? RealVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) || NoVersion.Contains(version, StringComparer.OrdinalIgnoreCase) ? null : version;

    private static string? Detail(ComponentAuditResult component)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(component.Reason))
        {
            parts.Add(component.Reason);
        }

        if (!string.IsNullOrWhiteSpace(component.InstalledPath))
        {
            parts.Add($"Found at: {component.InstalledPath}");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
