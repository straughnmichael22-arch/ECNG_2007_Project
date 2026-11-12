using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Windows.Verification;

/// <summary>Read-only file checks for smoke tests.</summary>
public sealed class WindowsFileProbe : IFileProbe
{
    public string ExpandEnvironmentVariables(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Environment.ExpandEnvironmentVariables(path);
    }

    public bool FileExists(string path) => File.Exists(path);
}
