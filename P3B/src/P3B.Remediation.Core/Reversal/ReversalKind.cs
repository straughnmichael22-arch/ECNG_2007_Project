namespace P3B.Remediation.Core.Reversal;

public enum ReversalKind
{
    /// <summary>P3B can reverse the change itself, using the recorded <see cref="ReversalOperation"/>.</summary>
    Automatic,

    /// <summary>The change cannot be reliably reversed by P3B; the method explains what the user can do.</summary>
    ManualOnly,

    /// <summary>Nothing was changed, so there is nothing to reverse.</summary>
    NothingToReverse,
}
