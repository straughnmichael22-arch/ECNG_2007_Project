namespace P3B.Remediation.Core.Actions;

/// <summary>How reversible an action is expected to be, before it runs.</summary>
public enum ReversalExpectation
{
    /// <summary>P3B expects to be able to reverse the change automatically.</summary>
    Reversible,

    /// <summary>Part of the change may not be reversible automatically (for example, replacing an installed version).</summary>
    PartiallyReversible,

    /// <summary>The change cannot be reversed by P3B.</summary>
    NotReversible,
}
