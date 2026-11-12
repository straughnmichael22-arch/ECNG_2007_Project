namespace P3B.Remediation.Core.Verification;

/// <summary>Finds a version number in tool output without partial matches.</summary>
public static class VersionText
{
    /// <summary>
    /// True if <paramref name="version"/> appears as a whole version in <paramref name="text"/>:
    /// "1.2" matches "tool 1.2" and "v1.2", but not "11.2", "1.2.3" or "1.20".
    /// </summary>
    public static bool ContainsVersion(string text, string version)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var index = text.IndexOf(version, StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = index + version.Length;
            if (StartsCleanly(text, index) && EndsCleanly(text, end))
            {
                return true;
            }

            index = text.IndexOf(version, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool StartsCleanly(string text, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var before = text[index - 1];
        if (before is 'v' or 'V')
        {
            return index == 1 || !char.IsLetterOrDigit(text[index - 2]);
        }

        return !char.IsLetterOrDigit(before) && before != '.';
    }

    private static bool EndsCleanly(string text, int end)
    {
        if (end >= text.Length)
        {
            return true;
        }

        var after = text[end];
        var followedByDigit = end + 1 < text.Length && char.IsDigit(text[end + 1]);
        return !char.IsLetterOrDigit(after) && !(after == '.' && followedByDigit);
    }
}
