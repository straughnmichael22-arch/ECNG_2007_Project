namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Opaque identifier for a tool, supplied by the caller.
/// </summary>
/// <remarks>
/// SEAM: the real identifier format comes from the Local EDA Tool Manifest v1 / shared
/// P3A-P3B contract, neither of which is available yet. This type deliberately assumes
/// nothing about the value's format or allowed values. See docs/INTEGRATION-SEAMS.md.
/// </remarks>
public sealed record ToolReference
{
    public ToolReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
