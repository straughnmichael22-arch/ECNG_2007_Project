using System.Text.Json;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>The structured result P3B returns to P3A (PROVISIONAL contract).</summary>
public class RemediationReportTests
{
    private static async Task<Results.RemediationRunResult> RunWithChecks(params SmokeCheckDefinition[] checks)
    {
        var manifest = new InMemoryManifestSource(ManifestLoadResult.Loaded(
            new LocalEdaToolManifest(ProvisionalManifestSchema.SupportedVersion, [TestManifests.Tool(TestManifests.ToolA, smokeChecks: checks)]),
            "TEST manifest"));
        var smokeTests = new SmokeTestService([new FakeSmokeCheckHandler()]);
        return await TestSupport.Service(new FakeExecutor(), smokeTests: smokeTests, manifest: manifest).RemediateAsync(TestSupport.Request());
    }

    [Fact]
    public async Task Report_separates_remediation_outcome_from_verification()
    {
        var report = RemediationReport.From(await RunWithChecks(new TestOnlyCheck("A")));

        Assert.Equal(TestManifests.ToolA, report.ToolId);
        Assert.Equal("TEST-ISSUE-1", report.IssueId);
        Assert.Equal("Succeeded", report.Outcome);
        Assert.Equal(TestManifests.TargetVersion, report.TargetVersion);
        Assert.Equal("Automatic", report.Reversibility);
        Assert.NotNull(report.ReversalJournalEntryId);
        Assert.Equal("Passed", report.Verification?.Outcome);
        var check = Assert.Single(report.Verification!.Checks);
        Assert.Equal("Passed", check.Status);
        Assert.Equal("TEST check A", check.Description);
    }

    [Fact]
    public async Task Report_always_leaves_readiness_to_p3a()
    {
        var report = RemediationReport.From(await RunWithChecks());

        Assert.True(report.ReauditRequired);
        Assert.Equal(RemediationReport.ReadinessStatement, report.Readiness);
        Assert.Equal("Unsupported", report.Verification?.Outcome);
        Assert.Contains("P3A", report.Verification?.Scope);
    }

    [Fact]
    public async Task Report_for_a_refused_request_needs_no_reaudit_and_has_no_verification()
    {
        var run = await TestSupport.Service(new FakeExecutor()).RemediateAsync(TestSupport.Request(TestManifests.NotInManifestTool));

        var report = RemediationReport.From(run);

        Assert.Equal("Unsupported", report.Outcome);
        Assert.False(report.ReauditRequired);
        Assert.Null(report.Verification);
    }

    [Fact]
    public async Task Report_serialises_to_json_with_readable_values()
    {
        var json = JsonSerializer.Serialize(RemediationReport.From(await RunWithChecks(new TestOnlyCheck("A"))));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Succeeded", root.GetProperty("Outcome").GetString());
        Assert.Equal("Passed", root.GetProperty("Verification").GetProperty("Outcome").GetString());
        Assert.Equal(RemediationReport.ReadinessStatement, root.GetProperty("Readiness").GetString());
        Assert.True(root.GetProperty("ReauditRequired").GetBoolean());
    }
}
