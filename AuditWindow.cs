using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EDAEnvironmentManager
{
    public class AuditWindow : Form
    {
        private FlowLayoutPanel resultsPanel = null!;
        private Button runAuditButton = null!;
        private Label statusLabel = null!;
        private Label overallStatusLabel = null!;

        private EdaManifest? manifest;

        public AuditWindow()
        {
            Text = "EDA Environment Audit - Results";
            Size = new Size(1100, 780);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);
            BackColor = Color.FromArgb(245, 247, 250);

            BuildInterface();
            LoadManifest();
        }

        // --------------------------------------------------
        // INTERFACE
        // --------------------------------------------------

        private void BuildInterface()
        {
            Label title = new Label
            {
                Text = "Environment Audit",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 45, 60),
                AutoSize = true,
                Location = new Point(35, 25)
            };

            Label description = new Label
            {
                Text = "Check installed EDA tools and their approved configurations.",
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(38, 80)
            };

            runAuditButton = new Button
            {
                Text = "Run Audit",
                Size = new Size(140, 42),
                Location = new Point(35, 120),
                BackColor = Color.FromArgb(35, 110, 190),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            runAuditButton.FlatAppearance.BorderSize = 0;
            runAuditButton.Click += RunAuditButton_Click;

            statusLabel = new Label
            {
                Text = "Ready to run audit.",
                Font = new Font("Segoe UI", 10.5f),
                AutoSize = true,
                Location = new Point(195, 133),
                ForeColor = Color.DimGray
            };

            overallStatusLabel = new Label
            {
                Text = "Overall Environment: Not Audited",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(35, 178),
                ForeColor = Color.DimGray
            };

            resultsPanel = new FlowLayoutPanel
            {
                Location = new Point(35, 220),
                Size = new Size(1015, 490),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Bottom |
                         AnchorStyles.Left |
                         AnchorStyles.Right
            };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(runAuditButton);
            Controls.Add(statusLabel);
            Controls.Add(overallStatusLabel);
            Controls.Add(resultsPanel);
        }

        // --------------------------------------------------
        // MANIFEST
        // --------------------------------------------------

        private void LoadManifest()
        {
            try
            {
                string manifestPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "eda-tool-manifest.json"
                );

                if (!File.Exists(manifestPath))
                {
                    statusLabel.Text = "Manifest Not Found.";
                    manifest = null;
                    return;
                }

                string json = File.ReadAllText(manifestPath);

                manifest = JsonSerializer.Deserialize<EdaManifest>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                if (manifest == null)
                {
                    statusLabel.Text = "Could not read manifest.";
                    return;
                }

                manifest.Tools ??= new List<EdaTool>();
                manifest.Extensions ??= new List<EdaExtension>();

                statusLabel.Text = "Manifest Found. Ready to audit.";
            }
            catch (Exception ex)
            {
                manifest = null;
                statusLabel.Text = "Error loading manifest: " + ex.Message;
            }
        }

        // --------------------------------------------------
        // RUN AUDIT
        // --------------------------------------------------

        private void RunAuditButton_Click(object? sender, EventArgs e)
        {
            if (manifest == null)
            {
                LoadManifest();

                if (manifest == null)
                    return;
            }

            resultsPanel.Controls.Clear();

            int totalComponents = 0;
            bool allReady = true;

            foreach (EdaTool tool in manifest.Tools)
            {
                bool toolReady = AddToolResult(tool);

                if (!toolReady)
                    allReady = false;

                totalComponents++;
            }

            foreach (EdaExtension extension in manifest.Extensions)
            {
                bool extensionReady = AddExtensionResult(extension);

                if (!extensionReady)
                    allReady = false;

                totalComponents++;
            }

            if (allReady)
            {
                overallStatusLabel.Text =
                    "Overall Environment: Ready";

                overallStatusLabel.ForeColor =
                    Color.ForestGreen;
            }
            else
            {
                overallStatusLabel.Text =
                    "Overall Environment: Not Ready";

                overallStatusLabel.ForeColor =
                    Color.Firebrick;
            }

            statusLabel.Text =
                $"Audit completed. {totalComponents} components checked.";
        }

        // --------------------------------------------------
        // TOOL DETECTION
        // --------------------------------------------------

        private List<string> FindExecutables(EdaTool tool)
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

        private string? FindExecutable(EdaTool tool)
        {
            return FindExecutables(tool).FirstOrDefault();
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

        private bool AddToolResult(EdaTool tool)
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

            if (installedPath == null)
            {
                reason = "Executable was not found in PATH or the manifest search locations.";
            }
            else if (!checkVersion)
            {
                reason = "";
            }
            else if (version == "Unknown")
            {
                reason = "The executable was found, but its version could not be retrieved.";
            }
            else
            {
                reason = GetVersionApprovalReason(
                    version,
                    tool.MinimumApprovedVersion,
                    tool.MaximumApprovedVersion
                );
            }

            CreateResultCard(
                tool.Name,
                installationStatus,
                readinessStatus,
                displayedVersion,
                installedPath,
                reason,
                tool
            );

            return ready;
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

        private bool IsVersionApproved(
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

        private string GetVersionApprovalReason(
            string installedVersion,
            string minimumVersion,
            string maximumVersion)
        {
            if (!Version.TryParse(installedVersion, out Version? installed))
                return "The detected version number could not be interpreted.";

            if (!string.IsNullOrWhiteSpace(minimumVersion))
            {
                if (!Version.TryParse(minimumVersion, out Version? minimum))
                    return "The minimum approved version in the manifest is invalid.";

                if (installed.CompareTo(minimum) < 0)
                    return $"Version is below the minimum approved version ({minimumVersion}).";
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                if (!Version.TryParse(maximumVersion, out Version? maximum))
                    return "The maximum approved version in the manifest is invalid.";

                if (installed.CompareTo(maximum) > 0)
                    return $"Version is above the maximum approved version ({maximumVersion}).";
            }

            return "Version is within the approved range.";
        }

        // --------------------------------------------------
        // EXTENSION DETECTION
        // NO VS CODE PROCESS IS LAUNCHED
        // --------------------------------------------------

        private bool AddExtensionResult(EdaExtension extension)
        {
            string installationStatus = "Not Checked";
            string readinessStatus = "Not Checked";
            string version = "Not Available";
            string? installedPath = null;
            string reason = "";
            string matchedExtensionName = "";

            if (!extension.CheckEnabled)
            {
                CreateResultCard(
                    extension.Name,
                    installationStatus,
                    readinessStatus,
                    version,
                    installedPath,
                    "Extension check is disabled in the manifest."
                );

                return true;
            }

            EdaTool? editor = manifest?.Tools.FirstOrDefault(
                t => string.Equals(
                    t.Name,
                    extension.Editor,
                    StringComparison.OrdinalIgnoreCase)
            );

            if (editor == null || FindExecutable(editor) == null)
            {
                installationStatus = "Not Detected";
                readinessStatus = "Not Ready";
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
                            installationStatus = "Detected";
                            readinessStatus = "Ready";
                            break;
                        }
                    }
                }

                if (readinessStatus != "Ready")
                {
                    installationStatus = "Not Detected";
                    readinessStatus = "Not Ready";
                }
            }

            string displayName =
                readinessStatus == "Ready" &&
                !string.IsNullOrWhiteSpace(matchedExtensionName)
                    ? matchedExtensionName
                    : extension.Name;

            CreateResultCard(
                displayName,
                installationStatus,
                readinessStatus,
                version,
                installedPath,
                reason
            );

            return readinessStatus == "Ready";
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

        // --------------------------------------------------
        // RESULT CARDS
        // --------------------------------------------------

        private void CreateResultCard(
            string name,
            string installationStatus,
            string readinessStatus,
            string version,
            string? path,
            string reason = "",
            EdaTool? tool = null)
        {
            Panel card = new Panel
            {
                Size = new Size(970, 195),
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 16),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label nameLabel = new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Location = new Point(20, 14),
                AutoSize = true
            };

            Label installationLabel = new Label
            {
                Text = "Installation Status: " + installationStatus,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(20, 44),
                AutoSize = true,
                ForeColor = GetStatusColor(installationStatus)
            };

            Label readinessLabel = new Label
            {
                Text = "Readiness Status: " + readinessStatus,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(20, 68),
                AutoSize = true,
                ForeColor = GetStatusColor(readinessStatus)
            };

            Label versionLabel = new Label
            {
                Text = "Version: " + version,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(20, 92),
                AutoSize = true,
                ForeColor = readinessStatus == "Ready"
                    ? Color.ForestGreen
                    : readinessStatus == "Not Ready"
                        ? Color.Firebrick
                        : Color.DimGray
            };

            bool showReason =
                installationStatus == "Detected" &&
                readinessStatus == "Not Ready" &&
                !string.IsNullOrWhiteSpace(reason);

            Label? reasonLabel = null;

            if (showReason)
            {
                reasonLabel = new Label
                {
                    Text = "Reason: " + reason,
                    Font = new Font("Segoe UI", 8.5f),
                    Location = new Point(20, 118),
                    AutoSize = true,
                    MaximumSize = new Size(720, 0),
                    ForeColor = Color.Firebrick
                };
            }

            Label pathLabel = new Label
            {
                Text = "Location: " + (path ?? "Not Available"),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(20, showReason ? 158 : 118),
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                ForeColor = Color.Gray
            };

            bool showSmokeTest = readinessStatus == "Ready";
            bool showRemediation = readinessStatus == "Not Ready";

            Button actionButton = new Button
            {
                Text = showSmokeTest ? "Smoke Test" : "Remediate",
                Location = new Point(770, 52),
                Size = new Size(165, 42),
                BackColor = showSmokeTest
                    ? Color.FromArgb(35, 140, 90)
                    : Color.FromArgb(35, 110, 190),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Visible = showSmokeTest || showRemediation,
                Enabled = showSmokeTest || showRemediation
            };

            actionButton.FlatAppearance.BorderSize = 0;

            actionButton.Click += (sender, e) =>
            {
                if (showSmokeTest)
                {
                    if (tool != null && path != null)
                        RunSmokeTest(tool, path);
                    else
                        RunSmokeTest(name);
                }
                else if (showRemediation)
                {
                    if (tool != null)
                        RunRemediation(tool);
                    else
                        RunRemediation(name);
                }
            };

            card.Controls.Add(nameLabel);
            card.Controls.Add(installationLabel);
            card.Controls.Add(readinessLabel);
            card.Controls.Add(versionLabel);

            if (reasonLabel != null)
                card.Controls.Add(reasonLabel);

            card.Controls.Add(pathLabel);
            card.Controls.Add(actionButton);

            resultsPanel.Controls.Add(card);
        }

        // --------------------------------------------------
        // STATUS COLOURS
        // --------------------------------------------------

        private Color GetStatusColor(string status)
        {
            if (status == "Ready" || status == "Detected")
                return Color.ForestGreen;

            if (status == "Not Ready" || status == "Not Detected")
                return Color.Firebrick;

            return Color.DimGray;
        }

        // --------------------------------------------------
        // SMOKE TEST DISPATCHER (CLEAN ROUTER)
        // --------------------------------------------------

        private void RunSmokeTest(string componentName)
        {
            MessageBox.Show(
                "No smoke test has been implemented for " + componentName + ".",
                "Smoke Test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void RunSmokeTest(EdaTool tool, string executablePath)
        {
            if (tool.SmokeTest == null || !tool.SmokeTest.Enabled)
            {
                MessageBox.Show(
                    "Smoke test is disabled or not configured for " + tool.Name + ".",
                    "Smoke Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            // Route based on tool type
            if (string.Equals(tool.Type, "ghdl", StringComparison.OrdinalIgnoreCase))
            {
                RunGhdlSmokeTest(tool, executablePath);
            }
            else
            {
                MessageBox.Show(
                    "No smoke-test implementation exists for " + tool.Name + ".",
                    "Smoke Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }

        // --------------------------------------------------
        // GHDL SMOKE TEST (SELF-CONTAINED ENCAPSULATION)
        // --------------------------------------------------

        private async void RunGhdlSmokeTest(EdaTool tool, string executablePath)
        {
            DialogResult confirmation = MessageBox.Show(
                this,
                "This smoke test will check whether GHDL can successfully " +
                "analyze, elaborate, and simulate a basic VHDL design.\n\n" +
                "The following actions will take place:\n\n" +
                "1. A test folder will be created in your Documents folder.\n" +
                "2. A VHDL test file (hello.vhd) will be written into it.\n" +
                "3. GHDL will analyze, elaborate, and simulate the design.\n\n" +
                "Would you like to proceed?",
                "Confirm GHDL Smoke Test",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirmation != DialogResult.Yes)
            {
                statusLabel.Text = "GHDL smoke test cancelled.";
                return;
            }

            try
            {
                SmokeTestSettings settings = tool.SmokeTest!;
                statusLabel.Text = "Running GHDL smoke test...";

                var result = await Task.Run(
                    () => ExecuteGhdlPipeline(settings, executablePath)
                );

                statusLabel.Text = result.Success
                    ? "GHDL smoke test passed."
                    : "GHDL smoke test failed.";

                MessageBox.Show(
                    result.Details,
                    result.Success ? "Smoke Test Passed" : "Smoke Test Failed",
                    MessageBoxButtons.OK,
                    result.Success
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Error
                );
            }
            catch (Exception ex)
            {
                statusLabel.Text = "GHDL smoke test failed.";

                MessageBox.Show(
                    "An error occurred while running the GHDL smoke test:\n\n" +
                    ex.Message,
                    "Smoke Test Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private (bool Success, string Details) ExecuteGhdlPipeline(
            SmokeTestSettings settings,
            string executablePath)
        {
            string documentsPath = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments
            );

            string testDirectory = Path.Combine(
                documentsPath,
                "WindowsEDAEnvironment",
                "SmokeTests",
                "GHDL",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            );

            Directory.CreateDirectory(testDirectory);

            string testFilePath = Path.Combine(
                testDirectory,
                settings.TestFile
            );

            File.WriteAllText(
                testFilePath,
                settings.Source,
                new UTF8Encoding(false)
            );

            string standard = settings.Standard;

            // STEP 1: ANALYZE
            var analyze = RunGhdlCommand(
                executablePath,
                testDirectory,
                "-a",
                "--std=" + standard,
                settings.TestFile
            );

            if (analyze.ExitCode != 0)
            {
                return (
                    false,
                    "GHDL analysis failed.\n\n" +
                    analyze.Output +
                    "\n\nTest files retained at:\n" +
                    testDirectory
                );
            }

            // STEP 2: ELABORATE
            var elaborate = RunGhdlCommand(
                executablePath,
                testDirectory,
                "-e",
                "--std=" + standard,
                settings.Entity
            );

            if (elaborate.ExitCode != 0)
            {
                return (
                    false,
                    "GHDL elaboration failed.\n\n" +
                    elaborate.Output +
                    "\n\nTest files retained at:\n" +
                    testDirectory
                );
            }

            // STEP 3: SIMULATE
            var simulation = RunGhdlCommand(
                executablePath,
                testDirectory,
                "-r",
                "--std=" + standard,
                settings.Entity
            );

            if (simulation.ExitCode != 0)
            {
                return (
                    false,
                    "GHDL simulation failed.\n\n" +
                    simulation.Output +
                    "\n\nTest files retained at:\n" +
                    testDirectory
                );
            }

            if (!simulation.Output.Contains(
                settings.ExpectedOutput,
                StringComparison.OrdinalIgnoreCase))
            {
                return (
                    false,
                    "GHDL completed, but the expected simulation message was not found.\n\n" +
                    "Expected:\n" + settings.ExpectedOutput +
                    "\n\nActual output:\n" + simulation.Output +
                    "\n\nTest files retained at:\n" + testDirectory
                );
            }

            return (
                true,
                "GHDL smoke test passed!\n\n" +
                "Analysis: Passed\n" +
                "Elaboration: Passed\n" +
                "Simulation: Passed\n\n" +
                "Expected output found:\n" +
                settings.ExpectedOutput +
                "\n\nTest files retained at:\n" + testDirectory
            );
        }

        // --------------------------------------------------
        // GHDL COMMAND RUNNER
        // --------------------------------------------------

        private (int ExitCode, string Output) RunGhdlCommand(
            string executablePath,
            string workingDirectory,
            params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using Process? process = Process.Start(startInfo);

            if (process == null)
                throw new InvalidOperationException("Could not start GHDL.");

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();

            string combinedOutput =
                output + Environment.NewLine + error;

            return (process.ExitCode, combinedOutput);
        }

        // --------------------------------------------------
        // REMEDIATION DISPATCHER (CLEAN ROUTER)
        // --------------------------------------------------

        private void RunRemediation(string componentName)
        {
            MessageBox.Show(
                "Remediation selected for " + componentName +
                ".\n\nRemediation functionality has not been implemented yet.",
                "Remediation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void RunRemediation(EdaTool tool)
        {
            if (tool.Remediation == null || 
                (tool.Remediation.Instructions.Count == 0 && string.IsNullOrWhiteSpace(tool.Remediation.DownloadUrl)))
            {
                MessageBox.Show(
                    "No remediation instructions are configured for " + tool.Name + ".",
                    "Remediation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            RunManualRemediation(tool);
        }

        // --------------------------------------------------
        // UNIFIED MANUAL REMEDIATION (DATA-DRIVEN HANDLER)
        // --------------------------------------------------

        private void RunManualRemediation(EdaTool tool)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Follow these steps to install {tool.Name}:\n");

            for (int i = 0; i < tool.Remediation!.Instructions.Count; i++)
            {
                // Double newline spacing between instruction bullet points
                sb.AppendLine($"{i + 1}.  {tool.Remediation.Instructions[i]}\n");
            }

            // Original design: Link and prompt at the bottom
            if (!string.IsNullOrWhiteSpace(tool.Remediation.DownloadUrl))
            {
                sb.AppendLine($"Official Website / Download URL:\n{tool.Remediation.DownloadUrl}");
                sb.AppendLine("\nWould you like to open the download page in your browser now?");
            }

            DialogResult dialogResult = MessageBox.Show(
                this,
                sb.ToString(),
                $"Remediation Guide: {tool.Name}",
                !string.IsNullOrWhiteSpace(tool.Remediation.DownloadUrl) 
                    ? MessageBoxButtons.YesNo 
                    : MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            if (dialogResult == DialogResult.Yes && !string.IsNullOrWhiteSpace(tool.Remediation.DownloadUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tool.Remediation.DownloadUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Could not open the browser: " + ex.Message,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }
    }

    // --------------------------------------------------
    // MANIFEST CLASSES
    // --------------------------------------------------

    public class EdaManifest
    {
        public string ManifestVersion { get; set; } = "";

        public List<EdaTool> Tools { get; set; } = new();

        public List<EdaExtension> Extensions { get; set; } = new();
    }

    public class EdaTool
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public string Executable { get; set; } = "";

        public string WhereCommand { get; set; } = "";

        public string MinimumApprovedVersion { get; set; } = "";

        public string MaximumApprovedVersion { get; set; } = "";

        public string VersionArguments { get; set; } = "";

        public string FolderPattern { get; set; } = "";

        public string RelativeExecutablePath { get; set; } = "";

        public List<string> SearchRoots { get; set; } = new();

        public List<string> CommonPaths { get; set; } = new();

        public List<string> ExpectedPaths { get; set; } = new();

        public ComponentActions Actions { get; set; } = new();

        public SmokeTestSettings? SmokeTest { get; set; }

        public RemediationSettings? Remediation { get; set; }
    }

    public class SmokeTestSettings
    {
        public bool Enabled { get; set; }

        public string Method { get; set; } = "";

        public string TestFile { get; set; } = "";

        public string Entity { get; set; } = "";

        public string Standard { get; set; } = "";

        public string Source { get; set; } = "";

        public string ExpectedOutput { get; set; } = "";
    }

    public class RemediationSettings
    {
        public string DownloadUrl { get; set; } = "";

        public List<string> Instructions { get; set; } = new();
    }

    public class EdaExtension
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public string Editor { get; set; } = "";

        public List<string> ApprovedExtensions { get; set; } = new();

        public string ExtensionId { get; set; } = "";

        public bool CheckEnabled { get; set; }

        public ComponentActions Actions { get; set; } = new();
    }

    public class ComponentActions
    {
        public bool ShowSmokeTest { get; set; }

        public bool ShowRemediation { get; set; }
    }
}