using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Windows.Tests.Fakes;
using P3B.Remediation.Windows.VsCode;

namespace P3B.Remediation.Windows.Tests;

/// <summary>
/// The VS Code extension installer against a scripted process runner and a fake code.cmd:
/// VS Code is never started and nothing is installed. Identifiers and versions are fake.
/// </summary>
public class VsCodeExtensionInstallerTests
{
    private const string Install = @"C:\TEST\VS Code";
    private const string Script = Install + @"\bin\code.cmd";
    private const string Exe = Install + @"\Code.exe";
    private const string Cli = Install + @"\abc123\resources\app\out\cli.js";
    private const string ExtensionId = "test-publisher.test-extension";
    private const string Version = "1.2.3";

    /// <summary>The layout VS Code ships (as read from this machine's code.cmd).</summary>
    private const string ShippedScript = """
        @echo off
        setlocal
        set VSCODE_DEV=
        set ELECTRON_RUN_AS_NODE=1
        "%~dp0..\Code.exe" "%~dp0..\abc123\resources\app\out\cli.js" %*
        IF %ERRORLEVEL% NEQ 0 EXIT /b %ERRORLEVEL%
        endlocal
        """;

    private static readonly InstallTarget Target = new(new ToolReference("TEST-EXTENSION"), "Test extension", ExtensionId, Version);

    private static VsCodeCliLocator Locator(string? script = ShippedScript, string? found = Script, params string[] existing) =>
        new(new FakeCommandLocator(found), new FakeTextFileReader(script, existing.Length == 0 ? [Exe, Cli] : existing));

    /// <summary>Scripts are keyed on the argument after cli.js: the VS Code option.</summary>
    private static FakeProcessRunner Runner() => new() { KeyArgument = 1 };

    private static VsCodeExtensionInstaller Installer(FakeProcessRunner runner, VsCodeCliLocator? locator = null) =>
        new(locator ?? Locator(), runner, new VsCodeOptions(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(22)));

    private static string Listed(string version) => $"other.extension@9.9.9\r\n{ExtensionId}@{version}\r\n";

    private static RemediationAction Action(AuditIssueState state = AuditIssueState.Missing)
    {
        var tool = new ManifestTool(
            new ToolReference("TEST-EXTENSION"),
            "Test extension",
            [Version],
            new ManifestRemediation(RemediationMethod.VsCodeExtension, ExtensionId),
            kind: ManifestComponentKind.Extension);
        var request = new RemediationRequest(new ToolReference("TEST-EXTENSION"), new IssueReference("TEST-ISSUE"), state, state == AuditIssueState.UnapprovedVersion ? "0.0.1" : null);
        return new RemediationAction(request, tool);
    }

    private static async Task<ExecutionReportView> Execute(FakeProcessRunner runner, AuditIssueState state = AuditIssueState.Missing)
    {
        var service = new InstallationService([Installer(runner)]);
        var preview = service.CreatePreview(Action(state));
        var report = await service.ExecuteAsync(preview, ApprovalDecision.Approve(preview), CancellationToken.None);
        return new ExecutionReportView(report.Succeeded, report.Message, report.Reversal);
    }

    private sealed record ExecutionReportView(bool Succeeded, string Message, ReversalInfo? Reversal);

    [Fact]
    public void Shipped_code_cmd_is_read_and_Code_exe_is_run_directly_without_a_shell()
    {
        var location = Locator().Locate();

        Assert.NotNull(location.Cli);
        Assert.Equal(Exe, location.Cli.Executable);
        Assert.Equal(Cli, location.Cli.CliScript);

        var request = location.Cli.Request(TimeSpan.FromSeconds(5), "--version");
        Assert.Equal(Exe, request.FileName);
        Assert.Equal(new[] { Cli, "--version" }, request.Arguments);
        Assert.Equal("1", request.Environment!["ELECTRON_RUN_AS_NODE"]);
        Assert.Null(request.Environment["VSCODE_DEV"]);
        Assert.DoesNotContain("cmd", request.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_code_command_is_reported()
    {
        var location = Locator(found: null).Locate();

        Assert.Null(location.Cli);
        Assert.Contains("not found on PATH", location.Description);
    }

    [Theory]
    [InlineData("@echo off\r\n\"%~dp0..\\Code.exe\" \"%~dp0..\\abc123\\resources\\app\\out\\cli.js\" %*")]
    [InlineData("set ELECTRON_RUN_AS_NODE=1\r\n\"%~dp0..\\Code.exe\" \"%~dp0..\\abc123\\resources\\app\\out\\cli.js\" %* & calc.exe")]
    [InlineData("set ELECTRON_RUN_AS_NODE=1\r\nstart \"\" \"%~dp0..\\Code.exe\" %*")]
    [InlineData("")]
    public void Unrecognised_script_layout_is_refused_rather_than_guessed(string script)
    {
        var location = Locator(script).Locate();

        Assert.Null(location.Cli);
        Assert.Contains("not in the layout P3B recognises", location.Description);
    }

    [Fact]
    public void Paths_that_leave_the_VS_Code_folder_are_refused()
    {
        const string escaping = "set ELECTRON_RUN_AS_NODE=1\r\n\"%~dp0..\\..\\TEST-elsewhere\\Code.exe\" \"%~dp0..\\abc123\\resources\\app\\out\\cli.js\" %*";

        var location = Locator(escaping, existing: [@"C:\TEST\TEST-elsewhere\Code.exe", Cli]).Locate();

        Assert.Null(location.Cli);
    }

    [Fact]
    public void Named_files_must_exist()
    {
        var location = Locator(existing: [Exe]).Locate();

        Assert.Null(location.Cli);
        Assert.Contains("were not found", location.Description);
    }

    [Fact]
    public async Task Available_when_VS_Code_reports_its_version()
    {
        var runner = Runner().Script("--version", FakeProcessRunner.Exited(0, "1.144.1\r\nabcdef\r\nx64"));

        var availability = await Installer(runner).CheckAvailabilityAsync(CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Contains("1.144.1", availability.Description);
    }

    [Fact]
    public async Task Not_available_without_VS_Code_and_nothing_is_run()
    {
        var runner = Runner();

        var availability = await Installer(runner, Locator(found: null)).CheckAvailabilityAsync(CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("other.extension@9.9.9", InstalledStateKind.NotInstalled)]
    [InlineData("test-publisher.test-extension@1.2.3", InstalledStateKind.TargetVersionInstalled)]
    [InlineData("TEST-PUBLISHER.Test-Extension@1.2.3", InstalledStateKind.TargetVersionInstalled)]
    [InlineData("test-publisher.test-extension@1.2.4", InstalledStateKind.OtherVersionInstalled)]
    public async Task Installed_state_comes_from_list_extensions_show_versions(string output, InstalledStateKind expected)
    {
        var runner = Runner().Script("--list-extensions", FakeProcessRunner.Exited(0, output));

        var state = await Installer(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(expected, state.Kind);
        Assert.Equal(new[] { Cli, "--list-extensions", "--show-versions" }, Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task Failed_listing_is_unknown_never_assumed()
    {
        var runner = Runner().Script("--list-extensions", FakeProcessRunner.Exited(1, error: "TEST failure"));

        var state = await Installer(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
    }

    [Theory]
    [InlineData("test&publisher.x", "1.2.3")]
    [InlineData("--force", "1.2.3")]
    [InlineData("test-publisher.test-extension", "1.2.3 --force")]
    [InlineData("test-publisher.test-extension", "latest")]
    public async Task Unsafe_identifier_or_version_is_rejected_before_anything_runs(string id, string version)
    {
        var runner = Runner();
        var target = new InstallTarget(new ToolReference("TEST"), "Test", id, version);

        var attempt = await Installer(runner).InstallAsync(target, CancellationToken.None);
        var state = await Installer(runner).GetInstalledStateAsync(target, CancellationToken.None);

        Assert.Equal(InstallAttemptStatus.Rejected, attempt.Status);
        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Approved_install_runs_the_exact_pinned_command_verifies_and_records_an_automatic_uninstall()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, "other.extension@9.9.9"), FakeProcessRunner.Exited(0, Listed(Version)))
            .Script("--install-extension", FakeProcessRunner.Exited(0, "Extension installed."));

        var report = await Execute(runner);

        Assert.True(report.Succeeded, report.Message);
        var install = Assert.Single(runner.RequestsFor("--install-extension"));
        Assert.Equal(new[] { Cli, "--install-extension", $"{ExtensionId}@{Version}" }, install.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(22), install.Timeout);
        Assert.Equal(ReversalKind.Automatic, report.Reversal!.Kind);
        Assert.Equal(new VsCodeExtensionUninstallReversal(ExtensionId, Version), report.Reversal.Operation);
        Assert.Equal(new[] { "--version", "--list-extensions", "--install-extension", "--list-extensions" }, runner.Requests.Select(r => r.Arguments[1]));
    }

    [Fact]
    public async Task No_command_ever_forces_an_install()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, ""), FakeProcessRunner.Exited(0, Listed(Version)))
            .Script("--install-extension", FakeProcessRunner.Exited(0))
            .Script("--uninstall-extension", FakeProcessRunner.Exited(0));
        await Execute(runner);
        await new VsCodeExtensionUninstallHandler(Installer(runner)).ReverseAsync(new VsCodeExtensionUninstallReversal(ExtensionId, Version), CancellationToken.None);

        Assert.All(runner.Requests, r => Assert.DoesNotContain(r.Arguments, a => a.Contains("force", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("--force", VsCodeExtensionCommands.Install(ExtensionId, Version));
    }

    [Fact]
    public async Task Exit_code_zero_without_the_version_listed_is_not_success()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, ""))
            .Script("--install-extension", FakeProcessRunner.Exited(0, "Extension installed."));

        var report = await Execute(runner);

        Assert.False(report.Succeeded);
        Assert.Contains("could not verify", report.Message);
    }

    [Fact]
    public async Task Replacing_another_version_is_manual_only_reversal()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, Listed("0.0.1")), FakeProcessRunner.Exited(0, Listed(Version)))
            .Script("--install-extension", FakeProcessRunner.Exited(0));

        var report = await Execute(runner, AuditIssueState.UnapprovedVersion);

        Assert.True(report.Succeeded, report.Message);
        Assert.Equal(ReversalKind.ManualOnly, report.Reversal!.Kind);
    }

    [Fact]
    public async Task Already_installed_approved_version_changes_nothing()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, Listed(Version)));

        var report = await Execute(runner);

        Assert.True(report.Succeeded);
        Assert.Equal(ReversalKind.NothingToReverse, report.Reversal!.Kind);
        Assert.Empty(runner.RequestsFor("--install-extension"));
    }

    [Fact]
    public async Task Failed_install_keeps_the_exit_code()
    {
        var runner = Runner()
            .Script("--version", FakeProcessRunner.Exited(0, "1.144.1"))
            .Script("--list-extensions", FakeProcessRunner.Exited(0, ""))
            .Script("--install-extension", FakeProcessRunner.Exited(1, error: "TEST extension not found"));

        var report = await Execute(runner);

        Assert.False(report.Succeeded);
        Assert.Contains("TEST extension not found", report.Message);
    }

    [Fact]
    public async Task Reversal_uninstalls_the_recorded_extension_and_verifies_it_is_gone()
    {
        var runner = Runner()
            .Script("--list-extensions", FakeProcessRunner.Exited(0, Listed(Version)), FakeProcessRunner.Exited(0, "other.extension@9.9.9"))
            .Script("--uninstall-extension", FakeProcessRunner.Exited(0));

        var attempt = await new VsCodeExtensionUninstallHandler(Installer(runner))
            .ReverseAsync(new VsCodeExtensionUninstallReversal(ExtensionId, Version), CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(new[] { Cli, "--uninstall-extension", ExtensionId }, Assert.Single(runner.RequestsFor("--uninstall-extension")).Arguments);
    }

    [Fact]
    public async Task Reversal_refuses_when_another_version_is_now_installed()
    {
        var runner = Runner().Script("--list-extensions", FakeProcessRunner.Exited(0, Listed("2.0.0")));

        var attempt = await new VsCodeExtensionUninstallHandler(Installer(runner))
            .ReverseAsync(new VsCodeExtensionUninstallReversal(ExtensionId, Version), CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Conflict, attempt.Status);
        Assert.Empty(runner.RequestsFor("--uninstall-extension"));
    }

    [Fact]
    public async Task Reversal_that_leaves_the_extension_installed_is_failed()
    {
        var runner = Runner()
            .Script("--list-extensions", FakeProcessRunner.Exited(0, Listed(Version)))
            .Script("--uninstall-extension", FakeProcessRunner.Exited(1, error: "TEST in use"));

        var attempt = await new VsCodeExtensionUninstallHandler(Installer(runner))
            .ReverseAsync(new VsCodeExtensionUninstallReversal(ExtensionId, Version), CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Failed, attempt.Status);
    }

    private sealed class FakeCommandLocator(string? found) : ICommandLocator
    {
        public string? Locate(string command) => command == "code.cmd" ? found : null;
    }

    private sealed class FakeTextFileReader(string? script, string[] existing) : ITextFileReader
    {
        public bool Exists(string path) => existing.Contains(path, StringComparer.OrdinalIgnoreCase);

        public string? ReadAllText(string path) => path == Script ? script : null;
    }
}
