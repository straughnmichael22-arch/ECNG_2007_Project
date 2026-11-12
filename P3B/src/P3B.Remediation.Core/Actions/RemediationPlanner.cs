using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Safety;

namespace P3B.Remediation.Core.Actions;

/// <summary>
/// Decides whether a request has a supported automatic fix, using only the manifest.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>a tool not defined in the manifest is unsupported;</item>
/// <item>Vivado is always unsupported (3B-DC-004, a fixed safety rule); the manifest's manual steps are still shown;</item>
/// <item>an extension group is unsupported unless the manifest gives a <c>vscodeExtension</c> method with an exact approved version (P3A's manifest does not);</item>
/// <item>a tool whose remediation method is <c>none</c> is unsupported (3B-UR-003), with the manifest's manual steps if it gives any;</item>
/// <item>a <c>linkToInstaller</c> tool is unsupported, with its download page and manual steps;</item>
/// <item>a misconfigured tool is unsupported: P3A's audit reports no misconfiguration that is a user-level setting P3B could change;</item>
/// <item>missing and unapproved-version tools with an installation source get an action targeting the approved version.</item>
/// </list>
/// </remarks>
public static class RemediationPlanner
{
    public static RemediationPlan Plan(RemediationRequest request, LocalEdaToolManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(manifest);

        var tool = manifest.FindTool(request.Tool);
        if (tool is null)
        {
            return RemediationPlan.Unsupported($"Tool '{request.Tool}' is not defined in the Local EDA Tool Manifest.");
        }

        var guidance = ManualGuidance.From(tool);

        if (ProhibitedSoftwarePolicy.IsProhibited(tool, out var prohibited))
        {
            return RemediationPlan.Unsupported(prohibited, guidance);
        }

        if (tool.Kind == ManifestComponentKind.Extension && tool.Remediation.Method != RemediationMethod.VsCodeExtension)
        {
            var approved = tool.ApprovedExtensions.Count == 0
                ? "the manifest lists no approved extensions."
                : $"install one of the approved extensions from the manifest in VS Code yourself: {string.Join(", ", tool.ApprovedExtensions)}.";
            return RemediationPlan.Unsupported(
                $"P3B can install a VS Code extension only when the manifest gives its identifier and one exact approved version, and this manifest entry does not. To fix this, {approved}",
                guidance);
        }

        if (tool.Remediation.Method == RemediationMethod.None)
        {
            return RemediationPlan.Unsupported(
                guidance is null
                    ? $"The manifest defines no automatic remediation for {tool.Name}."
                    : $"The manifest defines no automatic remediation for {tool.Name}. Follow the manual steps from the manifest.",
                guidance);
        }

        if (tool.Remediation.Method == RemediationMethod.LinkToInstaller)
        {
            return RemediationPlan.Unsupported(
                $"P3B does not run the installer for {tool.Name}. Install {VersionToInstall(tool)} yourself from: {tool.Remediation.Source}",
                guidance);
        }

        if (request.State == AuditIssueState.Misconfigured)
        {
            return RemediationPlan.Unsupported($"No automatic fix is defined yet for a misconfigured {tool.Name}.", guidance);
        }

        return RemediationPlan.Supported(new RemediationAction(request, tool));
    }

    private static string VersionToInstall(ManifestTool tool)
    {
        if (tool.ApprovedVersions.Count > 0)
        {
            return $"version {tool.ApprovedVersions[0]}";
        }

        return tool.ApprovedRange?.Describe() is { } range
            ? $"an approved version ({range})"
            : "it (the manifest does not restrict the version)";
    }
}
