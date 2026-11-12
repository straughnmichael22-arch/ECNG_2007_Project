using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Configuration;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Verification;

/// <summary>Asks the tool's installer (for example WinGet) which version is installed.</summary>
public sealed class InstalledVersionCheckHandler : ISmokeCheckHandler
{
    private readonly IReadOnlyDictionary<RemediationMethod, IInstaller> _installers;

    public InstalledVersionCheckHandler(IEnumerable<IInstaller> installers)
    {
        ArgumentNullException.ThrowIfNull(installers);
        _installers = installers.ToDictionary(i => i.Method);
    }

    public bool CanHandle(SmokeCheckDefinition check) => check is InstalledVersionCheck;

    public async Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_installers.TryGetValue(action.Method, out var installer))
        {
            return SmokeCheckResult.For(check, SmokeCheckStatus.Unsupported, $"No installer for the '{action.Method}' method can report installed versions.");
        }

        var state = await installer.GetInstalledStateAsync(InstallTarget.From(action), cancellationToken).ConfigureAwait(false);
        return state.Kind switch
        {
            InstalledStateKind.TargetVersionInstalled => SmokeCheckResult.For(
                check, SmokeCheckStatus.Passed, $"Version {action.TargetVersion} is installed ({state.Evidence})."),
            InstalledStateKind.OtherVersionInstalled => SmokeCheckResult.For(
                check, SmokeCheckStatus.Failed, $"Version {state.DetectedVersion ?? "(unknown)"} is installed, but the approved version is {action.TargetVersion}."),
            InstalledStateKind.NotInstalled => SmokeCheckResult.For(
                check, SmokeCheckStatus.Failed, $"{action.Tool.Name} is not installed ({state.Evidence})."),
            _ => SmokeCheckResult.For(
                check, SmokeCheckStatus.Failed, $"The installed version could not be confirmed: {state.Evidence}"),
        };
    }
}

/// <summary>Checks that an expected file, such as the executable, exists.</summary>
public sealed class FileExistsCheckHandler : ISmokeCheckHandler
{
    private readonly IFileProbe _files;

    public FileExistsCheckHandler(IFileProbe files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    public bool CanHandle(SmokeCheckDefinition check) => check is FileExistsCheck;

    public Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        var file = (FileExistsCheck)check;
        cancellationToken.ThrowIfCancellationRequested();

        var expanded = _files.ExpandEnvironmentVariables(file.Path);
        SmokeCheckResult result;
        if (expanded.Contains('%'))
        {
            result = SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"The path '{file.Path}' uses an environment variable that is not defined on this computer.");
        }
        else if (!Path.IsPathFullyQualified(expanded))
        {
            result = SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"'{expanded}' is not an absolute path, so it was not checked.");
        }
        else
        {
            result = _files.FileExists(expanded)
                ? SmokeCheckResult.For(check, SmokeCheckStatus.Passed, $"Found {expanded}.")
                : SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"{expanded} does not exist.");
        }

        return Task.FromResult(result);
    }
}

/// <summary>Checks that a command can be found the way a new terminal would find it.</summary>
public sealed class CommandOnPathCheckHandler : ISmokeCheckHandler
{
    private readonly ICommandLocator _locator;

    public CommandOnPathCheckHandler(ICommandLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    public bool CanHandle(SmokeCheckDefinition check) => check is CommandOnPathCheck;

    public Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        var command = (CommandOnPathCheck)check;
        cancellationToken.ThrowIfCancellationRequested();

        var located = _locator.Locate(command.Command);
        return Task.FromResult(located is null
            ? SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"'{command.Command}' was not found in any folder on the user or system PATH.")
            : SmokeCheckResult.For(check, SmokeCheckStatus.Passed, $"Found at {located}."));
    }
}

/// <summary>Checks that a user-level environment variable contains the expected text.</summary>
public sealed class UserEnvironmentVariableCheckHandler : ISmokeCheckHandler
{
    private readonly IUserEnvironment _environment;

    public UserEnvironmentVariableCheckHandler(IUserEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public bool CanHandle(SmokeCheckDefinition check) => check is UserEnvironmentVariableCheck;

    public Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        var variable = (UserEnvironmentVariableCheck)check;
        cancellationToken.ThrowIfCancellationRequested();

        var value = _environment.GetUserVariable(variable.Name);
        SmokeCheckResult result;
        if (value is null)
        {
            result = SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"The user variable '{variable.Name}' is not set.");
        }
        else if (value.Contains(variable.ExpectedContent, StringComparison.OrdinalIgnoreCase))
        {
            result = SmokeCheckResult.For(check, SmokeCheckStatus.Passed, $"The user variable '{variable.Name}' contains '{variable.ExpectedContent}'.");
        }
        else
        {
            result = SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"The user variable '{variable.Name}' does not contain '{variable.ExpectedContent}'.");
        }

        return Task.FromResult(result);
    }
}
