using System.Text.Json;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Safety;
using P3B.Remediation.Core.Tests.Fakes;
using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// End-to-end scenarios without P3A (3B-IR-005, 3B-DC-007, P3-AC4.1): an audit fixture file goes
/// in, the real planner, InstallationService, SmokeTestService and journal run with a scripted
/// installer, and a P3A stand-in reads the results file back for re-audit.
/// </summary>
public class IntegrationScenarioTests
{
    private static string AuditFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "audit", "standard-audit.test-fixture.json");

    private static LocalEdaToolManifest Manifest(params ManifestTool[] extra) => new(
        ProvisionalManifestSchema.SupportedVersion,
        [
            TestManifests.Tool(TestManifests.ToolA, smokeChecks: [new InstalledVersionCheck()]),
            TestManifests.Tool(TestManifests.ToolB, smokeChecks: [new InstalledVersionCheck()]),
            TestManifests.Tool(TestManifests.NoFixTool, RemediationMethod.None),
            .. extra,
        ]);

    private static RemediationService Service(FakeInstaller installer, IReversalJournal journal, LocalEdaToolManifest manifest) =>
        new(
            new InMemoryManifestSource(ManifestLoadResult.Loaded(manifest, "TEST manifest")),
            [new InstallationService([installer])],
            new SmokeTestService([new InstalledVersionCheckHandler([installer])]),
            new FakeApprovalProvider(),
            journal);

    [Fact]
    public async Task Audit_fixture_round_trip_without_p3a()
    {
        using var temp = new TempDirectory();
        var installer = new FakeInstaller
        {
            // Before install: absent. After install, and for the smoke test: approved version present.
            States = [InstalledState.NotInstalled("TEST absent"), InstalledState.TargetVersion(TestManifests.TargetVersion, "TEST present")],
        };
        var journal = new JsonFileReversalJournal(temp.PathOf("journal.json"));
        var service = Service(installer, journal, Manifest());

        var audit = AuditResultsFile.Load(AuditFixture);
        Assert.True(audit.IsLoaded, audit.Description);

        var reports = new List<RemediationReport>();
        foreach (var issue in audit.Issues)
        {
            var result = await service.RemediateAsync(issue);
            if (result.Run is { } run)
            {
                reports.Add(RemediationReport.From(run));
            }
        }

        var resultsPath = temp.PathOf("results.json");
        await RemediationResultsFile.WriteAsync(resultsPath, reports, DateTimeOffset.UtcNow, CancellationToken.None);

        // P3A stand-in: reads the file as plain JSON, without P3B's types.
        using var document = JsonDocument.Parse(File.ReadAllText(resultsPath));
        var byIssue = document.RootElement.GetProperty("reports").EnumerateArray()
            .ToDictionary(r => r.GetProperty("issueId").GetString()!, r => r);

        Assert.Equal(RemediationReport.ReadinessStatement, document.RootElement.GetProperty("readiness").GetString());
        Assert.Equal(5, byIssue.Count); // the incomplete issue never becomes a request

        var fixedIssue = byIssue["TEST-ISSUE-MISSING"];
        Assert.Equal("Succeeded", fixedIssue.GetProperty("outcome").GetString());
        Assert.Equal("Passed", fixedIssue.GetProperty("verification").GetProperty("outcome").GetString());
        Assert.True(fixedIssue.GetProperty("reauditRequired").GetBoolean());
        Assert.Equal("Automatic", fixedIssue.GetProperty("reversibility").GetString());

        Assert.Equal("Unsupported", byIssue["TEST-ISSUE-NO-FIX"].GetProperty("outcome").GetString());
        Assert.Equal("Unsupported", byIssue["TEST-ISSUE-NOT-IN-MANIFEST"].GetProperty("outcome").GetString());
        Assert.Equal("Unsupported", byIssue["TEST-ISSUE-MISCONFIGURED"].GetProperty("outcome").GetString());
        Assert.False(byIssue["TEST-ISSUE-NO-FIX"].GetProperty("reauditRequired").GetBoolean());

        // Only the approved version was ever installed, never P3A's detected unapproved version.
        Assert.Equal(TestManifests.TargetVersion, installer.LastInstallTarget?.Version);
        Assert.NotEmpty(await journal.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Incomplete_audit_issue_from_the_fixture_is_reported_not_remediated()
    {
        var installer = new FakeInstaller();
        var service = Service(installer, new InMemoryReversalJournal(), Manifest());
        var incomplete = AuditResultsFile.Load(AuditFixture).Issues.Single(i => i.IssueId == "TEST-ISSUE-INCOMPLETE");

        var result = await service.RemediateAsync(incomplete);

        Assert.Null(result.Run);
        Assert.Contains(result.Problems, p => p.Contains("toolId", StringComparison.Ordinal));
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Vivado_is_refused_end_to_end_and_nothing_is_queried_installed_or_licensed()
    {
        var installer = new FakeInstaller();
        var vivado = new ManifestTool(
            new Integration.ToolReference("TEST-AMD-VIVADO"),
            "AMD Vivado",
            ["TEST-VERSION-1"],
            new ManifestRemediation(RemediationMethod.WinGet, "TEST.Vivado.Package"));
        var service = Service(installer, new InMemoryReversalJournal(), Manifest(vivado));

        var result = await service.RemediateAsync(new AuditIssue { IssueId = "TEST-ISSUE-VIVADO", ToolId = "TEST-AMD-VIVADO", State = "missing" });

        Assert.Equal(RemediationOutcome.Unsupported, result.Run?.Remediation.Outcome);
        Assert.Contains(ProhibitedSoftwarePolicy.VivadoMessage, result.Run?.Remediation.Message);
        Assert.Equal(0, installer.StateCalls);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public void Core_contains_no_licensing_or_activation_code()
    {
        var names = typeof(RemediationService).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods().Select(m => m.Name).Prepend(t.Name));

        Assert.DoesNotContain(names, n =>
            n.Contains("Licen", StringComparison.OrdinalIgnoreCase) || n.Contains("Activat", StringComparison.OrdinalIgnoreCase));
    }
}
