
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

            resultsPanel = new FlowLayoutPanel
            {
                Location = new Point(30, 175),
                Size = new Size(970, 450),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                         AnchorStyles.Left | AnchorStyles.Right
            };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(runAuditButton);
            Controls.Add(statusLabel);
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
                    statusLabel.Text = "Manifest file not found.";
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

                statusLabel.Text = "Manifest loaded. Ready to audit.";
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

            foreach (EdaTool tool in manifest.Tools)
            {
                AddToolResult(tool);
                totalComponents++;
            }

            foreach (EdaExtension extension in manifest.Extensions)
            {
                AddExtensionResult(extension);
                totalComponents++;
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

        private void AddToolResult(EdaTool tool)
        {
            string? installedPath = FindExecutable(tool);

            string version = "Unknown";
            string result;

            if (installedPath == null)
            {
                result = "Not detected";
            }
            else
            {
                version = GetToolVersion(tool, installedPath);

                if (version == "Unknown")
                {
                    result = "Installed - Version unavailable";
                }
                else if (IsVersionApproved(
                    version,
                    tool.MinimumApprovedVersion))
                {
                    result = "Installed - Approved";
                }
                else
                {
                    result = "Installed - Update required";
                }
            }

            CreateResultCard(
                tool.Name,
                result,
                version,
                installedPath
            );
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

        private bool IsVersionApproved(
            string installedVersion,
            string minimumVersion)
        {
            if (string.IsNullOrWhiteSpace(minimumVersion))
                return true;

            if (!Version.TryParse(installedVersion, out Version? installed))
                return false;

            if (!Version.TryParse(minimumVersion, out Version? minimum))
                return false;

            return installed.CompareTo(minimum) >= 0;
        }

        // --------------------------------------------------
        // EXTENSION DETECTION
        // NO VS CODE PROCESS IS LAUNCHED
        // --------------------------------------------------

        private void AddExtensionResult(EdaExtension extension)
        {
            string result = "Not checked";
            string version = "N/A";
            string? installedPath = null;

            if (extension.CheckEnabled)
            {
                EdaTool? editor = manifest?.Tools.FirstOrDefault(
                    t => string.Equals(
                        t.Name,
                        extension.Editor,
                        StringComparison.OrdinalIgnoreCase)
                );

                if (editor == null)
                {
                    result = "Editor not listed in manifest";
                }
                else if (FindExecutable(editor) == null)
                {
                    result = "Editor not detected";
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
                        result = "Extensions folder not found";
                    }
                    else
                    {
                        bool found = TryFindInstalledExtension(
                            extensionsPath,
                            extension.ExtensionId,
                            out version,
                            out installedPath
                        );

                        result = found
                            ? "Installed"
                            : "Not installed";
                    }
                }
            }

            CreateResultCard(
                extension.Name,
                result,
                version,
                installedPath
            );
        }

        // Searches the VS Code extensions directory
        // and reads each extension's package.json.

        private bool TryFindInstalledExtension(
            string extensionsPath,
            string requiredExtensionId,
            out string version,
            out string? installedPath)
        {
            version = "N/A";
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
                                versionElement.GetString() ?? "Unknown";
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
                // Handle an inaccessible extensions directory.
            }

            return false;
        }

        // --------------------------------------------------
        // RESULT CARDS
        // --------------------------------------------------

        private void CreateResultCard(
            string name,
            string result,
            string version,
            string? path)
        {
            Panel card = new Panel
            {
                Size = new Size(925, 125),
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 12),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label nameLabel = new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(15, 12),
                AutoSize = true
            };

            Label resultLabel = new Label
            {
                Text = "Status: " + result,
                Font = new Font("Segoe UI", 10),
                Location = new Point(15, 43),
                AutoSize = true,
                ForeColor = GetStatusColor(result)
            };

            Label versionLabel = new Label
            {
                Text = "Version: " + version,
                Font = new Font("Segoe UI", 9),
                Location = new Point(15, 70),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            Label pathLabel = new Label
            {
                Text = "Location: " + (path ?? "Not detected"),
                Font = new Font("Segoe UI", 8),
                Location = new Point(15, 94),
                AutoSize = true,
                MaximumSize = new Size(570, 0),
                ForeColor = Color.Gray
            };

            bool showSmokeTest =
                result == "Installed - Approved" ||
                result == "Installed";

            bool showRemediation =
                result == "Not detected" ||
                result == "Not installed" ||
                result == "Installed - Update required" ||
                result == "Editor not detected" ||
                result == "Extensions folder not found";

            Button actionButton = new Button
            {
                Text = showSmokeTest ? "Smoke Test" : "Remediate",
                Location = new Point(730, 45),
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
            card.Controls.Add(resultLabel);
            card.Controls.Add(versionLabel);
            card.Controls.Add(pathLabel);
            card.Controls.Add(actionButton);

            resultsPanel.Controls.Add(card);
        }

        // --------------------------------------------------
        // STATUS COLOURS
        // --------------------------------------------------

        private Color GetStatusColor(string result)
        {
            if (result.Contains("Approved") ||
                result == "Installed")
            {
                return Color.ForestGreen;
            }

            if (result.Contains("Not detected") ||
                result.Contains("Not installed") ||
                result.Contains("not found"))
            {
                return Color.Firebrick;
            }

            if (result.Contains("Update"))
                return Color.DarkOrange;

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
