namespace P3B.Remediation.Core.Results;

public enum RemediationOutcome
{
    /// <summary>The action ran and reported success.</summary>
    Succeeded,

    /// <summary>The action ran (or tried to) and failed.</summary>
    Failed,

    /// <summary>The action was cancelled before or during execution.</summary>
    Cancelled,

    /// <summary>No supported automatic fix exists for the request (3B-UR-003). Nothing ran.</summary>
    Unsupported,

    /// <summary>The manifest or executor configuration is unusable (3B-IR-003). Nothing ran.</summary>
    InvalidConfiguration,

    /// <summary>The user did not approve the previewed action. Nothing ran.</summary>
    Rejected,

    /// <summary>Another action was already in progress (P3B-PR-01). Nothing ran.</summary>
    Busy,
}
