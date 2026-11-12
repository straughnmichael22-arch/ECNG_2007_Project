namespace P3B.Remediation.Core.Results;

public enum SmokeTestOutcome
{
    /// <summary>Every declared check passed.</summary>
    Passed,

    /// <summary>At least one check failed.</summary>
    Failed,

    Cancelled,

    /// <summary>No check failed, but at least one did not finish in time.</summary>
    TimedOut,

    /// <summary>No check failed or timed out, but some (or all) could not be run, so the tool is not fully verified.</summary>
    Unsupported,
}
