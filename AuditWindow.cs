using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EDAEnvironmentManager.Audit;

namespace EDAEnvironmentManager
{
    public class AuditWindow : Form
    {
        private FlowLayoutPanel resultsPanel = null!;
        private Button runAuditButton = null!;
        private Label statusLabel = null!;
        private Label overallStatusLabel = null!;

        private EdaManifest? manifest;

        private readonly EnvironmentAuditor auditor = new EnvironmentAuditor();

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
            manifest = EdaManifestLoader.TryLoad(
                EdaManifestLoader.DefaultPath,
                out string statusMessage
            );

            statusLabel.Text = statusMessage;
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

            AuditReport report = auditor.Audit(manifest);

            foreach (ComponentAuditResult result in report.Components)
                AddResultCard(result);

            int totalComponents = report.Components.Count;

            if (report.OverallReady)
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
        // AUDIT RESULT -> CARD
        // The checks themselves are in EnvironmentAuditor.
        // --------------------------------------------------

        private void AddResultCard(ComponentAuditResult result)
        {
            if (result.Kind == ComponentKind.Tool)
            {
                CreateResultCard(
                    result.DisplayName,
                    result.InstallationStatus,
                    result.ReadinessStatus,
                    result.Version,
                    result.InstalledPath,
                    result.Reason,
                    result.Tool
                );

                return;
            }

            // Extension cards never showed a reason, except when the check is disabled.
            CreateResultCard(
                result.DisplayName,
                result.InstallationStatus,
                result.ReadinessStatus,
                result.Version,
                result.InstalledPath,
                result.Finding == AuditFinding.CheckDisabled ? result.Reason : ""
            );
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
}