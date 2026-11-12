using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Windows.WinGet;

/// <summary>
/// Reverses a P3B WinGet installation by uninstalling exactly the recorded package and version.
/// It refuses if a different version is now installed, and verifies the package is gone before
/// reporting success.
/// </summary>
public sealed class WingetUninstallHandler : IReversalHandler
{
    private readonly WingetInstaller _winget;

    public WingetUninstallHandler(WingetInstaller winget)
    {
        ArgumentNullException.ThrowIfNull(winget);
        _winget = winget;
    }

    public bool CanHandle(ReversalOperation operation) => operation is WingetUninstallReversal;

    public async Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken)
    {
        if (operation is not WingetUninstallReversal uninstall)
        {
            throw new ArgumentException("Unsupported reversal operation.", nameof(operation));
        }

        var (id, version) = (uninstall.PackageId, uninstall.Version);
        var before = await _winget.GetPackageStateAsync(id, version, cancellationToken).ConfigureAwait(false);

        switch (before.Kind)
        {
            case InstalledStateKind.NotInstalled:
                return new ReversalAttempt(ReversalAttemptStatus.Reversed, $"{id} is no longer installed, so nothing needed to be removed.");
            case InstalledStateKind.OtherVersionInstalled:
                return new ReversalAttempt(
                    ReversalAttemptStatus.Conflict,
                    $"{id} is now at version {before.DetectedVersion ?? "(unknown)"}, not {version} as installed by P3B, so it was not uninstalled.");
            case InstalledStateKind.Unknown:
                return new ReversalAttempt(ReversalAttemptStatus.Failed, $"P3B could not confirm what is installed, so nothing was changed: {before.Evidence}");
        }

        var attempt = await _winget.UninstallPackageAsync(id, version, cancellationToken).ConfigureAwait(false);
        var after = await _winget.GetPackageStateAsync(id, version, CancellationToken.None).ConfigureAwait(false);

        return after.Kind == InstalledStateKind.NotInstalled
            ? new ReversalAttempt(ReversalAttemptStatus.Reversed, $"Uninstalled {id} {version} and verified that it is no longer installed.")
            : new ReversalAttempt(
                ReversalAttemptStatus.Failed,
                $"The uninstallation did not complete ({attempt.Describe()}). Afterwards, {id} is {after.Describe()}.");
    }
}
