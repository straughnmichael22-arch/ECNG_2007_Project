using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Installation;

/// <summary>
/// One installation mechanism (for example WinGet). Implementations live outside the core and
/// only run tools; every safety decision is made by <see cref="InstallationService"/>.
/// </summary>
public interface IInstaller
{
    RemediationMethod Method { get; }

    Task<InstallerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    /// <summary>Must return <see cref="InstalledStateKind.Unknown"/> rather than guess.</summary>
    Task<InstalledState> GetInstalledStateAsync(InstallTarget target, CancellationToken cancellationToken);

    /// <summary>Installs exactly <see cref="InstallTarget.Version"/>. Throws <see cref="OperationCanceledException"/> after stopping the installer if cancelled.</summary>
    Task<InstallAttempt> InstallAsync(InstallTarget target, CancellationToken cancellationToken);

    /// <summary>User-facing description of exactly what <see cref="InstallAsync"/> will do.</summary>
    IReadOnlyList<string> DescribeInstall(InstallTarget target);

    /// <summary>The operation that removes exactly what <see cref="InstallAsync"/> added.</summary>
    ReversalOperation CreateUninstallReversal(InstallTarget target);
}
