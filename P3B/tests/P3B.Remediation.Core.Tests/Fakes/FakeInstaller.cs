using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>
/// Scripted installer double. It never installs anything: it returns the configured states and
/// attempt, and records what it was asked to do.
/// </summary>
internal sealed class FakeInstaller : IInstaller
{
    private int _stateCalls;
    private int _installCalls;

    public RemediationMethod Method { get; init; } = RemediationMethod.WinGet;

    public bool Available { get; init; } = true;

    /// <summary>States returned by successive state queries; the last one repeats.</summary>
    public IReadOnlyList<InstalledState> States { get; init; } =
    [
        InstalledState.NotInstalled("TEST not installed"),
        InstalledState.TargetVersion(TestManifests.TargetVersion, "TEST installed"),
    ];

    public InstallAttempt Attempt { get; init; } = new(InstallAttemptStatus.Completed, 0, "TEST installer finished");

    public bool ThrowCancellationDuringInstall { get; init; }

    public int StateCalls => Volatile.Read(ref _stateCalls);

    public int InstallCalls => Volatile.Read(ref _installCalls);

    public InstallTarget? LastInstallTarget { get; private set; }

    public Task<InstallerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Available
            ? new InstallerAvailability(true, "TEST installer available.")
            : new InstallerAvailability(false, "TEST installer is not available."));

    public Task<InstalledState> GetInstalledStateAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _stateCalls) - 1;
        return Task.FromResult(States[Math.Min(call, States.Count - 1)]);
    }

    public Task<InstallAttempt> InstallAsync(InstallTarget target, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _installCalls);
        LastInstallTarget = target;

        if (ThrowCancellationDuringInstall)
        {
            throw new OperationCanceledException("TEST cancellation during install");
        }

        return Task.FromResult(Attempt);
    }

    public IReadOnlyList<string> DescribeInstall(InstallTarget target) =>
        [$"TEST: install {target.Source} version {target.Version} (simulated)"];

    public ReversalOperation CreateUninstallReversal(InstallTarget target) =>
        new WingetUninstallReversal(target.Source, target.Version);
}
