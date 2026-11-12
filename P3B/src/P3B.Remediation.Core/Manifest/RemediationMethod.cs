namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// How a tool may be remediated.
/// </summary>
/// <remarks>
/// PROVISIONAL – proposed by P3B, not part of a supplied specification. Values are derived
/// from 3B-IR-004 / 3B-DC-002 (WinGet, official release archives) and the P3B brief
/// ("VS Code/Vivado may be linked to their normal installers rather than silently installed").
/// See docs/proposals/local-eda-tool-manifest.PROVISIONAL.md.
/// </remarks>
public enum RemediationMethod
{
    /// <summary>Install the approved version using WinGet.</summary>
    WinGet,

    /// <summary>Install the approved version from an official release archive.</summary>
    OfficialArchive,

    /// <summary>
    /// Install exactly one approved version of a VS Code extension for the current user. The
    /// source is the extension identifier (<c>publisher.name</c>).
    /// </summary>
    VsCodeExtension,

    /// <summary>Direct the user to the tool's normal installer; P3B does not install it.</summary>
    LinkToInstaller,

    /// <summary>No automatic remediation is defined for this tool.</summary>
    None,
}
