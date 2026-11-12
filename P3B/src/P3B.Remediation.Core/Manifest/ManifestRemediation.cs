namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// The remediation definition for one manifest tool. PROVISIONAL – see
/// docs/proposals/local-eda-tool-manifest.PROVISIONAL.md.
/// </summary>
public sealed record ManifestRemediation
{
    public ManifestRemediation(RemediationMethod method, string? source)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown remediation method.");
        }

        if (method != RemediationMethod.None)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
        }

        Method = method;
        Source = source;
    }

    public RemediationMethod Method { get; }

    /// <summary>
    /// Where the approved version comes from (WinGet package identifier, archive URL or
    /// installer page), exactly as given in the manifest. Null when <see cref="Method"/> is None.
    /// </summary>
    public string? Source { get; }
}
