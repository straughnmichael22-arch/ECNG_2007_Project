using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Windows.Processes;
using P3B.Remediation.Windows.Tests.Fakes;
using P3B.Remediation.Windows.Verification;

namespace P3B.Remediation.Windows.Tests;

/// <summary>
/// Windows smoke-check pieces. The version-command handler is tested with a scripted runner;
/// the locator searches only temporary folders; one end-to-end test launches the harmless
/// P3B.TestProcess helper. No real tool is launched and the real PATH is never searched.
/// </summary>
public class SmokeCheckTests
{
    private const string Version = "1.2.3";

    private static RemediationAction Action(params SmokeCheckDefinition[] checks)
    {
        var tool = new ManifestTool(
            new ToolReference("TEST-TOOL"),
            "Test tool",
            [Version],
            new ManifestRemediation(RemediationMethod.WinGet, "TEST.Package"),
            checks);
        var request = new RemediationRequest(new ToolReference("TEST-TOOL"), new IssueReference("TEST-ISSUE"), AuditIssueState.Missing);
        return new RemediationAction(request, tool);
    }

    private static VersionCommandCheck VersionCheck(string command = "TEST-tool") =>
        new(command, ["--version"], TimeSpan.FromSeconds(5));

    private static (VersionCommandCheckHandler Handler, FakeProcessRunner Runner) Handler(string? locatedPath = @"C:\TEST\bin\TEST-tool.exe")
    {
        var runner = new FakeProcessRunner();
        return (new VersionCommandCheckHandler(new FixedLocator(locatedPath), runner), runner);
    }

    [Fact]
    public async Task Version_command_passes_when_the_tool_reports_the_approved_version()
    {
        var (handler, runner) = Handler();
        runner.Script("--version", FakeProcessRunner.Exited(0, "TEST tool v1.2.3 (build 9)\r\n"));

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Passed, result.Status);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(@"C:\TEST\bin\TEST-tool.exe", request.FileName);
        Assert.Equal(new[] { "--version" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }

    [Fact]
    public async Task Version_command_fails_when_a_different_version_is_reported()
    {
        var (handler, runner) = Handler();
        runner.Script("--version", FakeProcessRunner.Exited(0, "TEST tool 1.2.30"));

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
        Assert.Contains("does not show the approved version 1.2.3", result.Explanation);
        Assert.Contains("TEST tool 1.2.30", result.Explanation);
    }

    [Fact]
    public async Task Exit_code_zero_alone_is_not_success()
    {
        var (handler, runner) = Handler();
        runner.Script("--version", FakeProcessRunner.Exited(0, string.Empty));

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Version_command_fails_on_a_non_zero_exit_code_even_if_the_version_is_printed()
    {
        var (handler, runner) = Handler();
        runner.Script("--version", FakeProcessRunner.Exited(2, "TEST tool 1.2.3", "TEST licence error"));

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
        Assert.Contains("exited with code 2", result.Explanation);
    }

    [Fact]
    public async Task Version_command_timeout_is_timed_out()
    {
        var (handler, runner) = Handler();
        runner.Script("--version", FakeProcessRunner.TimedOut());

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.TimedOut, result.Status);
        Assert.Contains("did not respond within 5 seconds", result.Explanation);
    }

    [Fact]
    public async Task Version_command_that_cannot_be_found_or_started_fails_with_a_reason()
    {
        var (missingHandler, missingRunner) = Handler(locatedPath: null);
        var notFound = await missingHandler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        var (brokenHandler, brokenRunner) = Handler();
        brokenRunner.Script("--version", FakeProcessRunner.NotFound());
        var notStarted = await brokenHandler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, notFound.Status);
        Assert.Contains("could not be found", notFound.Explanation);
        Assert.Empty(missingRunner.Requests);
        Assert.Equal(SmokeCheckStatus.Failed, notStarted.Status);
        Assert.Contains("could not be started", notStarted.Explanation);
    }

    [Theory]
    [InlineData(@"C:\TEST\bin\TEST-tool.cmd")]
    [InlineData(@"C:\TEST\bin\TEST-tool.bat")]
    public async Task Scripts_are_never_launched(string locatedPath)
    {
        var (handler, runner) = Handler(locatedPath);

        var result = await handler.RunAsync(VersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Unsupported, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Locator_finds_commands_only_in_absolute_search_folders()
    {
        using var temp = new TempFolder();
        File.WriteAllBytes(Path.Combine(temp.Root, "TEST-tool.exe"), []);
        var locator = new WindowsCommandLocator(() => ["relative-folder", temp.Root], [".COM", ".EXE"]);

        // Windows paths are case-insensitive; the extension comes from PATHEXT (".EXE").
        Assert.Equal(Path.Combine(temp.Root, "TEST-tool.exe"), locator.Locate("TEST-tool"), ignoreCase: true);
        Assert.Equal(Path.Combine(temp.Root, "TEST-tool.exe"), locator.Locate("TEST-tool.exe"), ignoreCase: true);
        Assert.Null(locator.Locate("TEST-absent"));
        Assert.Null(locator.Locate(@"subfolder\TEST-tool.exe"));
        Assert.Null(locator.Locate(@"..\TEST-tool.exe"));
    }

    [Fact]
    public void Locator_accepts_an_existing_absolute_path()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Root, "TEST-tool.exe");
        File.WriteAllBytes(path, []);
        var locator = new WindowsCommandLocator(() => [], [".EXE"]);

        Assert.Equal(path, locator.Locate(path));
        Assert.Null(locator.Locate(Path.Combine(temp.Root, "absent.exe")));
    }

    [Fact]
    public void File_probe_reports_existing_files_and_expands_variables()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Root, "TEST-file.txt");
        File.WriteAllText(path, "TEST");
        var probe = new WindowsFileProbe();

        Assert.True(probe.FileExists(path));
        Assert.False(probe.FileExists(Path.Combine(temp.Root, "absent.txt")));
        Assert.DoesNotContain("%TEMP%", probe.ExpandEnvironmentVariables(@"%TEMP%\x"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TEST tool 1.2.3", SmokeTestOutcome.Passed)]
    [InlineData("TEST tool 9.9.9", SmokeTestOutcome.Failed)]
    public async Task End_to_end_smoke_test_launches_a_real_harmless_process(string printed, SmokeTestOutcome expected)
    {
        var helper = TestProcessHelper.ExecutablePath;
        var check = new VersionCommandCheck(helper, ["print", printed], TimeSpan.FromSeconds(30));
        var service = new SmokeTestService(
            [new VersionCommandCheckHandler(new WindowsCommandLocator(() => [], [".EXE"]), new ProcessRunner())]);
        var preview = new ActionPreview(Action(check), ["TEST change"], "TEST reversal");

        var result = await service.RunAsync(preview, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Contains(printed, Assert.Single(result.Checks).Explanation);
    }

    private sealed class FixedLocator(string? path) : ICommandLocator
    {
        public string? Locate(string command) => path;
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder() =>
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "p3b-tests", Guid.NewGuid().ToString("N"))).FullName;

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
