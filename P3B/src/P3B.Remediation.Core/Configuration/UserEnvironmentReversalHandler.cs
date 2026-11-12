using P3B.Remediation.Core.Reversal;

namespace P3B.Remediation.Core.Configuration;

/// <summary>
/// Restores a user environment variable, but only if it still holds the value P3B applied;
/// otherwise something else has changed it and restoring would overwrite that change.
/// </summary>
public sealed class UserEnvironmentReversalHandler : IReversalHandler
{
    private readonly IUserEnvironment _environment;

    public UserEnvironmentReversalHandler(IUserEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public bool CanHandle(ReversalOperation operation) => operation is RestoreUserEnvironmentVariableReversal;

    public Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken)
    {
        if (operation is not RestoreUserEnvironmentVariableReversal restore)
        {
            throw new ArgumentException("Unsupported reversal operation.", nameof(operation));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var current = _environment.GetUserVariable(restore.Name);
        if (!string.Equals(current, restore.AppliedValue, StringComparison.Ordinal))
        {
            return Task.FromResult(new ReversalAttempt(
                ReversalAttemptStatus.Conflict,
                $"The user variable '{restore.Name}' has changed since P3B modified it, so it was not restored automatically. "
                + $"Its value before P3B's change was: {restore.PriorValue ?? "(not set)"}"));
        }

        _environment.SetUserVariable(restore.Name, restore.PriorValue);

        return Task.FromResult(string.Equals(_environment.GetUserVariable(restore.Name), restore.PriorValue, StringComparison.Ordinal)
            ? new ReversalAttempt(ReversalAttemptStatus.Reversed, $"Restored the user variable '{restore.Name}' to its previous value.")
            : new ReversalAttempt(ReversalAttemptStatus.Failed, $"The user variable '{restore.Name}' could not be verified after restoring it."));
    }
}
