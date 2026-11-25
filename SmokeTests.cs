using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EDAEnvironmentManager
{
    public partial class AuditWindow
    {
        // ==================================================
        // SMOKE TEST DISPATCHER
        // ==================================================

        private void RunToolSmokeTest(
            EdaTool tool,
            string executablePath)
        {
            if (tool.SmokeTest == null || !tool.SmokeTest.Enabled)
            {
                MessageBox.Show(
                    this,
                    "Smoke test is disabled or not configured for " + tool.Name + ".",
                    "Smoke Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            if (string.Equals(
                tool.Type,
                "ghdl",
                StringComparison.OrdinalIgnoreCase))
            {
                RunGhdlSmokeTest(tool, executablePath);
            }
            else if (string.Equals(
                tool.Type,
                "surfer",
                StringComparison.OrdinalIgnoreCase))
            {
                RunSurferSmokeTest(tool, executablePath);
            }
            else if (string.Equals(
                tool.Type,
                "vscode",
                StringComparison.OrdinalIgnoreCase))
            {
                RunVsCodeSmokeTest(tool, executablePath);
            }
            else if (string.Equals(
                tool.Type,
                "vivado",
                StringComparison.OrdinalIgnoreCase))
            {
                RunVivadoSmokeTest(tool, executablePath);
            }
            else
            {
                MessageBox.Show(
                    this,
                    "No smoke-test implementation exists for " + tool.Name + ".",
                    "Smoke Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }

        // ==================================================
        // VIVADO SMOKE TEST
        // ==================================================

        private async void RunVivadoSmokeTest(
            EdaTool tool,
            string executablePath)
        {
            SmokeTestSettings settings = tool.SmokeTest!;

            if (!string.Equals(
                settings.Method,
                "batch-tcl",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "The Vivado smoke-test method must be configured as 'batch-tcl'.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.TestFile) ||
                string.IsNullOrWhiteSpace(settings.ExpectedOutput))
            {
                MessageBox.Show(
                    this,
                    "The Vivado smoke-test configuration is incomplete.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (settings.RequireUserConfirmation)
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    "PURPOSE OF THE SMOKE TEST\n\n" +
                    "This test checks whether Vivado can launch in batch mode " +
                    "and execute a basic Tcl script.\n\n" +

                    "TEST PROCEDURE\n" +
                    "1. A test folder will be created in the EDAudit application folder.\n" +
                    "2. A Tcl test script will be written into it.\n" +
                    "3. Vivado will execute the script in batch mode.\n" +
                    "4. The program will check the exit code and expected output.\n\n" +

                    "This test does not perform synthesis, implementation, " +
                    "or FPGA programming.\n\n" +

                    "Would you like to proceed?",
                    "Confirm Vivado Smoke Test",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirmation != DialogResult.Yes)
                {
                    statusLabel.Text = "Vivado smoke test cancelled.";
                    return;
                }
            }

            try
            {
                statusLabel.Text = "Running Vivado smoke test...";

                var result = await Task.Run(
                    () => ExecuteVivadoSmokeTest(settings, executablePath)
                );

                statusLabel.Text = result.Success
                    ? "Vivado smoke test passed."
                    : "Vivado smoke test failed.";

                MessageBox.Show(
                    this,
                    result.Details,
                    result.Success
                        ? "Smoke Test Passed"
                        : "Smoke Test Failed",
                    MessageBoxButtons.OK,
                    result.Success
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Error
                );
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Vivado smoke test failed.";

                MessageBox.Show(
                    this,
                    "An error occurred while running the Vivado smoke test:\n\n" +
                    ex.Message,
                    "Smoke Test Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private (bool Success, string Details) ExecuteVivadoSmokeTest(
            SmokeTestSettings settings,
            string executablePath)
        {
            string testDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "SmokeTests",
                "Vivado",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            );

            Directory.CreateDirectory(testDirectory);

            string testFileName = Path.GetFileName(settings.TestFile);

            string testFilePath = Path.Combine(
                testDirectory,
                testFileName
            );

            string runnerPath = Path.Combine(
                testDirectory,
                "run_vivado_smoke_test.cmd"
            );

            string logPath = Path.Combine(
                testDirectory,
                "vivado_output.log"
            );

            // Create the Tcl test script.
            string tclSource =
                "puts \"" + settings.ExpectedOutput + "\"\n" +
                "exit 0\n";

            File.WriteAllText(
                testFilePath,
                tclSource,
                new UTF8Encoding(false)
            );

            // Create a command file to launch Vivado's .bat executable.
            string runnerSource =
                "@echo off\r\n" +
                "call \"" + executablePath + "\" " +
                "-mode batch " +
                "-source \"" + testFilePath + "\" " +
                "-nolog -nojournal\r\n" +
                "exit /b %errorlevel%\r\n";

            File.WriteAllText(
                runnerPath,
                runnerSource,
                new UTF8Encoding(false)
            );

            var result = RunVivadoBatchCommand(
                runnerPath,
                testDirectory
            );

            // Retain Vivado's output for troubleshooting.
            File.WriteAllText(
                logPath,
                result.Output,
                new UTF8Encoding(false)
            );

            if (result.TimedOut)
            {
                return (
                    false,
                    "Vivado smoke test timed out after 120 seconds.\n\n" +
                    "Vivado may have encountered a startup, installation, or configuration issue.\n\n" +
                    "Output:\n" + result.Output +
                    "\n\nTest files and log retained at:\n" + testDirectory
                );
            }

            if (result.ExitCode != 0)
            {
                return (
                    false,
                    "Vivado exited with an error.\n\n" +
                    "Exit code: " + result.ExitCode +
                    "\n\nOutput:\n" + result.Output +
                    "\n\nTest files and log retained at:\n" + testDirectory
                );
            }

            if (result.Output.IndexOf(
                settings.ExpectedOutput,
                StringComparison.OrdinalIgnoreCase) < 0)
            {
                return (
                    false,
                    "Vivado exited, but the expected test message was not found.\n\n" +
                    "Expected:\n" + settings.ExpectedOutput +
                    "\n\nActual output:\n" + result.Output +
                    "\n\nTest files and log retained at:\n" + testDirectory
                );
            }

            return (
                true,
                "Vivado smoke test passed!\n\n" +
                "Vivado batch startup: Passed\n" +
                "Tcl script execution: Passed\n" +
                "Exit code: " + result.ExitCode + "\n" +
                "Expected output found: " + settings.ExpectedOutput +
                "\n\nTest files and log retained at:\n" + testDirectory
            );
        }

        private (int ExitCode, string Output, bool TimedOut)
            RunVivadoBatchCommand(
                string runnerPath,
                string workingDirectory)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c \"\"" + runnerPath + "\"\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(startInfo);

            if (process == null)
            {
                throw new InvalidOperationException(
                    "Windows could not start the Vivado smoke test."
                );
            }

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            // Allow up to two minutes for Vivado to start and run.
            if (!process.WaitForExit(120000))
            {
                process.Kill(true);
                process.WaitForExit();

                string timedOutOutput =
                    outputTask.GetAwaiter().GetResult();

                string timedOutError =
                    errorTask.GetAwaiter().GetResult();

                return (
                    -1,
                    timedOutOutput + Environment.NewLine + timedOutError,
                    true
                );
            }

            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();

            return (
                process.ExitCode,
                output + Environment.NewLine + error,
                false
            );
        }

        // ==================================================
        // VS CODE SMOKE TEST
        // ONLY CHECKS THAT VS CODE OPENS A FILE
        // ==================================================

        private void RunVsCodeSmokeTest(
            EdaTool tool,
            string executablePath)
        {
            SmokeTestSettings settings = tool.SmokeTest!;

            if (!string.Equals(
                settings.Method,
                "open-file",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "The VS Code smoke-test method must be configured as 'open-file'.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.TestFile))
            {
                MessageBox.Show(
                    this,
                    "The VS Code smoke test does not have a test filename configured.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (settings.RequireUserConfirmation)
            {
                DialogResult proceed = MessageBox.Show(
                    this,
                    "PURPOSE OF THE SMOKE TEST\n\n" +
                    "This test checks whether Visual Studio Code can launch and open a VHDL file.\n\n" +
                    "TEST PROCEDURE\n" +
                    "1. A small VHDL file will be created in the EDAudit application folder.\n" +
                    "2. Visual Studio Code will be launched with that file.\n" +
                    "3. You will confirm whether the file is visible in the editor.\n\n" +
                    "This test does not compile the VHDL or check extension syntax highlighting.\n\n" +
                    "Would you like to proceed?",
                    "Confirm VS Code Smoke Test",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (proceed != DialogResult.Yes)
                {
                    statusLabel.Text = "VS Code smoke test cancelled.";
                    return;
                }
            }

            try
            {
                statusLabel.Text = "Preparing VS Code smoke test...";

                string testDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "SmokeTests",
                    "VSCode",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                );

                Directory.CreateDirectory(testDirectory);

                string safeFileName = Path.GetFileName(settings.TestFile);

                if (!string.Equals(
                    Path.GetExtension(safeFileName),
                    ".vhd",
                    StringComparison.OrdinalIgnoreCase))
                {
                    safeFileName += ".vhd";
                }

                string testFilePath =
                    Path.Combine(testDirectory, safeFileName);

                const string vhdlSource =
                    "library ieee;\n" +
                    "use ieee.std_logic_1164.all;\n\n" +
                    "entity smoke_test is\n" +
                    "end entity smoke_test;\n\n" +
                    "architecture test of smoke_test is\n" +
                    "begin\n" +
                    "    -- VS Code smoke test: file opened successfully.\n" +
                    "end architecture test;\n";

                File.WriteAllText(
                    testFilePath,
                    vhdlSource,
                    new UTF8Encoding(false)
                );

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = testDirectory,
                    UseShellExecute = true
                };

                startInfo.ArgumentList.Add(testFilePath);

                Process? codeProcess = Process.Start(startInfo);

                if (codeProcess == null)
                {
                    throw new InvalidOperationException(
                        "Windows could not start Visual Studio Code."
                    );
                }

                statusLabel.Text =
                    "VS Code launched. Waiting for file confirmation...";

                DialogResult opened = MessageBox.Show(
                    this,
                    "Visual Studio Code has been launched with the generated VHDL file.\n\n" +
                    "Please check the VS Code window.\n\n" +
                    "Can you see '" + safeFileName + "' open in the editor?",
                    "Verify VS Code Smoke Test",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (opened == DialogResult.Yes)
                {
                    statusLabel.Text = "VS Code smoke test passed.";

                    MessageBox.Show(
                        this,
                        "VS Code successfully opened the test file according to your confirmation.",
                        "Smoke Test Passed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    statusLabel.Text = "VS Code smoke test failed.";

                    MessageBox.Show(
                        this,
                        "The test file was not confirmed as open in VS Code.",
                        "Smoke Test Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "VS Code smoke test failed.";

                MessageBox.Show(
                    this,
                    "An error occurred while running the VS Code smoke test:\n\n" +
                    ex.Message,
                    "Smoke Test Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // ==================================================
        // VHDL EXTENSION SMOKE TEST
        // AUTOMATED CHECKS FIRST, THEN ONE CONTINUOUS
        // USER-APPROVED VISUAL TEST PROCESS
        // ==================================================

        private void RunExtensionSmokeTest(
            EdaExtension extension,
            string extensionFolder)
        {
            SmokeTestSettings? settings = extension.SmokeTest;

            if (settings == null || !settings.Enabled)
            {
                MessageBox.Show(
                    this,
                    "The extension smoke test is disabled or not configured.",
                    "Smoke Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            string? testFilePath = null;

            try
            {
                // ==================================================
                // STAGE 1: AUTOMATED CHECKS
                // ==================================================

                DialogResult automatedConfirmation = MessageBox.Show(
                    this,
                    "AUTOMATED EXTENSION CHECKS\n\n" +
                    "This stage checks the installed VHDL extension.\n\n" +
                    "The following checks will be performed:\n\n" +
                    "1. Read the extension's package.json.\n" +
                    "2. Verify its approved extension ID.\n" +
                    "3. Check its VHDL language declaration.\n" +
                    "4. Check that both .vhd and .vhdl are declared.\n" +
                    "5. Check its VHDL grammar contribution.\n" +
                    "6. Verify that the grammar file exists.\n\n" +
                    "No test files will be created and VS Code will not be launched during these checks.\n\n" +
                    "Would you like to continue?",
                    "Automated Extension Checks",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (automatedConfirmation != DialogResult.Yes)
                {
                    statusLabel.Text =
                        "Automated extension checks cancelled.";
                    return;
                }

                statusLabel.Text =
                    "Starting automated extension checks...";

                // ------------------------------------------
                // LOCATE VS CODE
                // ------------------------------------------

                EdaTool? editor = manifest?.Tools.FirstOrDefault(
                    t => string.Equals(
                        t.Name,
                        extension.Editor,
                        StringComparison.OrdinalIgnoreCase)
                );

                if (editor == null)
                {
                    throw new Exception(
                        "The configured editor '" + extension.Editor +
                        "' could not be found in the manifest."
                    );
                }

                string? editorPath = FindExecutable(editor);

                if (editorPath == null)
                {
                    throw new Exception(
                        "The extension was detected, but Visual Studio Code could not be located.\n\n" +
                        "Use the VS Code remediation option to resolve this."
                    );
                }

                if (!string.Equals(
                    settings.Method,
                    "open-vhdl-file",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "The extension smoke-test method must be configured as 'open-vhdl-file'."
                    );
                }

                // ------------------------------------------
                // CHECK PACKAGE.JSON
                // ------------------------------------------

                statusLabel.Text =
                    "Checking extension package.json...";

                string packagePath = Path.Combine(
                    extensionFolder,
                    "package.json"
                );

                if (!File.Exists(packagePath))
                {
                    throw new Exception(
                        "The installed extension does not contain package.json."
                    );
                }

                using JsonDocument document =
                    JsonDocument.Parse(File.ReadAllText(packagePath));

                JsonElement root = document.RootElement;

                StringBuilder results = new StringBuilder();

                results.AppendLine("AUTOMATED CHECK RESULTS");
                results.AppendLine("=======================\n");

                // ------------------------------------------
                // CHECK EXTENSION IDENTITY
                // ------------------------------------------

                string publisher = GetJsonString(root, "publisher");
                string packageName = GetJsonString(root, "name");

                string installedId = publisher + "." + packageName;

                List<string> approvedIds =
                    extension.ApprovedExtensions ?? new List<string>();

                bool approvedId = approvedIds.Any(
                    id => string.Equals(
                        id,
                        installedId,
                        StringComparison.OrdinalIgnoreCase)
                );

                if (!approvedId &&
                    !string.Equals(
                        extension.ExtensionId,
                        installedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "The installed extension ID does not match an approved extension ID.\n\n" +
                        "Detected ID: " + installedId
                    );
                }

                results.AppendLine("[PASS] Approved extension ID");
                results.AppendLine("       " + installedId + "\n");

                // ------------------------------------------
                // CHECK VHDL LANGUAGE DECLARATION
                // ------------------------------------------

                statusLabel.Text =
                    "Checking VHDL language declaration...";

                if (!root.TryGetProperty(
                    "contributes",
                    out JsonElement contributes))
                {
                    throw new Exception(
                        "The extension package does not contain a contributes section."
                    );
                }

                if (!contributes.TryGetProperty(
                    "languages",
                    out JsonElement languages) ||
                    languages.ValueKind != JsonValueKind.Array)
                {
                    throw new Exception(
                        "The extension does not declare any contributed languages."
                    );
                }

                string expectedLanguage =
                    string.IsNullOrWhiteSpace(settings.ExpectedLanguage)
                        ? "vhdl"
                        : settings.ExpectedLanguage;

                List<string> requiredExtensions = new List<string>
                {
                    ".vhd",
                    ".vhdl"
                };

                List<string> configuredExtensions =
                    settings.ExpectedExtensions ?? new List<string>();

                foreach (string requiredExtension in requiredExtensions)
                {
                    if (!configuredExtensions.Contains(
                        requiredExtension,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            "The manifest must require both .vhd and .vhdl."
                        );
                    }
                }

                bool languageFound = false;

                foreach (JsonElement language in languages.EnumerateArray())
                {
                    string id = GetJsonString(language, "id");

                    if (!string.Equals(
                        id,
                        expectedLanguage,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    languageFound = true;

                    if (!language.TryGetProperty(
                        "extensions",
                        out JsonElement extensionsElement) ||
                        extensionsElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new Exception(
                            "The VHDL language declaration has no file extensions."
                        );
                    }

                    List<string> declaredExtensions =
                        extensionsElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? "")
                        .ToList();

                    foreach (string requiredExtension in requiredExtensions)
                    {
                        if (!declaredExtensions.Contains(
                            requiredExtension,
                            StringComparer.OrdinalIgnoreCase))
                        {
                            throw new Exception(
                                "The extension does not declare the required file extension: " +
                                requiredExtension
                            );
                        }

                        results.AppendLine(
                            "[PASS] File extension " + requiredExtension
                        );
                    }

                    break;
                }

                if (!languageFound)
                {
                    throw new Exception(
                        "The extension does not declare the expected language ID: " +
                        expectedLanguage
                    );
                }

                results.AppendLine(
                    "[PASS] VHDL language declaration (" +
                    expectedLanguage + ")\n"
                );

                // ------------------------------------------
                // CHECK GRAMMAR CONTRIBUTION
                // ------------------------------------------

                if (settings.RequireGrammar)
                {
                    statusLabel.Text =
                        "Checking VHDL grammar contribution...";

                    if (!contributes.TryGetProperty(
                        "grammars",
                        out JsonElement grammars) ||
                        grammars.ValueKind != JsonValueKind.Array)
                    {
                        throw new Exception(
                            "The extension does not declare a grammar contribution."
                        );
                    }

                    bool grammarFound = false;

                    foreach (JsonElement grammar in grammars.EnumerateArray())
                    {
                        string grammarLanguage =
                            GetJsonString(grammar, "language");

                        string grammarPath =
                            GetJsonString(grammar, "path");

                        if (!string.Equals(
                            grammarLanguage,
                            expectedLanguage,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(grammarPath))
                            continue;

                        string fullGrammarPath = Path.GetFullPath(
                            Path.Combine(extensionFolder, grammarPath)
                        );

                        string fullExtensionPath =
                            Path.GetFullPath(extensionFolder) +
                            Path.DirectorySeparatorChar;

                        if (!fullGrammarPath.StartsWith(
                            fullExtensionPath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new Exception(
                                "The grammar path points outside the extension folder."
                            );
                        }

                        if (!File.Exists(fullGrammarPath))
                        {
                            throw new Exception(
                                "The declared grammar file could not be found:\n" +
                                grammarPath
                            );
                        }

                        grammarFound = true;

                        results.AppendLine(
                            "[PASS] VHDL grammar contribution"
                        );

                        results.AppendLine(
                            "[PASS] Grammar file exists"
                        );

                        results.AppendLine(
                            "       " + grammarPath + "\n"
                        );

                        break;
                    }

                    if (!grammarFound)
                    {
                        throw new Exception(
                            "No valid grammar contribution was found for " +
                            expectedLanguage + "."
                        );
                    }
                }

                // ==================================================
                // STAGE 2: DISPLAY AUTOMATED RESULTS
                // ==================================================

                results.AppendLine(
                    "All configured automated checks passed."
                );

                statusLabel.Text =
                    "Automated extension checks passed.";

                MessageBox.Show(
                    this,
                    results.ToString(),
                    "Automated Check Results",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                // ==================================================
                // STAGE 3: BEGIN THE VISUAL TEST PROCESS
                // ==================================================

                DialogResult continueVisualTest = MessageBox.Show(
                    this,
                    "The automated extension checks have passed.\n\n" +
                    "Would you like to continue with the visual smoke test?\n\n" +
                    "If you select Yes, EDAudit will:\n\n" +
                    "1. Create a timestamped smoke-test folder.\n" +
                    "2. Generate a VHDL test file.\n" +
                    "3. Show you the Workspace Trust guidance.\n" +
                    "4. Ask whether you want to open VS Code.\n\n" +
                    "If you select No, the visual test will stop. No test file will be created and VS Code will not be launched.",
                    "Continue with Visual Smoke Test?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (continueVisualTest != DialogResult.Yes)
                {
                    statusLabel.Text =
                        "Automated checks passed. Visual test cancelled.";

                    return;
                }

                // ==================================================
                // CREATE THE FILE AND CONTINUE THE VISUAL TEST
                // ==================================================

                statusLabel.Text =
                    "Preparing VHDL visual smoke test...";

                string testDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "SmokeTests",
                    "VHDLExtension",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                );

                Directory.CreateDirectory(testDirectory);

                string testFileName =
                    Path.GetFileName(settings.TestFile);

                if (string.IsNullOrWhiteSpace(testFileName))
                {
                    testFileName = "extension_smoke_test.vhd";
                }

                string testFileExtension =
                    Path.GetExtension(testFileName);

                if (!string.Equals(
                    testFileExtension,
                    ".vhd",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                    testFileExtension,
                    ".vhdl",
                    StringComparison.OrdinalIgnoreCase))
                {
                    testFileName += ".vhd";
                }

                testFilePath =
                    Path.Combine(testDirectory, testFileName);

                const string vhdlSource =
                    "library ieee;\n" +
                    "use ieee.std_logic_1164.all;\n\n" +
                    "entity extension_smoke_test is\n" +
                    "    port (\n" +
                    "        clk : in std_logic;\n" +
                    "        data : in std_logic_vector(7 downto 0)\n" +
                    "    );\n" +
                    "end entity extension_smoke_test;\n\n" +
                    "architecture rtl of extension_smoke_test is\n" +
                    "    signal internal_signal : std_logic := '0';\n" +
                    "begin\n" +
                    "    process(clk)\n" +
                    "    begin\n" +
                    "        if rising_edge(clk) then\n" +
                    "            internal_signal <= data(0);\n" +
                    "        end if;\n" +
                    "    end process;\n" +
                    "end architecture rtl;\n";

                File.WriteAllText(
                    testFilePath,
                    vhdlSource,
                    new UTF8Encoding(false)
                );

                statusLabel.Text =
                    "VHDL test file created. Waiting for VS Code launch decision...";

                // ==================================================
                // WORKSPACE TRUST GUIDANCE
                // ==================================================

                DialogResult openVsCode = MessageBox.Show(
                    this,
                    "VHDL VISUAL SMOKE TEST\n\n" +
                    "Your VHDL test file has been created.\n\n" +
                    "WORKSPACE TRUST\n\n" +
                    "When VS Code opens the generated folder, you may see a Restricted Mode message at the top of the window.\n\n" +
                    "If you are comfortable trusting this generated smoke-test folder:\n" +
                    "1. Click Manage in the Restricted Mode notification.\n" +
                    "2. Select Trust for this smoke-test folder.\n\n" +
                    "Once the file is open, look at the VHDL code. The keywords, data types and comments should appear in different colours.\n\n" +
                    "You can return to EDAudit afterward to report whether the highlighting works.\n\n" +
                    "IMPORTANT:\n" +
                    "Only trust the generated folder if you are comfortable doing so. You can remain in Restricted Mode or cancel the test instead.\n\n" +
                    "Would you like to open Visual Studio Code and continue?",
                    "VHDL Smoke Test - Workspace Trust",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (openVsCode != DialogResult.Yes)
                {
                    statusLabel.Text =
                        "Visual test cancelled before VS Code launch.";

                    MessageBox.Show(
                        this,
                        "The visual smoke test has been cancelled.\n\n" +
                        "Visual Studio Code was not launched.\n\n" +
                        "Your generated VHDL test file has been retained at:\n\n" +
                        testFilePath,
                        "Visual Test Cancelled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    return;
                }

                // ==================================================
                // LAUNCH VS CODE
                // ==================================================

                statusLabel.Text =
                    "Launching Visual Studio Code...";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = editorPath,
                    WorkingDirectory = testDirectory,
                    UseShellExecute = true
                };

                startInfo.ArgumentList.Add(testFilePath);

                Process? process = Process.Start(startInfo);

                if (process == null)
                {
                    throw new Exception(
                        "Windows could not start Visual Studio Code."
                    );
                }

                statusLabel.Text =
                    "VS Code opened. Waiting for syntax-highlighting confirmation...";

                // ==================================================
                // VERIFY SYNTAX HIGHLIGHTING
                // ==================================================

                DialogResult highlighted = MessageBox.Show(
                    this,
                    "Does the VHDL syntax highlighting appear to be working correctly?\n\n" +
                    "Look for different colours applied to the VHDL keywords, data types and comments.\n\n" +
                    "Select Yes if the highlighting is working.",
                    "Verify VHDL Syntax Highlighting",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (highlighted == DialogResult.Yes)
                {
                    statusLabel.Text =
                        "VHDL extension smoke test passed.";

                    MessageBox.Show(
                        this,
                        "VHDL extension smoke test passed!\n\n" +
                        "Automated metadata checks: Passed\n" +
                        "VS Code file opening: Confirmed\n" +
                        "Syntax highlighting: User confirmed\n\n" +
                        "Test file retained at:\n" + testFilePath,
                        "Smoke Test Passed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    statusLabel.Text =
                        "VHDL extension smoke test not confirmed.";

                    MessageBox.Show(
                        this,
                        "The automated extension checks passed, but syntax highlighting was not confirmed.\n\n" +
                        "Check that the VHDL extension is enabled and that the workspace is trusted if you chose to trust it.\n\n" +
                        "Test file retained at:\n" + testFilePath,
                        "Smoke Test Not Passed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    "VHDL extension smoke test failed.";

                string retainedFileMessage =
                    !string.IsNullOrWhiteSpace(testFilePath) &&
                    File.Exists(testFilePath)
                        ? "\n\nThe generated test file was retained at:\n" +
                          testFilePath
                        : "";

                MessageBox.Show(
                    this,
                    "The VHDL extension smoke test failed:\n\n" +
                    ex.Message +
                    retainedFileMessage,
                    "Extension Smoke Test Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private string GetJsonString(
            JsonElement element,
            string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(
                    propertyName,
                    out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        // ==================================================
        // GHDL SMOKE TEST
        // ==================================================

        private async void RunGhdlSmokeTest(
            EdaTool tool,
            string executablePath)
        {
            DialogResult confirmation = MessageBox.Show(
                this,
                "This smoke test will check whether GHDL can successfully " +
                "analyze, elaborate, and simulate a basic VHDL design.\n\n" +
                "The following actions will take place:\n\n" +
                "1. A test folder will be created inside the EDAudit application folder.\n" +
                "2. A VHDL test file will be written into it.\n" +
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
                    this,
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
                    this,
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
            string testDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "SmokeTests",
                "GHDL",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            );

            Directory.CreateDirectory(testDirectory);

            string testFileName = Path.GetFileName(settings.TestFile);

            string testFilePath =
                Path.Combine(testDirectory, testFileName);

            File.WriteAllText(
                testFilePath,
                settings.Source,
                new UTF8Encoding(false)
            );

            string standard = settings.Standard;

            var analyze = RunGhdlCommand(
                executablePath,
                testDirectory,
                "-a",
                "--std=" + standard,
                testFileName
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

            if (simulation.Output.IndexOf(
                settings.ExpectedOutput,
                StringComparison.OrdinalIgnoreCase) < 0)
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

        // ==================================================
        // GHDL COMMAND RUNNER
        // ==================================================

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

            return (
                process.ExitCode,
                output + Environment.NewLine + error
            );
        }

        // ==================================================
        // SURFER SMOKE TEST
        // ==================================================

        private async void RunSurferSmokeTest(
            EdaTool tool,
            string executablePath)
        {
            SmokeTestSettings settings = tool.SmokeTest!;

            if (string.IsNullOrWhiteSpace(settings.TestFile))
            {
                MessageBox.Show(
                    this,
                    "The Surfer smoke test does not have a test filename configured.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (!string.Equals(
                settings.WaveformFormat,
                "vcd",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "This Surfer smoke-test implementation currently supports VCD files only.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            List<string> expectedSignals =
                (settings.ExpectedSignals ?? new List<string>())
                .Where(signal => !string.IsNullOrWhiteSpace(signal))
                .Select(signal => signal.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (expectedSignals.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Add at least one expected signal to the Surfer smoke-test configuration.",
                    "Smoke Test Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            foreach (string signal in expectedSignals)
            {
                if (!Regex.IsMatch(
                    signal,
                    @"^[A-Za-z_][A-Za-z0-9_$]*$"))
                {
                    MessageBox.Show(
                        this,
                        "The expected signal name '" + signal +
                        "' is not a valid simple VCD signal name.",
                        "Smoke Test Configuration Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                "PURPOSE OF THE SMOKE TEST\n\n" +
                "This test checks whether Surfer can open a VCD file and display its waveform signals.\n\n" +
                "TEST PROCEDURE\n" +
                "1. Generate a sample VCD file containing test signals.\n" +
                "2. Launch Surfer with the generated VCD file.\n" +
                "3. Confirm that the waveform signals are visible.\n\n" +
                "EXPECTED RESULT\n" +
                "The generated signals should appear in Surfer's waveform display.\n\n" +
                "The generated test file will be retained inside the EDAudit application folder.\n\n" +
                "Would you like to proceed?",
                "Confirm Surfer Smoke Test",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirmation != DialogResult.Yes)
            {
                statusLabel.Text = "Surfer smoke test cancelled.";
                return;
            }

            try
            {
                statusLabel.Text = "Preparing Surfer smoke test...";

                string testDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "SmokeTests",
                    "Surfer",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                );

                Directory.CreateDirectory(testDirectory);

                string safeFileName =
                    Path.GetFileName(settings.TestFile);

                if (!string.Equals(
                    Path.GetExtension(safeFileName),
                    ".vcd",
                    StringComparison.OrdinalIgnoreCase))
                {
                    safeFileName += ".vcd";
                }

                string testFilePath =
                    Path.Combine(testDirectory, safeFileName);

                string commandFilePath =
                    Path.Combine(testDirectory, "smoke_test.sucl");

                string vcdContent =
                    CreateSurferTestVcd(expectedSignals);

                File.WriteAllText(
                    testFilePath,
                    vcdContent,
                    new UTF8Encoding(false)
                );

                File.WriteAllText(
                    commandFilePath,
                    "scope_add smoke_test" + Environment.NewLine,
                    new UTF8Encoding(false)
                );

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = testDirectory,
                    UseShellExecute = true,
                    Arguments =
                        "--command-file \"" + commandFilePath +
                        "\" \"" + testFilePath + "\""
                };

                Process? surferProcess = Process.Start(startInfo);

                if (surferProcess == null)
                {
                    throw new InvalidOperationException(
                        "Windows could not start Surfer."
                    );
                }

                statusLabel.Text =
                    "Surfer opened. Waiting for waveform inspection...";

                await Task.Delay(1800);

                DialogResult inspected = MessageBox.Show(
                    this,
                    "Surfer has been launched with the generated VCD file and the waveform command file.\n\n" +
                    "Please inspect the Surfer window and confirm that the waveform is displayed and that the test signals are visible.\n\n" +
                    string.Join("\n", expectedSignals.Select(signal => "• " + signal)) +
                    "\n\nCan you see the waveform and its signals?",
                    "Verify Surfer Waveform",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (inspected == DialogResult.Yes)
                {
                    statusLabel.Text = "Surfer smoke test passed.";

                    MessageBox.Show(
                        this,
                        "Surfer smoke test passed.\n\n" +
                        "Surfer was launched with the generated VCD file, and you confirmed " +
                        "that the waveform and expected signals were visible.\n\n" +
                        "VCD file retained at:\n" + testFilePath +
                        "\n\nCommand file retained at:\n" + commandFilePath,
                        "Smoke Test Passed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    statusLabel.Text =
                        "Surfer smoke test not confirmed.";

                    MessageBox.Show(
                        this,
                        "The Surfer smoke test was not confirmed.\n\n" +
                        "Check that Surfer opened the VCD file and that the expected signals are visible.\n\n" +
                        "Test file:\n" + testFilePath,
                        "Smoke Test Not Passed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Surfer smoke test failed.";

                MessageBox.Show(
                    this,
                    "An error occurred while running the Surfer smoke test:\n\n" +
                    ex.Message,
                    "Smoke Test Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // ==================================================
        // SURFER VCD GENERATOR
        // ==================================================

        private string CreateSurferTestVcd(
            List<string> signalNames)
        {
            StringBuilder vcd = new StringBuilder();

            vcd.AppendLine("$date");
            vcd.AppendLine("    Generated by Windows EDA Environment Audit");
            vcd.AppendLine("$end");
            vcd.AppendLine("$version");
            vcd.AppendLine("    Surfer smoke test");
            vcd.AppendLine("$end");
            vcd.AppendLine("$timescale 1ns $end");
            vcd.AppendLine("$scope module smoke_test $end");

            for (int i = 0; i < signalNames.Count; i++)
            {
                string identifier = "sig" + i;

                vcd.AppendLine(
                    "$var wire 1 " + identifier + " " +
                    signalNames[i] + " $end"
                );
            }

            vcd.AppendLine("$upscope $end");
            vcd.AppendLine("$enddefinitions $end");
            vcd.AppendLine();

            vcd.AppendLine("#0");

            for (int i = 0; i < signalNames.Count; i++)
            {
                string identifier = "sig" + i;
                vcd.AppendLine("0" + identifier);
            }

            for (int step = 1; step <= 4; step++)
            {
                vcd.AppendLine("#" + (step * 5));

                for (int i = 0; i < signalNames.Count; i++)
                {
                    string identifier = "sig" + i;
                    int value = (step + i) % 2;

                    vcd.AppendLine(value + identifier);
                }
            }

            return vcd.ToString();
        }
    }
}