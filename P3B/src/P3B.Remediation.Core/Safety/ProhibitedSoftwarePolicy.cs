using System.Diagnostics.CodeAnalysis;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Safety;

/// <summary>
/// Software P3B must never install, activate or license, whatever the manifest says
/// (3B-DC-004, P3-AC2.2). This is a deliberate hard-coded safety rule, not a tool definition:
/// it only ever blocks actions, never enables them. P3B contains no licensing code at all.
/// </summary>
public static class ProhibitedSoftwarePolicy
{
    public const string VivadoMessage =
        "P3B never installs, activates or licenses AMD Vivado (3B-DC-004). Install Vivado manually by following the course instructions.";

    private static readonly string[] Markers = ["vivado", "xilinx"];

    public static bool IsProhibited(ManifestTool tool, [NotNullWhen(true)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(tool);

        string?[] fields = [tool.Id.Value, tool.DisplayName, tool.Remediation.Source];
        var prohibited = fields.Any(field =>
            field is not null && Markers.Any(marker => field.Contains(marker, StringComparison.OrdinalIgnoreCase)));

        reason = prohibited ? VivadoMessage : null;
        return prohibited;
    }
}
