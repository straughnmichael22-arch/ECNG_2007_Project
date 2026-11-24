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
    public partial class AuditWindow : Form
    {
        private FlowLayoutPanel resultsPanel = null!;
        private Button runAuditButton = null!;
        private Label statusLabel = null!;
        private Label overallStatusLabel = null!;

        private EdaManifest? manifest;
        private bool remediationInProgress;

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

        // ==================================================
        // INTERFACE
        // ==================================================

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

        // ==================================================
        // MANIFEST
        // ==================================================

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

        // ==================================================
        // RUN AUDIT
        // ==================================================

        private void RunAuditButton_Click(object? sender, EventArgs e)
        {
            if (remediationInProgress)
                return;

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
                if (!AddToolResult(tool))
                    allReady = false;

                totalComponents++;
            }

            foreach (EdaExtension extension in manifest.Extensions)
            {
                if (!AddExtensionResult(extension))
                    allReady = false;

                totalComponents++;
            }

            overallStatusLabel.Text = allReady
                ? "Overall Environment: Ready"
                : "Overall Environment: Not Ready";

            overallStatusLabel.ForeColor = allReady
                ? Color.ForestGreen
                : Color.Firebrick;

            statusLabel.Text =
                $"Audit completed. {totalComponents} components checked.";
        }

        // ==================================================
        // TOOL DETECTION
        // ==================================================

        private List<string> FindExecutables(EdaTool tool)
        {
            List<string> foundPaths = new List<string>();

            string whereCommand = !string.IsNullOrWhiteSpace(tool.WhereCommand)
                ? tool.WhereCommand
                : tool.Executable;

            if (!string.IsNullOrWhiteSpace(whereCommand))
            {
                foreach (string commandPath in FindUsingWhere(whereCommand))
                    foundPaths.AddRange(ResolveWhereResult(commandPath, tool));
            }

            foundPaths.AddRange(FindUsingManifest(tool));

            return foundPaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ResolveWhereResult(
            string commandPath,
            EdaTool tool)
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

            string? commandDirectory =
                Path.GetDirectoryName(commandPath);

            if (string.IsNullOrWhiteSpace(commandDirectory))
                return paths;

            DirectoryInfo containingDirectory =
                new DirectoryInfo(commandDirectory);

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
                tool.Executable
            );

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

                Task<string> outputTask =
                    process.StandardOutput.ReadToEndAsync();

                Task<string> errorTask =
                    process.StandardError.ReadToEndAsync();

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
                        string root =
                            Environment.ExpandEnvironmentVariables(rootPath);

                        if (!Directory.Exists(root))
                            continue;

                        string relativePath =
                            tool.RelativeExecutablePath ?? "";

                        if (string.IsNullOrWhiteSpace(tool.FolderPattern))
                        {
                            string directPath =
                                Path.Combine(root, relativePath);

                            if (File.Exists(directPath))
                                paths.Add(directPath);

                            continue;
                        }

                        foreach (string folder in
                            Directory.EnumerateDirectories(
                                root,
                                tool.FolderPattern))
                        {
                            string candidate =
                                Path.Combine(folder, relativePath);

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
                string expanded =
                    Environment.ExpandEnvironmentVariables(manifestPath);

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
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    },
                    StringSplitOptions.RemoveEmptyEntries
                );

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

                string candidateVersion =
                    GetToolVersion(tool, candidate);

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

            string readinessStatus = ready
                ? "Ready"
                : "Not Ready";

            string displayedVersion =
                version == "Unknown" ? "Not Available" : version;

            string reason;

            if (installedPath == null)
            {
                reason =
                    "Executable was not found in PATH or the manifest search locations.";
            }
            else if (!checkVersion)
            {
                reason = "";
            }
            else if (version == "Unknown")
            {
                reason =
                    "The executable was found, but its version could not be retrieved.";
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
                tool: tool
            );

            return ready;
        }

        // ==================================================
        // TOOL VERSION DETECTION
        // ==================================================

        private string GetToolVersion(EdaTool tool, string path)
        {
            try
            {
                if (string.Equals(
                    tool.Type,
                    "vscode",
                    StringComparison.OrdinalIgnoreCase))
                {
                    FileVersionInfo info =
                        FileVersionInfo.GetVersionInfo(path);

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
                    string output =
                        RunVersionCommand(path, arguments);

                    string parsedVersion =
                        ParseToolVersion(tool.Type, output);

                    if (parsedVersion != "Unknown")
                        return parsedVersion;
                }

                FileVersionInfo fallbackInfo =
                    FileVersionInfo.GetVersionInfo(path);

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

            if (string.Equals(
                toolType,
                "ghdl",
                StringComparison.OrdinalIgnoreCase))
            {
                match = Regex.Match(
                    output,
                    @"(?i)\bGHDL\s+v?(\d+(?:\.\d+){1,3})"
                );
            }
            else if (string.Equals(
                toolType,
                "vivado",
                StringComparison.OrdinalIgnoreCase))
            {
                match = Regex.Match(output, @"\b(\d{4}\.\d+)\b");
            }
            else
            {
                match = Regex.Match(
                    output,
                    @"\b(\d+(?:\.\d+){1,3})\b"
                );
            }

            return match.Success
                ? match.Groups[1].Value
                : "Unknown";
        }

        private string RunVersionCommand(
            string executablePath,
            string arguments)
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

                Task<string> outputTask =
                    process.StandardOutput.ReadToEndAsync();

                Task<string> errorTask =
                    process.StandardError.ReadToEndAsync();

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

        // ==================================================
        // VERSION APPROVAL
        // ==================================================

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
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                if (!Version.TryParse(maximumVersion, out Version? maximum) ||
                    installed.CompareTo(maximum) > 0)
                {
                    return false;
                }
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
                {
                    return $"Version is below the minimum approved version ({minimumVersion}).";
                }
            }

            if (!string.IsNullOrWhiteSpace(maximumVersion))
            {
                if (!Version.TryParse(maximumVersion, out Version? maximum))
                    return "The maximum approved version in the manifest is invalid.";

                if (installed.CompareTo(maximum) > 0)
                {
                    return $"Version is above the maximum approved version ({maximumVersion}).";
                }
            }

            return "Version is within the approved range.";
        }

        // ==================================================
        // EXTENSION AUDIT
        // ==================================================

        private bool AddExtensionResult(EdaExtension extension)
        {
            if (!extension.CheckEnabled)
            {
                CreateResultCard(
                    extension.Name,
                    "Not Checked",
                    "Not Checked",
                    "Not Available",
                    null,
                    "Extension check is disabled in the manifest."
                );

                return true;
            }

            string extensionsRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".vscode",
                "extensions"
            );

            string extensionsJsonPath = Path.Combine(
                extensionsRoot,
                "extensions.json"
            );

            string? installedPath = null;
            string installedId = "";
            string version = "Not Available";

            List<string> approvedIds =
                extension.ApprovedExtensions ?? new List<string>();

            if (approvedIds.Count == 0 &&
                !string.IsNullOrWhiteSpace(extension.ExtensionId))
            {
                approvedIds.Add(extension.ExtensionId);
            }

            if (File.Exists(extensionsJsonPath))
            {
                try
                {
                    using var stream = new FileStream(
                        extensionsJsonPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite
                    );
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string jsonContent = reader.ReadToEnd();

                    using JsonDocument document = JsonDocument.Parse(jsonContent);

                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entry in document.RootElement.EnumerateArray())
                        {
                            if (!entry.TryGetProperty("identifier", out JsonElement identifierElement) ||
                                !identifierElement.TryGetProperty("id", out JsonElement idElement))
                            {
                                continue;
                            }

                            string? entryId = idElement.GetString();

                            if (string.IsNullOrWhiteSpace(entryId))
                                continue;

                            bool isApproved = approvedIds.Any(id =>
                                string.Equals(id, entryId, StringComparison.OrdinalIgnoreCase));

                            if (!isApproved)
                                continue;

                            string? relativeLocation =
                                entry.TryGetProperty("relativeLocation", out JsonElement relElement)
                                    ? relElement.GetString()
                                    : null;

                            if (string.IsNullOrWhiteSpace(relativeLocation))
                                continue;

                            string fullPath = Path.GetFullPath(
                                Path.Combine(extensionsRoot, relativeLocation)
                            );

                            if (!Directory.Exists(fullPath))
                                continue;

                            installedPath = fullPath;
                            installedId = entryId;

                            string? entryVersion =
                                entry.TryGetProperty("version", out JsonElement versionElement)
                                    ? versionElement.GetString()
                                    : null;

                            version = !string.IsNullOrWhiteSpace(entryVersion)
                                ? entryVersion
                                : "Unknown";

                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            bool ready = installedPath != null;

            string displayName = ready
                ? GetExtensionDisplayName(installedId)
                : extension.Name;

            CreateResultCard(
                displayName,
                ready ? "Detected" : "Not Detected",
                ready ? "Ready" : "Not Ready",
                version,
                installedPath,
                ready
                    ? "Active extension entry found in extensions.json and folder exists."
                    : "No active approved extension entry with an existing folder was found.",
                extension: extension
            );

            return ready;
        }

        // ==================================================
        // EXTENSION DISPLAY NAMES
        // ==================================================

        private string GetExtensionDisplayName(string extensionId)
        {
            if (string.Equals(
                extensionId,
                "teros-technology.teroshdl",
                StringComparison.OrdinalIgnoreCase))
                return "TerosHDL";

            if (string.Equals(
                extensionId,
                "p2l2.vhdl-by-hgb",
                StringComparison.OrdinalIgnoreCase))
                return "VHDL by HGB";

            if (string.Equals(
                extensionId,
                "rjyoung.vscode-modern-vhdl-support",
                StringComparison.OrdinalIgnoreCase))
                return "Modern VHDL Support";

            if (string.Equals(
                extensionId,
                "vhdlwhiz.vhdl-by-vhdlwhiz",
                StringComparison.OrdinalIgnoreCase))
                return "VHDL by VHDLwhiz";

            return extensionId;
        }

        // ==================================================
        // RESULT CARDS
        // ==================================================

        private void CreateResultCard(
            string name,
            string installationStatus,
            string readinessStatus,
            string version,
            string? path,
            string reason = "",
            EdaTool? tool = null,
            EdaExtension? extension = null)
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
                if (remediationInProgress)
                    return;

                if (showSmokeTest)
                {
                    if (tool != null && path != null)
                    {
                        RunToolSmokeTest(tool, path);
                    }
                    else if (extension != null && path != null)
                    {
                        RunExtensionSmokeTest(extension, path);
                    }
                    else
                    {
                        MessageBox.Show(
                            this,
                            "No smoke test is configured for " + name + ".",
                            "Smoke Test",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
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

        private Color GetStatusColor(string status)
        {
            if (status == "Ready" || status == "Detected")
                return Color.ForestGreen;

            if (status == "Not Ready" || status == "Not Detected")
                return Color.Firebrick;

            return Color.DimGray;
        }

        // ==================================================
        // REMEDIATION
        // ==================================================

        private void RunRemediation(string componentName)
        {
            MessageBox.Show(
                this,
                "Remediation selected for " + componentName +
                ".\n\nRemediation functionality has not been implemented yet.",
                "Remediation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void RunRemediation(EdaTool tool)
        {
            if (remediationInProgress)
                return;

            if (tool.Remediation == null)
            {
                MessageBox.Show(
                    this,
                    "No remediation instructions are configured for " +
                    tool.Name + ".",
                    "Remediation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string method = tool.Remediation.Method ?? "manual";

            if (string.Equals(
                method,
                "winget",
                StringComparison.OrdinalIgnoreCase))
            {
                RunWingetRemediation(tool);
            }
            else
            {
                RunManualRemediation(tool);
            }
        }

        // ==================================================
        // WINGET REMEDIATION
        // ==================================================

        private async void RunWingetRemediation(EdaTool tool)
        {
            if (remediationInProgress)
                return;

            RemediationSettings settings = tool.Remediation!;

            if (string.IsNullOrWhiteSpace(settings.PackageId))
            {
                MessageBox.Show(
                    this,
                    "The WinGet package ID is missing from the manifest.",
                    "Remediation Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Version))
            {
                MessageBox.Show(
                    this,
                    "The WinGet installation version is missing from the manifest.",
                    "Remediation Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                return;
            }

            // --------------------------------------------------
            // CONFIRM INSTALLATION
            // --------------------------------------------------

            DialogResult confirmation = MessageBox.Show(
                this,
                "You are about to attempt installing " +
                tool.Name + " using WinGet.\n\n" +

                "INSTALLATION DETAILS\n" +
                "Tool: " + tool.Name + "\n" +
                "Package ID: " + settings.PackageId + "\n" +
                "Approved version: " + settings.Version + "\n" +
                "Installation method: WinGet\n\n" +

                "AGREEMENT NOTICE\n" +
                "The package and source agreements will be automatically " +
                "accepted as part of this installation attempt.\n\n" +

                "Installation progress and available output will be displayed.\n\n" +

                "Once the installation attempt finishes, run the audit again " +
                "to verify whether the tool was successfully installed and detected.\n\n" +

                "Would you like to proceed?",
                "Confirm " + tool.Name + " Installation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirmation != DialogResult.Yes)
            {
                statusLabel.Text = tool.Name + " remediation cancelled.";
                return;
            }

            WingetProgressForm? progressWindow = null;

            try
            {
                SetRemediationBusy(true);

                statusLabel.Text =
                    "Attempting to install " + tool.Name + " through WinGet...";

                progressWindow = new WingetProgressForm(tool.Name);

                progressWindow.Show(this);
                progressWindow.SetStatus("Starting WinGet installation...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--id");
                startInfo.ArgumentList.Add(settings.PackageId);
                startInfo.ArgumentList.Add("--exact");
                startInfo.ArgumentList.Add("--version");
                startInfo.ArgumentList.Add(settings.Version);
                startInfo.ArgumentList.Add("--silent");
                startInfo.ArgumentList.Add("--accept-package-agreements");
                startInfo.ArgumentList.Add("--accept-source-agreements");

                using var process = new Process
                {
                    StartInfo = startInfo
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "Windows could not start WinGet."
                    );
                }

                progressWindow.SetStatus(
                    "WinGet is installing " + tool.Name +
                    ". Waiting for installer output..."
                );

                progressWindow.AppendOutput(
                    "Starting WinGet installation."
                );

                progressWindow.AppendOutput(
                    "Package ID: " + settings.PackageId
                );

                progressWindow.AppendOutput(
                    "Selected version: " + settings.Version
                );

                progressWindow.AppendOutput("");

                Task outputTask = ReadProcessOutputAsync(
                    process.StandardOutput,
                    progressWindow,
                    ""
                );

                Task errorTask = ReadProcessOutputAsync(
                    process.StandardError,
                    progressWindow,
                    "[Error] "
                );

                Task exitTask = process.WaitForExitAsync();

                await Task.WhenAll(
                    outputTask,
                    errorTask,
                    exitTask
                );

                int exitCode = process.ExitCode;

                if (exitCode == 0)
                {
                    progressWindow.SetStatus(
                        "WinGet completed successfully."
                    );

                    progressWindow.AppendOutput("");
                    progressWindow.AppendOutput(
                        "WinGet reported that the installation command completed successfully."
                    );

                    RefreshProcessPath();

                    statusLabel.Text =
                        "WinGet installation attempt finished. Rerun the audit.";

                    MessageBox.Show(
                        this,
                        "WinGet reported that the installation process completed successfully.\n\n" +
                        "Package ID: " + settings.PackageId + "\n" +
                        "Selected version: " + settings.Version + "\n\n" +
                        "Please select Run Audit to verify " + tool.Name + ".",
                        "WinGet Installation Attempt Finished",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    string details = progressWindow.GetOutput();

                    progressWindow.SetStatus(
                        "Installation failed. Exit code: " + exitCode
                    );

                    statusLabel.Text =
                        "WinGet installation failed. Exit code: " + exitCode;

                    MessageBox.Show(
                        this,
                        "WinGet did not report a successful installation.\n\n" +
                        "Exit code: " + exitCode + "\n\n" +
                        "Installer output:\n" +
                        (string.IsNullOrWhiteSpace(details)
                            ? "No output was returned."
                            : details),
                        "WinGet Installation Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = tool.Name + " remediation failed.";

                MessageBox.Show(
                    this,
                    "An error occurred while attempting to install " +
                    tool.Name + " through WinGet.\n\n" +
                    ex.Message + "\n\n" +
                    "Make sure WinGet is available on this computer.",
                    "WinGet Remediation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                if (progressWindow != null)
                {
                    if (!progressWindow.IsDisposed)
                        progressWindow.Close();

                    progressWindow.Dispose();
                }

                SetRemediationBusy(false);
            }
        }

        // ==================================================
        // LIVE WINGET OUTPUT
        // ==================================================

        private static async Task ReadProcessOutputAsync(
            StreamReader reader,
            WingetProgressForm progressWindow,
            string prefix)
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync();

                if (line == null)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                    progressWindow.AppendOutput(prefix + line);
            }
        }

        // ==================================================
        // REMEDIATION CONTROL LOCKING
        // ==================================================

        private void SetRemediationBusy(bool busy)
        {
            remediationInProgress = busy;

            runAuditButton.Enabled = !busy;

            foreach (Control control in resultsPanel.Controls)
            {
                if (control is Panel card)
                {
                    foreach (Control child in card.Controls)
                    {
                        if (child is Button button)
                            button.Enabled = !busy;
                    }
                }
            }
        }

        // ==================================================
        // REFRESH PROCESS PATH
        // ==================================================

        private static void RefreshProcessPath()
        {
            string machinePath =
                Environment.GetEnvironmentVariable(
                    "Path",
                    EnvironmentVariableTarget.Machine
                ) ?? "";

            string userPath =
                Environment.GetEnvironmentVariable(
                    "Path",
                    EnvironmentVariableTarget.User
                ) ?? "";

            string combinedPath = string.Join(
                ";",
                (machinePath + ";" + userPath)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            );

            Environment.SetEnvironmentVariable(
                "Path",
                combinedPath,
                EnvironmentVariableTarget.Process
            );
        }

        // ==================================================
        // MANUAL REMEDIATION
        // ==================================================

        private void RunManualRemediation(EdaTool tool)
        {
            var sb = new StringBuilder();

            List<string> instructions =
                tool.Remediation!.Instructions ?? new List<string>();

            sb.AppendLine(
                $"Follow these steps to install {tool.Name}:\n"
            );

            for (int i = 0; i < instructions.Count; i++)
            {
                sb.AppendLine(
                    $"{i + 1}.  {instructions[i]}\n"
                );
            }

            if (!string.IsNullOrWhiteSpace(
                tool.Remediation.DownloadUrl))
            {
                sb.AppendLine(
                    $"Official Website / Download URL:\n" +
                    tool.Remediation.DownloadUrl
                );

                sb.AppendLine(
                    "\nWould you like to open the download page in your browser now?"
                );
            }

            DialogResult dialogResult = MessageBox.Show(
                this,
                sb.ToString(),
                $"Remediation Guide: {tool.Name}",

                !string.IsNullOrWhiteSpace(
                    tool.Remediation.DownloadUrl)
                    ? MessageBoxButtons.YesNo
                    : MessageBoxButtons.OK,

                MessageBoxIcon.Information
            );

            if (dialogResult == DialogResult.Yes &&
                !string.IsNullOrWhiteSpace(
                    tool.Remediation.DownloadUrl))
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
                        this,
                        "Could not open the browser: " + ex.Message,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }
    }

    // ==================================================
    // WINGET PROGRESS WINDOW
    // ==================================================

    public class WingetProgressForm : Form
    {
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private readonly TextBox outputTextBox;

        private readonly StringBuilder outputLog = new StringBuilder();

        public WingetProgressForm(string toolName)
        {
            Text = "Installing " + toolName;
            Size = new Size(720, 470);
            MinimumSize = new Size(600, 380);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(245, 247, 250);

            // Prevent accidental closure while WinGet is running.
            ControlBox = false;

            Label headingLabel = new Label
            {
                Text = "Installing " + toolName,
                Font = new Font("Segoe UI", 17, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 45, 60),
                Location = new Point(25, 20),
                AutoSize = true
            };

            statusLabel = new Label
            {
                Text = "Preparing installation...",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.DimGray,
                Location = new Point(28, 65),
                Size = new Size(640, 35),
                AutoEllipsis = true
            };

            progressBar = new ProgressBar
            {
                Location = new Point(28, 105),
                Size = new Size(640, 20),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            Label outputHeading = new Label
            {
                Text = "Installer Output",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 45, 60),
                Location = new Point(28, 145),
                AutoSize = true
            };

            outputTextBox = new TextBox
            {
                Location = new Point(28, 175),
                Size = new Size(640, 220),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9),
                Anchor = AnchorStyles.Top |
                         AnchorStyles.Bottom |
                         AnchorStyles.Left |
                         AnchorStyles.Right
            };

            Controls.Add(headingLabel);
            Controls.Add(statusLabel);
            Controls.Add(progressBar);
            Controls.Add(outputHeading);
            Controls.Add(outputTextBox);
        }

        public void SetStatus(string message)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => SetStatus(message)));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            statusLabel.Text = message;
        }

        public void AppendOutput(string line)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => AppendOutput(line)));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            outputLog.AppendLine(line);

            outputTextBox.AppendText(line + Environment.NewLine);

            // Keep the output log from growing indefinitely.
            const int maximumCharacters = 30000;

            if (outputTextBox.TextLength > maximumCharacters)
            {
                outputTextBox.Text =
                    outputTextBox.Text.Substring(
                        outputTextBox.TextLength - maximumCharacters
                    );

                outputTextBox.SelectionStart = outputTextBox.TextLength;
                outputTextBox.ScrollToCaret();
            }

            if (outputLog.Length > maximumCharacters)
            {
                outputLog.Remove(
                    0,
                    outputLog.Length - maximumCharacters
                );
            }

            outputTextBox.SelectionStart = outputTextBox.TextLength;
            outputTextBox.ScrollToCaret();
        }

        public string GetOutput()
        {
            if (IsDisposed)
                return outputLog.ToString();

            if (InvokeRequired)
            {
                try
                {
                    return (string)Invoke(
                        new Func<string>(() => outputLog.ToString())
                    );
                }
                catch (InvalidOperationException)
                {
                    return outputLog.ToString();
                }
            }

            return outputLog.ToString();
        }
    }

    // ==================================================
    // MANIFEST CLASSES
    // ==================================================

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

    public class EdaExtension
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Editor { get; set; } = "";

        public List<string> ApprovedExtensions { get; set; } = new();

        public string ExtensionId { get; set; } = "";
        public bool CheckEnabled { get; set; }

        public ComponentActions Actions { get; set; } = new();
        public SmokeTestSettings? SmokeTest { get; set; }
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
        public string WaveformFormat { get; set; } = "";

        public bool RequireUserConfirmation { get; set; }
        public List<string> ExpectedSignals { get; set; } = new();

        public string ExpectedLanguage { get; set; } = "";
        public List<string> ExpectedExtensions { get; set; } = new();
        public bool RequireGrammar { get; set; }
    }

    public class RemediationSettings
    {
        public string Method { get; set; } = "manual";
        public string PackageId { get; set; } = "";
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public List<string> Instructions { get; set; } = new();
    }

    public class ComponentActions
    {
        public bool ShowSmokeTest { get; set; }
        public bool ShowRemediation { get; set; }
    }
}