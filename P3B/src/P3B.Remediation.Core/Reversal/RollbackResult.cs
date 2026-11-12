namespace P3B.Remediation.Core.Reversal;

/// <summary>What reversing a journal entry would do; shown to the user before they confirm.</summary>
public sealed record RollbackPreview(
    Guid EntryId,
    string ToolId,
    ReversalKind Kind,
    string Method,
    IReadOnlyList<string> ChangesToReverse,
    ReversalEntryStatus Status);

public enum RollbackOutcome
{
    Reversed,
    NotConfirmed,
    NotFound,
    AlreadyClosed,
    ManualOnly,
    NothingToReverse,
    NoHandler,
    Conflict,
    Failed,
    Cancelled,
    Busy,
}

public sealed record RollbackResult(Guid EntryId, RollbackOutcome Outcome, string Message);
