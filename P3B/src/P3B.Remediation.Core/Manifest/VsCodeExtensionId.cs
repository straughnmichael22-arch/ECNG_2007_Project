using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// The only VS Code extension identifiers and versions P3B passes to VS Code: letters, digits
/// and hyphens, so nothing in them can be read as an option, a path or a shell character.
/// </summary>
public static partial class VsCodeExtensionId
{
    /// <summary><c>publisher.name</c>, as shown by <c>--list-extensions</c>.</summary>
    public static bool IsValid([NotNullWhen(true)] string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>A plain version such as <c>1.2.3</c> or <c>1.2.3-beta1</c>.</summary>
    public static bool IsValidVersion([NotNullWhen(true)] string? version) => version is not null && VersionPattern().IsMatch(version);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,99}\.[A-Za-z0-9][A-Za-z0-9-]{0,99}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+){1,3}(-[A-Za-z0-9]+)?$")]
    private static partial Regex VersionPattern();
}
