using System.Text.Json;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>The PROVISIONAL file hand-offs with P3A: audit results in, remediation results out.</summary>
public class ResultsFilesTests
{
    [Fact]
    public void Audit_results_file_is_read_with_every_issue()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteText("audit.json", """
            { "issues": [
                { "issueId": "TEST-ISSUE-1", "toolId": "TEST-TOOL-A", "state": "unapprovedVersion", "detectedVersion": "TEST-NEW", "detail": "TEST detail" },
                { "issueId": "TEST-ISSUE-2" }
            ] }
            """);

        var load = AuditResultsFile.Load(path);

        Assert.True(load.IsLoaded);
        Assert.Equal(2, load.Issues.Count);
        Assert.Equal("TEST-NEW", load.Issues[0].DetectedVersion);
        Assert.False(RemediationRequestFactory.FromAuditIssue(load.Issues[1]).IsValid);
    }

    [Theory]
    [InlineData(null, "No audit results file")]
    [InlineData("{ broken", "not in the expected format")]
    [InlineData("{ \"something\": 1 }", "no \"issues\" list")]
    public void Unusable_audit_results_are_reported(string? content, string expected)
    {
        using var temp = new TempDirectory();
        var path = content is null ? temp.PathOf("absent.json") : temp.WriteText("audit.json", content);

        var load = AuditResultsFile.Load(path);

        Assert.False(load.IsLoaded);
        Assert.Contains(expected, load.Description);
        Assert.Empty(load.Issues);
    }

    [Fact]
    public async Task Results_file_contains_reports_and_the_readiness_statement()
    {
        using var temp = new TempDirectory();
        var run = await TestSupport.Service(new FakeExecutor()).RemediateAsync(TestSupport.Request());
        var path = temp.PathOf(Path.Combine("sub", "results.json"));

        await RemediationResultsFile.WriteAsync(path, [RemediationReport.From(run)], DateTimeOffset.UtcNow, CancellationToken.None);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(RemediationResultsFile.FormatVersion, document.RootElement.GetProperty("formatVersion").GetString());
        Assert.Equal(RemediationReport.ReadinessStatement, document.RootElement.GetProperty("readiness").GetString());
        Assert.Equal("Succeeded", document.RootElement.GetProperty("reports")[0].GetProperty("outcome").GetString());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData(AuditIssueState.Missing, ReversalExpectation.Reversible)]
    [InlineData(AuditIssueState.UnapprovedVersion, ReversalExpectation.PartiallyReversible)]
    public void Installation_preview_flags_replacing_an_installed_version_as_partially_reversible(AuditIssueState state, ReversalExpectation expected)
    {
        var request = TestSupport.Request(state: state, detectedVersion: "TEST-VERSION-NEWER");

        var preview = new InstallationService([new FakeInstaller()]).CreatePreview(TestSupport.Action(request));

        Assert.Equal(expected, preview.ExpectedReversal);
    }
}
