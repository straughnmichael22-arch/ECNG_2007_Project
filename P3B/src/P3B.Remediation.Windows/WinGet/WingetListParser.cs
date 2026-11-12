namespace P3B.Remediation.Windows.WinGet;

/// <summary>The table row for one package in <c>winget list</c> output.</summary>
public sealed record WingetListMatch(string Line, string? Version);

/// <summary>
/// Finds a package in <c>winget list</c> table output (columns Name, Id, Version, ...). The
/// version is the column after the ID. Returns null if the ID is not found, for example because
/// WinGet truncated it, so the caller treats the state as unknown rather than guessing.
/// </summary>
public static class WingetListParser
{
    private static readonly string[] VersionPrefixes = ["<", ">"];

    public static WingetListMatch? FindPackage(string output, string packageId)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        foreach (var rawLine in output.Split('\n'))
        {
            // WinGet redraws progress spinners with carriage returns; keep only the final text.
            var line = rawLine.Split('\r', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = Array.FindIndex(tokens, t => string.Equals(t, packageId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            var versionIndex = index + 1;
            if (versionIndex < tokens.Length && VersionPrefixes.Contains(tokens[versionIndex]))
            {
                versionIndex++;
            }

            return new WingetListMatch(line, versionIndex < tokens.Length ? tokens[versionIndex] : null);
        }

        return null;
    }
}
