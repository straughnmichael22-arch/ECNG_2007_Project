using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Safety;

namespace P3B.Remediation.Core.Installation;

/// <summary>
/// Executes approved installation actions through an <see cref="IInstaller"/>, applying every
/// safety rule in one place:
/// <list type="bullet">
/// <item>nothing runs without an approval of this exact preview;</item>
/// <item>Vivado is never installed (3B-DC-004);</item>
/// <item>only the manifest's approved target version is installed (3B-DC-005);</item>
/// <item>the installer's availability is checked first;</item>
/// <item>the prior state must be known, so the change can be described and, where reliable, reversed;</item>
/// <item>success is reported only after the target version is verified as installed.</item>
/// </list>
/// </summary>
public sealed class InstallationService : IRemediationExecutor
{
    private readonly IReadOnlyDictionary<RemediationMethod, IInstaller> _installers;

    public InstallationService(IEnumerable<IInstaller> installers)
    {
        ArgumentNullException.ThrowIfNull(installers);

        var list = installers.ToArray();
        var duplicate = list.GroupBy(i => i.Method).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"More than one installer handles '{duplicate.Key}'.", nameof(installers));
        }

        _installers = list.ToDictionary(i => i.Method);
    }

    public bool CanHandle(RemediationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _installers.ContainsKey(action.Method) && !ProhibitedSoftwarePolicy.IsProhibited(action.Tool, out _);
    }

    public ActionPreview CreatePreview(RemediationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_installers.TryGetValue(action.Method, out var installer))
        {
            throw new InvalidOperationException($"No installer handles '{action.Method}'.");
        }

        var target = InstallTarget.From(action);
        var changes = installer.DescribeInstall(target)
            .Append($"Before reporting success, P3B checks that {target.ToolName} {target.Version} is actually installed.");

        // P3A reported another version as installed: replacing it cannot be undone automatically.
        var expectation = action.Request.State == AuditIssueState.UnapprovedVersion
            ? ReversalExpectation.PartiallyReversible
            : ReversalExpectation.Reversible;

        return new ActionPreview(
            action,
            changes,
            $"If {target.ToolName} was not installed before, P3B records how to uninstall exactly version {target.Version} so you can reverse it later. "
            + "If another version was already installed, replacing it cannot be reversed automatically, and P3B will say so.",
            expectation);
    }

    public async Task<ExecutionReport> ExecuteAsync(
        ActionPreview preview,
        ApprovalDecision approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(approval);

        if (!approval.Approves(preview))
        {
            return ExecutionReport.Failure("The installation was not approved, so nothing was changed.");
        }

        var action = preview.Action;
        if (ProhibitedSoftwarePolicy.IsProhibited(action.Tool, out var prohibited))
        {
            return ExecutionReport.Failure(prohibited);
        }

        if (!_installers.TryGetValue(action.Method, out var installer))
        {
            return ExecutionReport.Failure($"No installer is available for the '{action.Method}' method. Nothing was changed.");
        }

        var target = InstallTarget.From(action);
        if (!action.Tool.ApprovedVersions.Contains(target.Version, StringComparer.Ordinal))
        {
            return ExecutionReport.Failure($"Version {target.Version} is not an approved version of {target.ToolName}. Nothing was changed.");
        }

        var availability = await installer.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsAvailable)
        {
            return ExecutionReport.Failure($"{availability.Description} Nothing was changed.");
        }

        var prior = await installer.GetInstalledStateAsync(target, cancellationToken).ConfigureAwait(false);
        var priorText = $"{target.ToolName} was {prior.Describe()} ({prior.Evidence})";

        switch (prior.Kind)
        {
            case InstalledStateKind.Unknown:
                return ExecutionReport.Failure(
                    $"P3B could not determine whether {target.ToolName} is already installed, so it did not continue: {prior.Evidence}",
                    priorText);
            case InstalledStateKind.TargetVersionInstalled:
                return ExecutionReport.Success(
                    $"{target.ToolName} {target.Version} is already installed. Nothing was changed.",
                    priorText,
                    [],
                    ReversalInfo.NothingToReverse(preview, priorText));
        }

        InstallAttempt attempt;
        try
        {
            attempt = await installer.InstallAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var interrupted = await CheckAfterAsync(installer, target).ConfigureAwait(false);
            return ExecutionReport.Cancelled(
                $"The installation was cancelled and the installer was stopped. Afterwards, {target.ToolName} is {interrupted.Describe()}.",
                priorText,
                ObservedChanges(target, prior, interrupted),
                BuildReversal(preview, installer, target, prior, interrupted, priorText));
        }

        var post = await CheckAfterAsync(installer, target).ConfigureAwait(false);
        var observed = ObservedChanges(target, prior, post);
        var reversal = BuildReversal(preview, installer, target, prior, post, priorText);
        var verified = post.Kind == InstalledStateKind.TargetVersionInstalled;

        if (attempt.Status == InstallAttemptStatus.Completed && verified)
        {
            return ExecutionReport.Success(
                $"Installed {target.ToolName} {target.Version} and verified that it is installed.",
                priorText,
                observed,
                reversal);
        }

        if (attempt.Status == InstallAttemptStatus.Completed)
        {
            return ExecutionReport.Failure(
                $"The installer reported success, but P3B could not verify that {target.ToolName} {target.Version} is installed "
                + $"(found {post.Describe()}: {post.Evidence}). The installation is treated as failed.",
                priorText,
                observed,
                reversal);
        }

        return ExecutionReport.Failure(
            $"The installation did not complete ({attempt.Describe()}). Afterwards, {target.ToolName} is {post.Describe()}.",
            priorText,
            observed,
            reversal);
    }

    private static async Task<InstalledState> CheckAfterAsync(IInstaller installer, InstallTarget target)
    {
        // The check must run even if the user cancelled, so the real outcome is recorded.
        try
        {
            return await installer.GetInstalledStateAsync(target, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return InstalledState.Unknown($"The check after installation failed: {ex.Message}");
        }
    }

    private static bool Unchanged(InstalledState prior, InstalledState post) =>
        post.Kind != InstalledStateKind.Unknown
        && prior.Kind == post.Kind
        && string.Equals(prior.DetectedVersion, post.DetectedVersion, StringComparison.Ordinal);

    private static string[] ObservedChanges(InstallTarget target, InstalledState prior, InstalledState post) =>
        Unchanged(prior, post) ? [] : [$"{target.ToolName}: {prior.Describe()} -> {post.Describe()}"];

    private static ReversalInfo BuildReversal(
        ActionPreview preview,
        IInstaller installer,
        InstallTarget target,
        InstalledState prior,
        InstalledState post,
        string priorText)
    {
        if (Unchanged(prior, post))
        {
            return ReversalInfo.NothingToReverse(preview, priorText);
        }

        var changes = ObservedChanges(target, prior, post);

        if (prior.Kind == InstalledStateKind.NotInstalled && post.Kind == InstalledStateKind.TargetVersionInstalled)
        {
            return ReversalInfo.Automatic(
                preview,
                $"Uninstall {target.ToolName} {target.Version}, which P3B installed. P3B first checks that this exact version is still installed.",
                priorText,
                changes,
                installer.CreateUninstallReversal(target));
        }

        if (prior.Kind == InstalledStateKind.OtherVersionInstalled && post.Kind == InstalledStateKind.TargetVersionInstalled)
        {
            return ReversalInfo.ManualOnly(
                preview,
                $"{target.ToolName} {prior.DetectedVersion ?? "(unknown version)"} was replaced by the approved version {target.Version}. "
                + "P3B cannot restore the previous version automatically and never installs unapproved versions; reinstall it yourself if you need it.",
                priorText,
                changes);
        }

        return ReversalInfo.ManualOnly(
            preview,
            $"P3B could not reliably determine what the installer changed ({target.ToolName} is now {post.Describe()}). "
            + $"Check Installed apps in Windows Settings and remove or repair {target.ToolName} manually if needed.",
            priorText,
            changes);
    }
}
