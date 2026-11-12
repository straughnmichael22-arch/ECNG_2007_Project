using EDAEnvironmentManager;
using EDAEnvironmentManager.Audit;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Integration.P3A;

/// <summary>Runs P3A's audit. The seam that lets tests replace the real audit with a fake.</summary>
public interface IP3AAuditEngine
{
    AuditReport Audit(EdaManifest manifest);
}

/// <summary>P3A's own <see cref="EnvironmentAuditor"/>: read-only detection, nothing is changed.</summary>
public sealed class P3AAuditEngine : IP3AAuditEngine
{
    private readonly EnvironmentAuditor _auditor = new();

    public AuditReport Audit(EdaManifest manifest) => _auditor.Audit(manifest);
}

/// <summary>
/// The production P3A adapter: runs P3A's audit against the manifest the dashboard has open
/// and converts the result with <see cref="P3AFindingMapper"/>. Supplies both the initial
/// audit (<see cref="IAuditStateSource"/>) and every re-audit (<see cref="IReadinessAuditor"/>).
/// </summary>
public sealed class P3AAuditAdapter : IAuditStateSource, IReadinessAuditor
{
    private readonly Func<string?> _manifestPath;
    private readonly IP3AAuditEngine _engine;
    private readonly TimeProvider _time;

    /// <param name="manifestPath">The manifest file to audit against, read each time; null when none is open.</param>
    public P3AAuditAdapter(Func<string?> manifestPath, IP3AAuditEngine? engine = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        _manifestPath = manifestPath;
        _engine = engine ?? new P3AAuditEngine();
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReadinessAudit> RunAuditAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = _manifestPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return CouldNotRun("No manifest is open, so P3A's audit has nothing to check against.");
        }

        var manifest = EdaManifestLoader.TryLoad(path, out var status);
        if (manifest is null)
        {
            return CouldNotRun($"P3A could not load the manifest at {path}: {status}");
        }

        // P3A's loader accepts any JSON object, so check the format before auditing against it.
        if (manifest.ManifestVersion != P3AManifestSchema.SupportedVersion)
        {
            return CouldNotRun(
                $"The open manifest is not in P3A's format (manifestVersion \"{P3AManifestSchema.SupportedVersion}\"), so P3A's audit cannot use it.");
        }

        AuditReport report;
        try
        {
            // P3A's audit is synchronous and can take several seconds (where.exe, version commands).
            report = await Task.Run(() => _engine.Audit(manifest), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CouldNotRun($"P3A's audit stopped with an error: {ex.Message}");
        }

        return P3AFindingMapper.ToReadinessAudit(report);
    }

    public async Task<IReadOnlyList<AuditIssue>> GetAuditIssuesAsync(CancellationToken cancellationToken)
    {
        var audit = await RunAuditAsync(cancellationToken).ConfigureAwait(false);
        return audit.HasVerdict ? audit.Issues : throw new InvalidOperationException(audit.Summary);
    }

    private ReadinessAudit CouldNotRun(string reason) => ReadinessAudit.CouldNotRun(reason, _time.GetLocalNow());
}
