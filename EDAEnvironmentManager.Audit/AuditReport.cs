using System;
using System.Collections.Generic;
using System.Linq;

namespace EDAEnvironmentManager.Audit
{
    public enum ComponentKind
    {
        Tool,
        Extension
    }

    /// <summary>
    /// Why a component is or is not ready. Each value matches one of the reasons the audit
    /// window already shows, so other programs do not have to read the reason text.
    /// </summary>
    public enum AuditFinding
    {
        /// <summary>The component passed the audit.</summary>
        None,

        /// <summary>The executable was not found in PATH or the manifest search locations.</summary>
        NotInstalled,

        /// <summary>The executable was found, but its version could not be retrieved.</summary>
        VersionUnknown,

        /// <summary>The detected version number could not be interpreted.</summary>
        VersionUnparseable,

        /// <summary>The version is below the minimum approved version.</summary>
        VersionBelowMinimum,

        /// <summary>The version is above the maximum approved version.</summary>
        VersionAboveMaximum,

        /// <summary>The minimum approved version in the manifest is invalid.</summary>
        InvalidMinimumVersion,

        /// <summary>The maximum approved version in the manifest is invalid.</summary>
        InvalidMaximumVersion,

        /// <summary>The editor the extension belongs to was not detected.</summary>
        EditorNotDetected,

        /// <summary>None of the approved extensions is installed.</summary>
        ExtensionNotInstalled,

        /// <summary>The extension check is disabled in the manifest.</summary>
        CheckDisabled
    }

    /// <summary>The audit result for one tool or extension: what the result card shows.</summary>
    public sealed class ComponentAuditResult
    {
        public ComponentKind Kind { get; init; }

        /// <summary>The component's name in the manifest.</summary>
        public string Name { get; init; } = "";

        /// <summary>The component's type in the manifest, for example "ghdl".</summary>
        public string Type { get; init; } = "";

        /// <summary>The title shown on the result card.</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>"Detected", "Not Installed", "Not Detected" or "Not Checked".</summary>
        public string InstallationStatus { get; init; } = "";

        /// <summary>"Ready", "Not Ready" or "Not Checked".</summary>
        public string ReadinessStatus { get; init; } = "";

        /// <summary>The detected version, or "Not Available" / "Not Checked".</summary>
        public string Version { get; init; } = "";

        public string? InstalledPath { get; init; }

        public string Reason { get; init; } = "";

        public AuditFinding Finding { get; init; }

        /// <summary>Whether this component counts as ready for the overall result.</summary>
        public bool CountsAsReady { get; init; }

        public EdaTool? Tool { get; init; }

        public EdaExtension? Extension { get; init; }
    }

    /// <summary>The result of one complete audit.</summary>
    public sealed class AuditReport
    {
        public AuditReport(
            string manifestVersion,
            DateTimeOffset auditedAt,
            IEnumerable<ComponentAuditResult> components)
        {
            ManifestVersion = manifestVersion;
            AuditedAt = auditedAt;
            Components = components.ToList().AsReadOnly();
        }

        public string ManifestVersion { get; }

        public DateTimeOffset AuditedAt { get; }

        public IReadOnlyList<ComponentAuditResult> Components { get; }

        /// <summary>True when every component counts as ready.</summary>
        public bool OverallReady => Components.All(c => c.CountsAsReady);

        /// <summary>"Ready" or "Not Ready", as shown after "Overall Environment:".</summary>
        public string OverallStatus => OverallReady ? "Ready" : "Not Ready";
    }
}
