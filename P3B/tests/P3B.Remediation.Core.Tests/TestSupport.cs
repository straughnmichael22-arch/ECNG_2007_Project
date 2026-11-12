using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

internal static class TestSupport
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Obviously fake references; no real tool names or issue codes are used.</summary>
    public static RemediationRequest Request(
        string tool = TestManifests.ToolA,
        string issue = "TEST-ISSUE-1",
        AuditIssueState state = AuditIssueState.Missing,
        string? detectedVersion = null) =>
        new(new ToolReference(tool), new IssueReference(issue), state, detectedVersion);

    public static RemediationAction Action(RemediationRequest? request = null) =>
        new(request ?? Request(), TestManifests.Tool(TestManifests.ToolA));

    public static RemediationService Service(
        IRemediationExecutor? executor = null,
        IApprovalProvider? approvals = null,
        ISmokeTestRunner? smokeTests = null,
        IManifestSource? manifest = null,
        IReversalJournal? journal = null,
        OperationGate? gate = null) =>
        new(
            manifest ?? InMemoryManifestSource.WithTestTools(),
            executor is null ? Array.Empty<IRemediationExecutor>() : new[] { executor },
            smokeTests ?? new FakeSmokeTestRunner(),
            approvals ?? new FakeApprovalProvider(),
            journal ?? new InMemoryReversalJournal(),
            gate);
}
