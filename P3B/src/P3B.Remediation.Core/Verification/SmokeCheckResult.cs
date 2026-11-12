using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.Core.Verification;

public enum SmokeCheckStatus
{
    Passed,
    Failed,

    /// <summary>The check did not finish in time; nothing is concluded from it.</summary>
    TimedOut,

    /// <summary>The check could not be run here (unknown kind, no handler, or not applicable).</summary>
    Unsupported,
}

/// <summary>The result of one smoke check, with an explanation whenever it did not pass.</summary>
public sealed record SmokeCheckResult(string Kind, string Description, SmokeCheckStatus Status, string Explanation)
{
    public static SmokeCheckResult For(SmokeCheckDefinition check, SmokeCheckStatus status, string explanation)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new SmokeCheckResult(check.Kind, check.Describe(), status, explanation);
    }
}
