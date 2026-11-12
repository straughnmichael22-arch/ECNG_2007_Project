namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Opaque identifier for the audit issue that a remediation action addresses (P3B-DR-01).
/// </summary>
/// <remarks>
/// SEAM: issue codes are defined by the shared P3A-P3B contract, which is not available yet.
/// No issue codes are defined here. See docs/INTEGRATION-SEAMS.md.
/// </remarks>
public sealed record IssueReference
{
    public IssueReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
