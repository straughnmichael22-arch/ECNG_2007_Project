namespace P3B.Remediation.Core.Reversal;

public enum ReversalAttemptStatus
{
    /// <summary>The change was reversed and the result verified.</summary>
    Reversed,

    /// <summary>The system changed since P3B's change, so it was deliberately not reversed.</summary>
    Conflict,

    Failed,
}

public sealed record ReversalAttempt(ReversalAttemptStatus Status, string Message);

/// <summary>Performs one kind of <see cref="ReversalOperation"/>.</summary>
public interface IReversalHandler
{
    bool CanHandle(ReversalOperation operation);

    /// <summary>Must verify the result before reporting <see cref="ReversalAttemptStatus.Reversed"/>.</summary>
    Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken);
}
