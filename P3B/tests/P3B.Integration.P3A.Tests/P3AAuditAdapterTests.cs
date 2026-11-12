using EDAEnvironmentManager;
using EDAEnvironmentManager.Audit;
using P3B.Remediation.Core.Integration;
using static P3B.Integration.P3A.Tests.P3AFindingMapperTests;

namespace P3B.Integration.P3A.Tests;

/// <summary>
/// The production adapter, with a fake in place of P3A's audit engine: nothing on this computer
/// is detected, run or changed. P3A's real manifest file is only read.
/// </summary>
public class P3AAuditAdapterTests
{
    private static readonly string RealManifest = Path.Combine(AppContext.BaseDirectory, EdaManifestLoader.FileName);

    [Fact]
    public async Task Audit_runs_against_the_open_manifest_and_returns_P3A_verdict_and_issues()
    {
        var engine = new FakeP3AAuditEngine(Component(AuditFinding.NotInstalled, "surfer"), Component(AuditFinding.None, "ghdl"));
        var adapter = new P3AAuditAdapter(() => RealManifest, engine);

        var audit = await adapter.RunAuditAsync(CancellationToken.None);

        Assert.Equal(ReadinessVerdict.NotReady, audit.Verdict);
        Assert.Equal("surfer/NotInstalled", Assert.Single(audit.Issues).IssueId);
        Assert.Equal(1, engine.Calls);
        Assert.Equal("1.0", engine.LastManifest!.ManifestVersion);
        Assert.Contains(engine.LastManifest.Tools, t => t.Type == "ghdl");
    }

    [Fact]
    public async Task Ready_verdict_comes_only_from_P3A()
    {
        var adapter = new P3AAuditAdapter(() => RealManifest, new FakeP3AAuditEngine(Component(AuditFinding.None)));

        var audit = await adapter.RunAuditAsync(CancellationToken.None);

        Assert.Equal(ReadinessVerdict.Ready, audit.Verdict);
        Assert.Empty(await adapter.GetAuditIssuesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task No_open_manifest_means_the_audit_could_not_run()
    {
        var engine = new FakeP3AAuditEngine();
        var adapter = new P3AAuditAdapter(() => null, engine);

        var audit = await adapter.RunAuditAsync(CancellationToken.None);

        Assert.Equal(ReadinessVerdict.CouldNotRun, audit.Verdict);
        Assert.False(audit.HasVerdict);
        Assert.Equal(0, engine.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetAuditIssuesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_manifest_file_is_reported_in_P3A_words()
    {
        var engine = new FakeP3AAuditEngine();
        var adapter = new P3AAuditAdapter(() => Path.Combine(Path.GetTempPath(), "p3b-tests", "TEST-missing.json"), engine);

        var audit = await adapter.RunAuditAsync(CancellationToken.None);

        Assert.Equal(ReadinessVerdict.CouldNotRun, audit.Verdict);
        Assert.Contains("Manifest Not Found.", audit.Summary);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Manifest_in_another_format_is_not_audited()
    {
        var path = Path.Combine(Path.GetTempPath(), "p3b-tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "manifestVersion": "p3b-provisional-0", "tools": [] }""");
        try
        {
            var engine = new FakeP3AAuditEngine();
            var audit = await new P3AAuditAdapter(() => path, engine).RunAuditAsync(CancellationToken.None);

            Assert.Equal(ReadinessVerdict.CouldNotRun, audit.Verdict);
            Assert.Contains("not in P3A's format", audit.Summary);
            Assert.Equal(0, engine.Calls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task An_error_inside_P3A_audit_is_reported_not_thrown()
    {
        var adapter = new P3AAuditAdapter(() => RealManifest, new FakeP3AAuditEngine { Throw = true });

        var audit = await adapter.RunAuditAsync(CancellationToken.None);

        Assert.Equal(ReadinessVerdict.CouldNotRun, audit.Verdict);
        Assert.Contains("TEST engine failure", audit.Summary);
    }

    [Fact]
    public void Every_P3A_audit_engine_in_these_tests_is_a_fake()
    {
        var engines = typeof(P3AAuditAdapterTests).Assembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(IP3AAuditEngine).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(engines);
        Assert.All(engines, t => Assert.StartsWith("Fake", t.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Test_suite_does_not_reference_process_registry_or_network_apis()
    {
        var referenced = typeof(P3AAuditAdapterTests).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("System.Diagnostics.Process", referenced);
        Assert.DoesNotContain("Microsoft.Win32.Registry", referenced);
        Assert.DoesNotContain("System.Net.Http", referenced);
    }
}

/// <summary>Stands in for P3A's EnvironmentAuditor; detects and runs nothing.</summary>
internal sealed class FakeP3AAuditEngine : IP3AAuditEngine
{
    private readonly ComponentAuditResult[] _components;

    public FakeP3AAuditEngine(params ComponentAuditResult[] components) => _components = components;

    public bool Throw { get; init; }

    public int Calls { get; private set; }

    public EdaManifest? LastManifest { get; private set; }

    public AuditReport Audit(EdaManifest manifest)
    {
        Calls++;
        LastManifest = manifest;
        if (Throw)
        {
            throw new InvalidOperationException("TEST engine failure");
        }

        return new AuditReport(manifest.ManifestVersion, DateTimeOffset.Now, _components);
    }
}
