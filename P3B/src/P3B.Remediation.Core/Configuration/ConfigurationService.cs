using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Configuration;

/// <summary>A proposed addition to the user PATH, for the preview and approval step.</summary>
public sealed record UserPathAdditionPlan(
    string Directory,
    bool AlreadyPresent,
    IReadOnlyList<string> ProposedChanges,
    string ReversalMethod);

/// <summary>
/// Supported user-level configuration changes. Currently one change that can be reliably
/// reversed: appending a directory to the current user's PATH.
/// </summary>
/// <remarks>
/// Not yet wired to the planner: the manifest and contract do not specify which configuration
/// changes are supported (see docs/BLOCKERS.md). Changes are user-level only, need an approval
/// of the exact preview, are verified, and record the prior value for reversal.
/// </remarks>
public sealed class ConfigurationService
{
    public const string PathVariable = "Path";

    private readonly IUserEnvironment _environment;

    public ConfigurationService(IUserEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <exception cref="ArgumentException">The directory is not an absolute path or contains unsafe characters.</exception>
    public UserPathAdditionPlan PlanUserPathAddition(string directory)
    {
        var normalized = NormalizeDirectory(directory);
        var present = Contains(_environment.GetUserVariable(PathVariable), normalized);

        return new UserPathAdditionPlan(
            normalized,
            present,
            present
                ? [$"No change: {normalized} is already in your user PATH."]
                : [$"Add {normalized} to the end of your user PATH (current user only; the system PATH is not changed)."],
            "P3B records your previous user PATH and can restore it, as long as nothing else has changed your user PATH in the meantime.");
    }

    public ExecutionReport ApplyUserPathAddition(ActionPreview preview, ApprovalDecision approval, UserPathAdditionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(plan);

        if (!approval.Approves(preview))
        {
            return ExecutionReport.Failure("The configuration change was not approved, so nothing was changed.");
        }

        var directory = NormalizeDirectory(plan.Directory);
        var prior = _environment.GetUserVariable(PathVariable);
        if (Contains(prior, directory))
        {
            return ExecutionReport.Success(
                $"{directory} is already in your user PATH. Nothing was changed.",
                prior,
                [],
                ReversalInfo.NothingToReverse(preview, prior));
        }

        var applied = string.IsNullOrEmpty(prior) ? directory : $"{prior.TrimEnd(';')};{directory}";
        _environment.SetUserVariable(PathVariable, applied);

        var change = $"Added {directory} to the user PATH.";
        var reversal = ReversalInfo.Automatic(
            preview,
            plan.ReversalMethod,
            prior,
            [change],
            new RestoreUserEnvironmentVariableReversal(PathVariable, prior, applied));

        if (!string.Equals(_environment.GetUserVariable(PathVariable), applied, StringComparison.Ordinal))
        {
            return ExecutionReport.Failure(
                "The user PATH was changed but could not be verified afterwards. Check it before relying on it.",
                prior,
                [change],
                reversal);
        }

        return ExecutionReport.Success(change, prior, [change], reversal);
    }

    private static string NormalizeDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var trimmed = directory.Trim();

        if (trimmed.IndexOfAny([';', '"', '%']) >= 0 || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("The directory must not contain ';', quotes, '%' or control characters.", nameof(directory));
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("The directory must be an absolute path.", nameof(directory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
    }

    private static bool Contains(string? pathValue, string directory) =>
        (pathValue ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(entry => string.Equals(Path.TrimEndingDirectorySeparator(entry), directory, StringComparison.OrdinalIgnoreCase));
}
