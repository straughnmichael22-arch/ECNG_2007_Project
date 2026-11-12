using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EDAEnvironmentManager.Audit
{
    /// <summary>
    /// Checks the computer against the manifest. Moved from AuditWindow without changing what
    /// is detected or how: the window now draws its result cards from the returned report.
    /// Nothing is installed or changed.
    /// </summary>
    public class EnvironmentAuditor
    {
        // --------------------------------------------------
        // RUN AUDIT
        // --------------------------------------------------

        public AuditReport Audit(EdaManifest manifest)
        {
            List<ComponentAuditResult> results = new List<ComponentAuditResult>();

            foreach (EdaTool tool in manifest.Tools ?? new List<EdaTool>())
                results.Add(AuditTool(tool));

            foreach (EdaExtension extension in manifest.Extensions ?? new List<EdaExtension>())
                results.Add(AuditExtension(extension, manifest));

            return new AuditReport(manifest.ManifestVersion, DateTimeOffset.Now, results);
        }

        // --------------------------------------------------
        // TOOL RESULT
        // --------------------------------------------------

        public ComponentAuditResult AuditTool(EdaTool tool)
        {
            List<string> candidates = FindExecutables(tool);

            string? installedPath = null;
            string version = "Not Available";
            bool ready = false;

            bool checkVersion =
                !string.IsNullOrWhiteSpace(tool.MinimumApprovedVersion) ||
                !string.IsNullOrWhiteSpace(tool.MaximumApprovedVersion);

            foreach (string candidate in candidates)
            {
                if (!checkVersion)
                {
                    installedPath = candidate;
                    version = "Not Checked";
                    ready = true;
                    break;
                }

                string candidateVersion = GetToolVersion(tool, candidate);

                if (candidateVersion != "Unknown" &&
                    IsVersionApproved(
                        candidateVersion,
                        tool.MinimumApprovedVersion,
                        tool.MaximumApprovedVersion))
                {
                    installedPath = candidate;
                    version = candidateVersion;
                    ready = true;
                    break;
                }

                if (installedPath == null)
                {
                    installedPath = candidate;
                    version = candidateVersion;
                }
            }

            string installationStatus = installedPath == null
                ? "Not Installed"
                : "Detected";

            string readinessStatus = ready ? "Ready" : "Not Ready";

            string displayedVersion =
                version == "Unknown" ? "Not Available" : version;

            string reason;
            AuditFinding finding;

            if (installedPath == null)
            {
                reason = "Executable was not found in PATH or the manifest search locations.";
                finding = AuditFinding.NotInstalled;
            }
            else if (!checkVersion)
            {
                reason = "";
                finding = AuditFinding.None;
            }
            else if (version == "Unknown")
            {
                reason = "The executable was found, but its version could not be retrieved.";
                finding = AuditFinding.VersionUnknown;
            }
            else
            {
                finding = ClassifyVersion(
                    version,
                    tool.MinimumApprovedVersion,
                    tool.MaximumApprovedVersion,
                    out reason
                );
            }

            return new ComponentAuditResult
            {
                Kind = ComponentKind.Tool,
                Name = tool.Name,
                Type = tool.Type,
                DisplayName = tool.Name,
                InstallationStatus = installationStatus,
                ReadinessStatus = readinessStatus,
                Version = displayedVersion,
                InstalledPath = installedPath,
                Reason = reason,
                Finding = ready ? AuditFinding.None : finding,
                CountsAsReady = ready,
                Tool = tool
            };
        }

        // --------------------------------------------------
        // TOOL DETECTION
        // --------------------------------------------------

        public List<string> FindExecutables(EdaTool tool)
        {
            List<string> foundPaths = new List<string>();

            string whereCommand = !string.IsNullOrWhiteSpace(tool.WhereCommand)
                ? tool.WhereCommand
                : tool.Executable;

            if (!string.IsNullOrWhiteSpace(whereCommand))
            {
                foreach (string commandPath in FindUsingWhere(whereCommand))
                {
                    foundPaths.AddRange(ResolveWhereResult(commandPath, tool));
                }
            }

            foundPaths.AddRange(FindUsingManifest(tool));

            return foundPaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string? FindExecutable(EdaTool tool)
        {
            return FindExecutables(tool).FirstOrDefault();
        }

        private List<string> ResolveWhereResult(string commandPath, EdaTool tool)
        {
            List<string> paths = new List<string>();

            if (string.IsNullOrWhiteSpace(commandPath) ||
                string.IsNullOrWhiteSpace(tool.Executable))
            {
                return paths;
            }

            if (string.Equals(
                Path.GetFileName(commandPath),
                tool.Executable,
                StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(commandPath))
                    paths.Add(commandPath);

                return paths;
            }

            string? commandDirectory = Path.GetDirectoryName(commandPath);
            if (string.IsNullOrWhiteSpace(commandDirectory))
                return paths;

            DirectoryInfo? containingDirectory = new DirectoryInfo(commandDirectory);

            DirectoryInfo? installDirectory =
                string.Equals(
                    containingDirectory.Name,
                    "bin",
                    StringComparison.OrdinalIgnoreCase)
                    ? containingDirectory.Parent
                    : containingDirectory;

            if (installDirectory == null)
                return paths;

            string resolvedExecutable = Path.Combine(
                installDirectory.FullName,
                tool.Executable);

            if (File.Exists(resolvedExecutable))
                paths.Add(resolvedExecutable);

            return paths;
        }

        private List<string> FindUsingWhere(string executable)
        {
            List<string> paths = new List<string>();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add(executable);

                using Process? process = Process.Start(startInfo);
                if (process == null)
                    return paths;

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                    return paths;
                }

                string output = outputTask.GetAwaiter().GetResult();
                _ = errorTask.GetAwaiter().GetResult();

                foreach (string line in output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = line.Trim();
                    if (File.Exists(candidate))
                        paths.Add(candidate);
                }
            }
            catch
            {
            }

            return paths;
        }

        private List<string> FindUsingManifest(EdaTool tool)
        {
            List<string> paths = new List<string>();

            if (tool.CommonPaths != null)
            {
                foreach (string path in tool.CommonPaths)
                    paths.AddRange(ExpandManifestPath(path));
            }

            if (tool.ExpectedPaths != null)
            {
                foreach (string path in tool.ExpectedPaths)
                    paths.AddRange(ExpandManifestPath(path));
            }

            if (tool.SearchRoots != null)
            {
                foreach (string rootPath in tool.SearchRoots)
                {
                    try
                    {
                        string root = Environment.ExpandEnvironmentVariables(rootPath);

                        if (!Directory.Exists(root))
                            continue;

                        string relativePath = tool.RelativeExecutablePath ?? "";

                        if (string.IsNullOrWhiteSpace(tool.FolderPattern))
                        {
                            string directPath = Path.Combine(root, relativePath);

                            if (File.Exists(directPath))
                                paths.Add(directPath);

                            continue;
                        }

                        foreach (string folder in Directory.EnumerateDirectories(
                            root, tool.FolderPattern))
                        {
                            string candidate = Path.Combine(folder, relativePath);

                            if (File.Exists(candidate))
                                paths.Add(candidate);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return paths;
        }

        private List<string> ExpandManifestPath(string manifestPath)
        {
            List<string> results = new List<string>();

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(manifestPath);

                if (!expanded.Contains('*') && !expanded.Contains('?'))
                {
                    if (File.Exists(expanded))
                        results.Add(expanded);

                    return results;
                }

                string? root = Path.GetPathRoot(expanded);

                if (string.IsNullOrWhiteSpace(root))
                    return results;

                string remainder = expanded.Substring(root.Length);

                string[] segments = remainder.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                List<string> currentPaths = new List<string> { root };

                for (int i = 0; i < segments.Length; i++)
                {
                    bool finalSegment = i == segments.Length - 1;
                    List<string> nextPaths = new List<string>();

                    foreach (string current in currentPaths)
                    {
                        if (!Directory.Exists(current))
                            continue;

                        try
                        {
                            IEnumerable<string> matches = finalSegment
                                ? Directory.EnumerateFiles(current, segments[i])
                                : Directory.EnumerateDirectories(current, segments[i]);

                            nextPaths.AddRange(matches);
                        }
                        catch
                        {
                        }
                    }

                    currentPaths = nextPaths;

                    if (currentPaths.Count == 0)
                        break;
                }

                results.AddRange(currentPaths.Where(File.Exists));
            }
            catch
            {
            }

            return results;
        }

        // --------------------------------------------------
        // TOOL VERSION DETECTION
        // --------------------------------------------------

        private string GetToolVersion(EdaTool tool, string path)
        {
            try
            {
                if (string.Equals(
                    tool.Type,
                    "vscode",
                    StringComparison.OrdinalIgnoreCase))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);

                    string? fileVersion = info.FileVersion;

                    if (!string.IsNullOrWhiteSpace(fileVersion) &&
                        Version.TryParse(fileVersion, out _))
                    {
                        return fileVersion;
                    }

                    return "Unknown";
                }

                string arguments = tool.VersionArguments ?? "";

                bool supportsVersionCommand =
                    string.Equals(tool.Type, "ghdl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tool.Type, "vivado", StringComparison.OrdinalIgnoreCase);

                if (supportsVersionCommand &&
                    !string.IsNullOrWhiteSpace(arguments))
                {
                    string output = RunVersionCommand(path, arguments);

                    string parsedVersion = ParseToolVersion(tool.Type, output);

                    if (parsedVersion != "Unknown")
                        return parsedVersion;
                }

                FileVersionInfo fallbackInfo = FileVersionInfo.GetVersionInfo(path);

                string? fallbackVersion = fallbackInfo.FileVersion;

                if (!string.IsNullOrWhiteSpace(fallbackVersion) &&
                    Version.TryParse(fallbackVersion, out _))
                {
                    return fallbackVersion;
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private string ParseToolVersion(string toolType, string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return "Unknown";

            Match match;

            if (string.Equals(toolType, "vscode", StringComparison.OrdinalIgnoreCase))
            {
                match = Regex.Match(output, @"(?m)^\s*(\d+\.\d+\.\d+)\s*$");
            }
            else if (string.Equals(toolType, "ghdl", StringComparison.OrdinalIgnoreCase))
            {
                match = Regex.Match(
                    output,
                    @"(?i)\bGHDL\s+v?(\d+(?:\.\d+){1,3})");
            }
            else if (string.Equals(toolType, "vivado", StringComparison.OrdinalIgnoreCase))
            {
                match = Regex.Match(output, @"\b(\d{4}\.\d+)\b");
            }
            else
            {
                match = Regex.Match(output, @"\b(\d+(?:\.\d+){1,3})\b");
            }

            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        private string RunVersionCommand(string executablePath, string arguments)
        {
            try
            {
                bool isBatchFile =
                    executablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                    executablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

                ProcessStartInfo startInfo;

                if (isBatchFile)
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/d /s /c \"\"" + executablePath +
                                    "\" " + arguments + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                }

                using Process? process = Process.Start(startInfo);

                if (process == null)
                    return "";

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(15000))
                {
                    process.Kill(true);
                    return "";
                }

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();

                return output + Environment.NewLine + error;
            }
            catch
            {
                return "";
            }
        }

        // --------------------------------------------------
        // VERSION APPROVAL
        // --------------------------------------------------

        public static bool IsVersionApproved(
            string installedVersion,
            string minimumVersion,
            string maximumVersion)
        {
            if (!Version.TryParse(installedVersion, out Version? installed))
                return false;

            if (!string.IsNullOrWhiteSpace(minimumVersion))
            {
                if (!Version.TryParse(minimumVersion, out Version? minimum) ||
                    installed.CompareTo(minimum) < 0)
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                if (!Version.TryParse(maximumVersion, out Version? maximum) ||
                    installed.CompareTo(maximum) > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// GetVersionApprovalReason from AuditWindow, with the same messages, also returning
        /// which finding the message describes.
        /// </summary>
        public static AuditFinding ClassifyVersion(
            string installedVersion,
            string minimumVersion,
            string maximumVersion,
            out string reason)
        {
            if (!Version.TryParse(installedVersion, out Version? installed))
            {
                reason = "The detected version number could not be interpreted.";
                return AuditFinding.VersionUnparseable;
            }

            if (!string.IsNullOrWhiteSpace(minimumVersion))
            {
                if (!Version.TryParse(minimumVersion, out Version? minimum))
                {
                    reason = "The minimum approved version in the manifest is invalid.";
                    return AuditFinding.InvalidMinimumVersion;
                }

                if (installed.CompareTo(minimum) < 0)
                {
                    reason = $"Version is below the minimum approved version ({minimumVersion}).";
                    return AuditFinding.VersionBelowMinimum;
                }
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                if (!Version.TryParse(maximumVersion, out Version? maximum))
                {
                    reason = "The maximum approved version in the manifest is invalid.";
                    return AuditFinding.InvalidMaximumVersion;
                }

                if (installed.CompareTo(maximum) > 0)
                {
                    reason = $"Version is above the maximum approved version ({maximumVersion}).";
                    return AuditFinding.VersionAboveMaximum;
                }
            }

            reason = "Version is within the approved range.";
            return AuditFinding.None;
        }

        // --------------------------------------------------
        // EXTENSION DETECTION
        // NO VS CODE PROCESS IS LAUNCHED
        // --------------------------------------------------

        public ComponentAuditResult AuditExtension(EdaExtension extension, EdaManifest manifest)
        {
            if (!extension.CheckEnabled)
            {
                return new ComponentAuditResult
                {
                    Kind = ComponentKind.Extension,
                    Name = extension.Name,
                    Type = extension.Type,
                    DisplayName = extension.Name,
                    InstallationStatus = "Not Checked",
                    ReadinessStatus = "Not Checked",
                    Version = "Not Available",
                    Reason = "Extension check is disabled in the manifest.",
                    Finding = AuditFinding.CheckDisabled,
                    CountsAsReady = true,
                    Extension = extension
                };
            }

            string version = "Not Available";
            string? installedPath = null;
            string matchedExtensionName = "";
            bool ready = false;
            AuditFinding finding;
            string reason;

            EdaTool? editor = manifest.Tools?.FirstOrDefault(
                t => string.Equals(
                    t.Name,
                    extension.Editor,
                    StringComparison.OrdinalIgnoreCase)
            );

            if (editor == null || FindExecutable(editor) == null)
            {
                finding = AuditFinding.EditorNotDetected;
                reason = $"The editor ({extension.Editor}) was not detected, so the extensions could not be checked.";
            }
            else
            {
                string extensionsPath = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".vscode",
                    "extensions"
                );

                if (Directory.Exists(extensionsPath))
                {
                    List<string> approvedIds =
                        (extension.ApprovedExtensions ?? new List<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToList();

                    if (approvedIds.Count == 0 &&
                        !string.IsNullOrWhiteSpace(extension.ExtensionId))
                    {
                        approvedIds.Add(extension.ExtensionId);
                    }

                    foreach (string approvedId in approvedIds)
                    {
                        if (TryFindInstalledExtension(
                            extensionsPath,
                            approvedId,
                            out version,
                            out installedPath,
                            out matchedExtensionName))
                        {
                            ready = true;
                            break;
                        }
                    }
                }

                finding = ready ? AuditFinding.None : AuditFinding.ExtensionNotInstalled;
                reason = ready ? "" : "None of the approved extensions is installed.";
            }

            string displayName =
                ready && !string.IsNullOrWhiteSpace(matchedExtensionName)
                    ? matchedExtensionName
                    : extension.Name;

            return new ComponentAuditResult
            {
                Kind = ComponentKind.Extension,
                Name = extension.Name,
                Type = extension.Type,
                DisplayName = displayName,
                InstallationStatus = ready ? "Detected" : "Not Detected",
                ReadinessStatus = ready ? "Ready" : "Not Ready",
                Version = version,
                InstalledPath = installedPath,
                Reason = reason,
                Finding = finding,
                CountsAsReady = ready,
                Extension = extension
            };
        }

        // --------------------------------------------------
        // SEARCH EXTENSION PACKAGE.JSON
        // --------------------------------------------------

        private bool TryFindInstalledExtension(
            string extensionsPath,
            string requiredExtensionId,
            out string version,
            out string? installedPath,
            out string installedExtensionName)
        {
            version = "Not Available";
            installedPath = null;
            installedExtensionName = requiredExtensionId;

            try
            {
                foreach (string folder in
                    Directory.EnumerateDirectories(extensionsPath))
                {
                    string packagePath = Path.Combine(folder, "package.json");

                    if (!File.Exists(packagePath))
                        continue;

                    try
                    {
                        string json = File.ReadAllText(packagePath);

                        using JsonDocument document = JsonDocument.Parse(json);

                        JsonElement root = document.RootElement;

                        if (!root.TryGetProperty("publisher", out JsonElement publisherElement))
                            continue;

                        if (!root.TryGetProperty("name", out JsonElement nameElement))
                            continue;

                        string? publisher = publisherElement.GetString();
                        string? name = nameElement.GetString();

                        if (string.IsNullOrWhiteSpace(publisher) ||
                            string.IsNullOrWhiteSpace(name))
                            continue;

                        string installedExtensionId = publisher + "." + name;

                        if (!installedExtensionId.Equals(
                            requiredExtensionId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        installedPath = folder;

                        if (root.TryGetProperty(
                            "displayName",
                            out JsonElement displayNameElement) &&
                            displayNameElement.ValueKind == JsonValueKind.String)
                        {
                            string? packageDisplayName = displayNameElement.GetString();

                            if (!string.IsNullOrWhiteSpace(packageDisplayName))
                                installedExtensionName = packageDisplayName;
                        }
                        else if (root.TryGetProperty(
                            "name",
                            out JsonElement packageNameElement) &&
                            packageNameElement.ValueKind == JsonValueKind.String)
                        {
                            installedExtensionName =
                                packageNameElement.GetString() ?? requiredExtensionId;
                        }

                        if (root.TryGetProperty("version", out JsonElement versionElement))
                        {
                            version = versionElement.GetString() ?? "Not Available";
                        }

                        return true;
                    }
                    catch (JsonException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
