namespace P3B.Remediation.Core.Integration;

/// <summary>
/// Runs P3A's environment audit on demand: the "re-audit" step of the joint
/// audit → fix → re-audit flow (P3-StR4.1, P3-AC4.1, SRS FR-004).
/// </summary>
/// <remarks>
/// P3B never decides readiness itself; that is P3A's responsibility. The only Ready / Not Ready label P3B ever
/// shows is <see cref="ReadinessAudit.Verdict"/>, which comes from P3A. The production
/// implementation is P3B.Integration.P3A.P3AAuditAdapter; tests use fakes.
/// </remarks>
public interface IReadinessAuditor
{
    Task<ReadinessAudit> RunAuditAsync(CancellationToken cancellationToken);
}

/// <summary>P3A's overall result, word for word.</summary>
public enum ReadinessVerdict
{
    /// <summary>P3A's audit reported "Overall Environment: Ready".</summary>
    Ready,

    /// <summary>P3A's audit reported "Overall Environment: Not Ready".</summary>
    NotReady,

    /// <summary>P3A's audit did not run, so there is no verdict.</summary>
    CouldNotRun,
}

/// <summary>One component as P3A's audit reported it.</summary>
public sealed record ReadinessComponent(
    string Name,
    string ReadinessStatus,
    string InstallationStatus,
    string Version,
    string? Reason);

/// <summary>The result of one run of P3A's audit, as received.</summary>
public sealed record ReadinessAudit
{
    public ReadinessAudit(
        ReadinessVerdict verdict,
        string summary,
        DateTimeOffset auditedAt,
        IEnumerable<AuditIssue> issues,
        IEnumerable<ReadinessComponent> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(components);
        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown verdict.");
        }

        Verdict = verdict;
        Summary = summary;
        AuditedAt = auditedAt;
        Issues = Array.AsReadOnly(issues.ToArray());
        Components = Array.AsReadOnly(components.ToArray());
    }

    public ReadinessVerdict Verdict { get; }

    /// <summary>What happened, in P3A's words where it gives them.</summary>
    public string Summary { get; }

    public DateTimeOffset AuditedAt { get; }

    /// <summary>Every component that did not pass, converted for P3B. Empty when Ready or CouldNotRun.</summary>
    public IReadOnlyList<AuditIssue> Issues { get; }

    public IReadOnlyList<ReadinessComponent> Components { get; }

    public bool HasVerdict => Verdict != ReadinessVerdict.CouldNotRun;

    public static ReadinessAudit CouldNotRun(string reason, DateTimeOffset at) =>
        new(ReadinessVerdict.CouldNotRun, reason, at, [], []);
}
