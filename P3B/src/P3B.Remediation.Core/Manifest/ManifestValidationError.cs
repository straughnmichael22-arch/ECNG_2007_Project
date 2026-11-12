namespace P3B.Remediation.Core.Manifest;

/// <summary>One validation problem, located by a JSON path such as <c>$.tools[0].id</c>.</summary>
public sealed record ManifestValidationError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}
