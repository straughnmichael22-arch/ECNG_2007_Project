namespace P3B.Remediation.Core.Manifest;

public enum ManifestLoadStatus
{
    /// <summary>The manifest was read and passed validation.</summary>
    Loaded,

    /// <summary>No manifest location has been configured.</summary>
    NotConfigured,

    /// <summary>No file exists at the configured location.</summary>
    Missing,

    /// <summary>The file exists but could not be read (locked, access denied, I/O error).</summary>
    Unreadable,

    /// <summary>The file is not well-formed JSON.</summary>
    Malformed,

    /// <summary>The file declares a manifest version P3B does not understand.</summary>
    UnsupportedVersion,

    /// <summary>The file is well-formed but required fields are missing or have invalid values.</summary>
    Invalid,
}
