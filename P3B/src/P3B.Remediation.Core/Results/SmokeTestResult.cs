using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Core.Results;

/// <summary>
/// The structured verification report for one remediated tool (P3-StR2.2), kept distinct from
/// the <see cref="RemediationResult"/> (3B-UR-002). It never states that the environment is
/// ready: that is determined by P3A's re-audit.
/// </summary>
public sealed record SmokeTestResult
{
    public const string ReadinessDisclaimer =
        "These checks cover only the remediated tool. Overall environment readiness is determined by P3A's re-audit.";

    public required Guid PreviewId { get; init; }

    public required ToolReference Tool { get; init; }

    /// <summary>The approved version the checks verified against.</summary>
    public string? TargetVersion { get; init; }

    public required SmokeTestOutcome Outcome { get; init; }

    /// <summary>Summary, including why any check did not pass.</summary>
    public required string Message { get; init; }

    /// <summary>Each check that ran, in manifest order.</summary>
    public IReadOnlyList<SmokeCheckResult> Checks { get; init; } = [];

    public string ReadinessNote { get; init; } = ReadinessDisclaimer;

    public required DateTimeOffset CompletedAt { get; init; }
}
