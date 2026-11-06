
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace EDAEnvironmentAudit
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
            Size = new Size(1050, 700);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(850, 550);
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
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 45, 60),
                AutoSize = true,
                Location = new Point(30, 25)
            };

            Label description = new Label
            {
                Text = "Check installed EDA tools and their approved configurations.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(33, 75)
            };

            runAuditButton = new Button
            {
                Text = "Run Audit",
                Size = new Size(130, 40),
                Location = new Point(30, 115),
                BackColor = Color.FromArgb(35, 110, 190),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            runAuditButton.FlatAppearance.BorderSize = 0;
            runAuditButton.Click += RunAuditButton_Click;

            statusLabel = new Label
            {
                Text = "Ready to run audit.",
                Font = new Font("Segoe UI", 10),
                AutoSize = true,
                Location = new Point(180, 127),
                ForeColor = Color.DimGray
            };

            overallStatusLabel = new Label
            {
                Text = "Overall Environment: Not Audited",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(30, 165),
                ForeColor = Color.DimGray
            };

            resultsPanel = new FlowLayoutPanel
            {
                Location = new Point(30, 205),
                Size = new Size(970, 420),
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

            // Audit tools.

            foreach (EdaTool tool in manifest.Tools)
            {
                bool toolReady = AddToolResult(tool);

                if (!toolReady)
                    allReady = false;

                totalComponents++;
            }

            // Audit extensions.

            foreach (EdaExtension extension in manifest.Extensions)
            {
                bool extensionReady = AddExtensionResult(extension);

                if (!extensionReady)
                    allReady = false;

                totalComponents++;
            }

            // Overall environment readiness.

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

        private string? FindExecutable(EdaTool tool)
        {
            List<string> paths = new List<string>();

            if (tool.CommonPaths != null)
                paths.AddRange(tool.CommonPaths);

            if (tool.ExpectedPaths != null)
                paths.AddRange(tool.ExpectedPaths);

            foreach (string path in paths.Distinct())
            {
                try
                {
                    string expandedPath =
                        Environment.ExpandEnvironmentVariables(path);

                    if (File.Exists(expandedPath))
                        return expandedPath;
                }
                catch
                {
                    // Skip invalid paths.
                }
            }

            return null;
        }

        private bool AddToolResult(EdaTool tool)
        {
            string? installedPath = FindExecutable(tool);

            string installationStatus;
            string readinessStatus;
            string version = "Not Available";

            if (installedPath == null)
            {
                installationStatus = "Not Detected";
                readinessStatus = "Not Ready";
            }
            else
            {
                installationStatus = "Detected";

                version = GetToolVersion(tool, installedPath);

                if (version == "Unknown")
                {
                    version = "Not Available";
                    readinessStatus = "Not Ready";
                }
                else if (IsVersionApproved(
                    version,
                    tool.MinimumApprovedVersion))
                {
                    readinessStatus = "Ready";
                }
                else
                {
                    readinessStatus = "Not Ready";
                }
            }

            CreateResultCard(
                tool.Name,
                installationStatus,
                readinessStatus,
                version,
                installedPath
            );

            return readinessStatus == "Ready";
        }

        // --------------------------------------------------
        // TOOL VERSION DETECTION
        // --------------------------------------------------

        private string GetToolVersion(EdaTool tool, string path)
        {
            try
            {
                // VS Code: Read Windows file version.

                if (string.Equals(
                    tool.Type,
                    "vscode",
                    StringComparison.OrdinalIgnoreCase))
                {
                    FileVersionInfo info =
                        FileVersionInfo.GetVersionInfo(path);

                    return !string.IsNullOrWhiteSpace(info.FileVersion)
                        ? info.FileVersion
                        : "Unknown";
                }

                // Vivado: Execute its version command.

                if (string.Equals(
                    tool.Type,
                    "vivado",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string arguments =
                        string.IsNullOrWhiteSpace(tool.VersionArguments)
                        ? "-version"
                        : tool.VersionArguments;

                    string output = RunVersionCommand(path, arguments);

                    Match match = Regex.Match(
                        output,
                        @"\d{4}\.\d+"
                    );

                    return match.Success ? match.Value : "Unknown";
                }

                // Other tools: Read Windows file version.

                FileVersionInfo fileInfo =
                    FileVersionInfo.GetVersionInfo(path);

                return !string.IsNullOrWhiteSpace(fileInfo.FileVersion)
                    ? fileInfo.FileVersion
                    : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string RunVersionCommand(
            string executablePath,
            string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /s /c \"\"" +
                                executablePath + "\" " +
                                arguments + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(startInfo);

                if (process == null)
                    return "";

                var outputTask =
                    process.StandardOutput.ReadToEndAsync();

                var errorTask =
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

        // --------------------------------------------------
        // VERSION APPROVAL
        // --------------------------------------------------

        private bool IsVersionApproved(
            string installedVersion,
            string minimumVersion)
        {
            if (string.IsNullOrWhiteSpace(minimumVersion))
                return true;

            if (!Version.TryParse(
                installedVersion,
                out Version? installed))
                return false;

            if (!Version.TryParse(
                minimumVersion,
                out Version? minimum))
                return false;

            return installed.CompareTo(minimum) >= 0;
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

            // Disabled extensions are excluded from
            // the overall environment readiness calculation.

            if (!extension.CheckEnabled)
            {
                CreateResultCard(
                    extension.Name,
                    installationStatus,
                    readinessStatus,
                    version,
                    installedPath
                );

                return true;
            }

            EdaTool? editor = manifest?.Tools.FirstOrDefault(
                t => string.Equals(
                    t.Name,
                    extension.Editor,
                    StringComparison.OrdinalIgnoreCase)
            );

            if (editor == null)
            {
                installationStatus = "Not Detected";
                readinessStatus = "Not Ready";
            }
            else if (FindExecutable(editor) == null)
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

                if (!Directory.Exists(extensionsPath))
                {
                    installationStatus = "Not Detected";
                    readinessStatus = "Not Ready";
                }
                else
                {
                    bool found = TryFindInstalledExtension(
                        extensionsPath,
                        extension.ExtensionId,
                        out version,
                        out installedPath
                    );

                    if (found)
                    {
                        installationStatus = "Detected";
                        readinessStatus = "Ready";
                    }
                    else
                    {
                        installationStatus = "Not Detected";
                        readinessStatus = "Not Ready";
                    }
                }
            }

            CreateResultCard(
                extension.Name,
                installationStatus,
                readinessStatus,
                version,
                installedPath
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
            out string? installedPath)
        {
            version = "Not Available";
            installedPath = null;

            try
            {
                foreach (string folder in
                    Directory.EnumerateDirectories(extensionsPath))
                {
                    string packagePath = Path.Combine(
                        folder,
                        "package.json"
                    );

                    if (!File.Exists(packagePath))
                        continue;

                    try
                    {
                        string json = File.ReadAllText(packagePath);

                        using JsonDocument document =
                            JsonDocument.Parse(json);

                        JsonElement root = document.RootElement;

                        if (!root.TryGetProperty(
                            "publisher",
                            out JsonElement publisherElement))
                            continue;

                        if (!root.TryGetProperty(
                            "name",
                            out JsonElement nameElement))
                            continue;

                        string? publisher = publisherElement.GetString();
                        string? name = nameElement.GetString();

                        if (string.IsNullOrWhiteSpace(publisher) ||
                            string.IsNullOrWhiteSpace(name))
                            continue;

                        string installedExtensionId =
                            publisher + "." + name;

                        if (!installedExtensionId.Equals(
                            requiredExtensionId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        installedPath = folder;

                        if (root.TryGetProperty(
                            "version",
                            out JsonElement versionElement))
                        {
                            version =
                                versionElement.GetString() ?? "Not Available";
                        }

                        return true;
                    }
                    catch (JsonException)
                    {
                        // Skip invalid package.json files.
                    }
                    catch (IOException)
                    {
                        // Skip files that cannot be read.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible files.
                    }
                }
            }
            catch
            {
                // Handle inaccessible extensions directory.
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
            string? path)
        {
            Panel card = new Panel
            {
                Size = new Size(925, 145),
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 12),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label nameLabel = new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(15, 10),
                AutoSize = true
            };

            Label installationLabel = new Label
            {
                Text = "Installation Status: " + installationStatus,
                Font = new Font("Segoe UI", 9),
                Location = new Point(15, 38),
                AutoSize = true,
                ForeColor = GetStatusColor(installationStatus)
            };

            Label readinessLabel = new Label
            {
                Text = "Readiness Status: " + readinessStatus,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(15, 60),
                AutoSize = true,
                ForeColor = GetStatusColor(readinessStatus)
            };

            Label versionLabel = new Label
            {
                Text = "Version: " + version,
                Font = new Font("Segoe UI", 9),
                Location = new Point(15, 83),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            Label pathLabel = new Label
            {
                Text = "Location: " + (path ?? "Not Available"),
                Font = new Font("Segoe UI", 8),
                Location = new Point(15, 106),
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.Gray
            };

            bool showSmokeTest = readinessStatus == "Ready";

            bool showRemediation = readinessStatus == "Not Ready";

            Button actionButton = new Button
            {
                Text = showSmokeTest ? "Smoke Test" : "Remediate",
                Location = new Point(730, 48),
                Size = new Size(160, 40),
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
                    RunSmokeTest(name);
                else if (showRemediation)
                    RunRemediation(name);
            };

            card.Controls.Add(nameLabel);
            card.Controls.Add(installationLabel);
            card.Controls.Add(readinessLabel);
            card.Controls.Add(versionLabel);
            card.Controls.Add(pathLabel);
            card.Controls.Add(actionButton);

            resultsPanel.Controls.Add(card);
        }

        // --------------------------------------------------
        // STATUS COLOURS
        // --------------------------------------------------

        private Color GetStatusColor(string status)
        {
            if (status == "Ready" ||
                status == "Detected")
            {
                return Color.ForestGreen;
            }

            if (status == "Not Ready" ||
                status == "Not Detected")
            {
                return Color.Firebrick;
            }

            return Color.DimGray;
        }

        // --------------------------------------------------
        // ACTION BUTTONS
        // --------------------------------------------------

        private void RunSmokeTest(string componentName)
        {
            MessageBox.Show(
                "Smoke Test selected for " + componentName +
                ".\n\nSmoke-test functionality has not been implemented yet.",
                "Smoke Test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

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

        public string MinimumApprovedVersion { get; set; } = "";

        public string VersionArguments { get; set; } = "";

        public List<string> CommonPaths { get; set; } = new();

        public List<string> ExpectedPaths { get; set; } = new();

        public ComponentActions Actions { get; set; } = new();
    }

    public class EdaExtension
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public string Editor { get; set; } = "";

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
