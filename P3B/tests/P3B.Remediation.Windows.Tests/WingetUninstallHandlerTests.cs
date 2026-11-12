using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Windows.Tests.Fakes;
using P3B.Remediation.Windows.WinGet;

namespace P3B.Remediation.Windows.Tests;

/// <summary>Reversal of P3B WinGet installs against a scripted runner.</summary>
public class WingetUninstallHandlerTests
{
    private static readonly WingetUninstallReversal Operation =
        new(CapturedWingetOutput.InstalledPackageId, CapturedWingetOutput.InstalledVersion);

    private static WingetUninstallHandler Handler(FakeProcessRunner runner) => new(new WingetInstaller(runner));

    [Fact]
    public async Task Installed_recorded_version_is_uninstalled_and_verified()
    {
        var runner = new FakeProcessRunner()
            .Script(
                "list",
                FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled),
                FakeProcessRunner.Exited(WingetCommands.NoInstalledPackageFound, CapturedWingetOutput.ListAbsent))
            .Script("uninstall", FakeProcessRunner.Exited(0, "Successfully uninstalled"));

        var attempt = await Handler(runner).ReverseAsync(Operation, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Reversed, attempt.Status);
        Assert.Equal(
            WingetCommands.Uninstall(Operation.PackageId, Operation.Version),
            Assert.Single(runner.RequestsFor("uninstall")).Arguments);
    }

    [Fact]
    public async Task Uninstall_that_leaves_the_package_installed_is_a_failure()
    {
        var runner = new FakeProcessRunner()
            .Script("list", FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled))
            .Script("uninstall", FakeProcessRunner.Exited(0, "Successfully uninstalled"));

        var attempt = await Handler(runner).ReverseAsync(Operation, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Failed, attempt.Status);
    }

    [Fact]
    public async Task Different_version_now_installed_is_a_conflict_and_nothing_is_uninstalled()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.Exited(0, CapturedWingetOutput.ListInstalled));
        var olderRecord = Operation with { Version = "1.100.0" };

        var attempt = await Handler(runner).ReverseAsync(olderRecord, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Conflict, attempt.Status);
        Assert.Empty(runner.RequestsFor("uninstall"));
    }

    [Fact]
    public async Task Already_removed_package_counts_as_reversed_without_running_uninstall()
    {
        var runner = new FakeProcessRunner().Script(
            "list",
            FakeProcessRunner.Exited(WingetCommands.NoInstalledPackageFound, CapturedWingetOutput.ListAbsent));

        var attempt = await Handler(runner).ReverseAsync(Operation, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Reversed, attempt.Status);
        Assert.Empty(runner.RequestsFor("uninstall"));
    }

    [Fact]
    public async Task Unknown_state_changes_nothing()
    {
        var runner = new FakeProcessRunner().Script("list", FakeProcessRunner.TimedOut());

        var attempt = await Handler(runner).ReverseAsync(Operation, CancellationToken.None);

        Assert.Equal(ReversalAttemptStatus.Failed, attempt.Status);
        Assert.Empty(runner.RequestsFor("uninstall"));
    }
}
