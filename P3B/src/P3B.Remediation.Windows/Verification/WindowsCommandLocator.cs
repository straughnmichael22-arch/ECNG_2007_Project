using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Windows.Verification;

/// <summary>
/// Finds commands the way a newly opened terminal would: by searching the system and user PATH
/// read fresh from Windows (an installer may have changed PATH after P3B started), trying each
/// PATHEXT extension. Relative paths and the current directory are never searched. Read-only.
/// </summary>
public sealed class WindowsCommandLocator : ICommandLocator
{
    private readonly Func<IReadOnlyList<string>> _searchDirectories;
    private readonly IReadOnlyList<string> _extensions;

    public WindowsCommandLocator()
        : this(ReadCurrentPath, ReadPathExtensions())
    {
    }

    /// <param name="searchDirectories">Supplies the folders to search, in order.</param>
    /// <param name="extensions">Executable extensions to try, for example ".EXE".</param>
    public WindowsCommandLocator(Func<IReadOnlyList<string>> searchDirectories, IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(searchDirectories);
        ArgumentNullException.ThrowIfNull(extensions);
        _searchDirectories = searchDirectories;
        _extensions = extensions;
    }

    public string? Locate(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (Path.IsPathFullyQualified(command))
        {
            return File.Exists(command) ? command : null;
        }

        if (command.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0)
        {
            return null;
        }

        var hasKnownExtension = _extensions.Any(e => command.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        var candidates = hasKnownExtension ? [command] : _extensions.Select(e => command + e).ToArray();

        foreach (var directory in _searchDirectories())
        {
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadCurrentPath()
    {
        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        return $"{machine};{user}"
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => Environment.ExpandEnvironmentVariables(entry).Trim('"'))
            .ToArray();
    }

    private static string[] ReadPathExtensions() =>
        (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
