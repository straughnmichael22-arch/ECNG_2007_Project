using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Windows.Tests.Fakes;
using P3B.Remediation.Windows.WinGet;

namespace P3B.Remediation.Windows.Tests;

/// <summary>WingetInstaller against a scripted runner: WinGet is never actually run.</summary>
public class WingetInstallerTests
{
    private const string PackageId = CapturedWingetOutput.InstalledPackageId;
    private const string Version = CapturedWingetOutput.InstalledVersion;

    private static readonly InstallTarget Target = new(new ToolReference("TEST-TOOL"), "Test tool", PackageId, Version);

    private static readonly WingetOptions Options = new(
        TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(22), TimeSpan.FromSeconds(33));

    [Fact]
    public async Task Available_when_winget_reports_its_version()
    {
        var runner = new FakeProcessRunner().Script("--version", FakeProcessRunner.Exited(0, "v1.29.380\r\n"));

        var availability = await new WingetInstaller(runner).CheckAvailabilityAsync(CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Contains("v1.29.380", availability.Description);
        Assert.Equal(WingetCommands.Executable, Assert.Single(runner.Requests).FileName);
    }

    [Fact]
    public async Task Not_available_when_winget_cannot_be_started()
    {
        var runner = new FakeProcessRunner().Script("--version", FakeProcessRunner.NotFound());

        var availability = await new WingetInstaller(runner).CheckAvailabilityAsync(CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("App Installer", availability.Description);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Not_available_when_winget_fails(int exitCode)
    {
        var runner = new FakeProcessRunner().Script("--version", FakeProcessRunner.Exited(exitCode));

        Assert.False((await new WingetInstaller(runner).CheckAvailabilityAsync(CancellationToken.None)).IsAvailable);
    }

    [Fact]
    public async Task Installed_target_version_is_recognised_from_real_output()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled));

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.TargetVersionInstalled, state.Kind);
        Assert.Equal(Version, state.DetectedVersion);
    }

    [Fact]
    public async Task Different_installed_version_is_reported_as_other_version()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled));
        var olderTarget = Target with { Version = "1.100.0" };

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(olderTarget, CancellationToken.None);

        Assert.Equal(InstalledStateKind.OtherVersionInstalled, state.Kind);
        Assert.Equal(Version, state.DetectedVersion);
    }

    [Fact]
    public async Task Absent_package_exit_code_means_not_installed()
    {
        var runner = new FakeProcessRunner().Script(
            "list",
            FakeProcessRunner.Exited(WingetCommands.NoInstalledPackageFound, CapturedWingetOutput.ListAbsent));

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.NotInstalled, state.Kind);
    }

    [Theory]
    [InlineData(WingetCommands.SourceAgreementsNotAccepted, "does not accept them on your behalf")]
    [InlineData(1, "0x00000001")]
    public async Task Unexpected_list_failure_is_unknown_not_assumed(int exitCode, string expected)
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.Exited(exitCode, CapturedWingetOutput.SourceAgreementsPrompt));

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
        Assert.Contains(expected, state.Evidence);
    }

    [Fact]
    public async Task Successful_list_without_the_package_row_is_unknown()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.Exited(0, "Name Id Version\n------\n"));

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
    }

    [Fact]
    public async Task List_timeout_is_unknown()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.TimedOut());

        var state = await new WingetInstaller(runner).GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
    }

    [Fact]
    public async Task Install_runs_the_exact_command_with_the_install_timeout()
    {
        var runner = new FakeProcessRunner().Script("install", FakeProcessRunner.Exited(0, "Successfully installed"));

        var attempt = await new WingetInstaller(runner, Options).InstallAsync(Target, CancellationToken.None);

        Assert.Equal(InstallAttemptStatus.Completed, attempt.Status);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(WingetCommands.Executable, request.FileName);
        Assert.Equal(WingetCommands.Install(PackageId, Version), request.Arguments);
        Assert.Equal(Options.InstallTimeout, request.Timeout);
    }

    [Fact]
    public async Task Install_failure_keeps_the_exit_code_and_a_readable_summary()
    {
        var output = "\r-\r\\\r  ██████████▒▒▒▒  50%\r\nInstaller failed with exit code: 1603\r\n";
        var runner = new FakeProcessRunner().Script("install", FakeProcessRunner.Exited(unchecked((int)0x8A150011), output));

        var attempt = await new WingetInstaller(runner).InstallAsync(Target, CancellationToken.None);

        Assert.Equal(InstallAttemptStatus.Failed, attempt.Status);
        Assert.Equal(unchecked((int)0x8A150011), attempt.ExitCode);
        Assert.Contains("1603", attempt.Details);
        Assert.DoesNotContain("█", attempt.Details);
    }

    [Fact]
    public async Task Install_timeout_is_reported_as_timed_out()
    {
        var runner = new FakeProcessRunner().Script("install", FakeProcessRunner.TimedOut("Downloading"));

        var attempt = await new WingetInstaller(runner).InstallAsync(Target, CancellationToken.None);

        Assert.Equal(InstallAttemptStatus.TimedOut, attempt.Status);
        Assert.Contains("did not finish in time", attempt.Details);
    }

    [Fact]
    public async Task Install_cancellation_propagates()
    {
        var runner = new FakeProcessRunner().ScriptHangUntilCancelled("install");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new WingetInstaller(runner).InstallAsync(Target, cts.Token));
    }

    [Fact]
    public async Task Unsafe_package_identifier_is_rejected_without_running_anything()
    {
        var runner = new FakeProcessRunner();
        var unsafeTarget = Target with { Source = "--force" };

        var attempt = await new WingetInstaller(runner).InstallAsync(unsafeTarget, CancellationToken.None);
        var state = await new WingetInstaller(runner).GetInstalledStateAsync(unsafeTarget, CancellationToken.None);

        Assert.Equal(InstallAttemptStatus.Rejected, attempt.Status);
        Assert.Equal(InstalledStateKind.Unknown, state.Kind);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Description_and_reversal_name_the_exact_package_and_version()
    {
        var installer = new WingetInstaller(new FakeProcessRunner(), Options);

        Assert.Contains(installer.DescribeInstall(Target), line => line.Contains(PackageId, StringComparison.Ordinal) && line.Contains(Version, StringComparison.Ordinal));
        Assert.Contains(installer.DescribeInstall(Target), line => line.Contains("current user only", StringComparison.Ordinal));
        Assert.Equal(new WingetUninstallReversal(PackageId, Version), installer.CreateUninstallReversal(Target));
    }

    [Fact]
    public async Task Full_install_through_installation_service_with_scripted_winget()
    {
        var runner = new FakeProcessRunner()
            .Script("--version", FakeProcessRunner.Exited(0, "v1.29.380"))
            .Script(
                "list",
                FakeProcessRunner.Exited(WingetCommands.NoInstalledPackageFound, CapturedWingetOutput.ListAbsent),
                FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled))
            .Script("install", FakeProcessRunner.Exited(0, "Successfully installed"));
        var service = new InstallationService([new WingetInstaller(runner)]);
        var action = TestActions.For(PackageId, Version);
        var preview = service.CreatePreview(action);

        var report = await service.ExecuteAsync(preview, Core.Actions.ApprovalDecision.Approve(preview), CancellationToken.None);

        Assert.True(report.Succeeded, report.Message);
        Assert.Equal(ReversalKind.Automatic, report.Reversal?.Kind);
        Assert.Equal(["--version", "list", "install", "list"], runner.Requests.Select(r => r.Arguments[0]));
    }

    [Fact]
    public async Task Installer_exit_zero_without_verification_is_not_success()
    {
        var runner = new FakeProcessRunner()
            .Script("--version", FakeProcessRunner.Exited(0, "v1.29.380"))
            .Script("list", FakeProcessRunner.Exited(WingetCommands.NoInstalledPackageFound, CapturedWingetOutput.ListAbsent))
            .Script("install", FakeProcessRunner.Exited(0, "Successfully installed"));
        var service = new InstallationService([new WingetInstaller(runner)]);
        var preview = service.CreatePreview(TestActions.For(PackageId, Version));

        var report = await service.ExecuteAsync(preview, Core.Actions.ApprovalDecision.Approve(preview), CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Contains("could not verify", report.Message);
    }

    [Fact]
    public void Summary_without_output_says_so()
    {
        Assert.Equal("(no output)", WingetInstaller.Summarise(FakeProcessRunner.Exited(0)));
    }

    [Fact]
    public async Task Requests_never_use_a_shell()
    {
        var runner = new FakeProcessRunner()
            .Script("--version", FakeProcessRunner.Exited(0, "v1"))
            .Script("list", FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled));
        var installer = new WingetInstaller(runner);

        await installer.CheckAvailabilityAsync(CancellationToken.None);
        await installer.GetInstalledStateAsync(Target, CancellationToken.None);

        Assert.All(runner.Requests, r => Assert.Equal(WingetCommands.Executable, r.FileName));
        Assert.DoesNotContain(runner.Requests, r => r.FileName is "cmd" or "cmd.exe" or "powershell" or "powershell.exe");
    }

    private static class TestActions
    {
        public static Core.Actions.RemediationAction For(string packageId, string version)
        {
            var tool = new Core.Manifest.ManifestTool(
                new ToolReference("TEST-TOOL"),
                "Test tool",
                [version],
                new Core.Manifest.ManifestRemediation(Core.Manifest.RemediationMethod.WinGet, packageId));
            var request = new Core.Actions.RemediationRequest(new ToolReference("TEST-TOOL"), new IssueReference("TEST-ISSUE"), AuditIssueState.Missing);
            return new Core.Actions.RemediationAction(request, tool);
        }
    }
}
