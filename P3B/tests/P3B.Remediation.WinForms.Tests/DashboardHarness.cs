using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.WinForms.Composition;
using P3B.Remediation.WinForms.Presentation;

namespace P3B.Remediation.WinForms.Tests;

/// <summary>
/// A dashboard wired to the real remediation and rollback services, but with fake executors,
/// approvals, smoke tests and reversal handlers: nothing is installed or changed. Files live in
/// a temporary folder that is deleted afterwards.
/// </summary>
internal sealed class DashboardHarness : IDisposable
{
    public const string StandardManifest = """
        {
          "_testFixture": "TEST FIXTURE - provisional format, fake values only.",
          "manifestVersion": "p3b-provisional-0",
          "tools": [
            { "id": "TEST-TOOL-A", "displayName": "Test tool A", "approvedVersions": [ "TEST-VERSION-1" ],
              "remediation": { "method": "winget", "source": "TEST.Package.A" } },
            { "id": "TEST-TOOL-NO-FIX", "approvedVersions": [ "TEST-VERSION-1" ], "remediation": { "method": "none" } }
          ]
        }
        """;

    public const string StandardAudit = """
        {
          "issues": [
            { "issueId": "TEST-ISSUE-A", "toolId": "TEST-TOOL-A", "state": "missing", "detail": "TEST not found" },
            { "issueId": "TEST-ISSUE-NO-FIX", "toolId": "TEST-TOOL-NO-FIX", "state": "missing" },
            { "issueId": "TEST-ISSUE-UNKNOWN", "toolId": "TEST-TOOL-NOT-IN-MANIFEST", "state": "missing" },
            { "issueId": "TEST-ISSUE-INCOMPLETE", "state": "missing" }
          ]
        }
        """;

    private readonly string _root;

    private readonly IRemediationExecutor _executor;

    public DashboardHarness(IRemediationExecutor? executor = null, FakeReversalHandler? reversalHandler = null, IReadinessAuditor? auditor = null)
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "p3b-tests", Guid.NewGuid().ToString("N"))).FullName;
        _executor = executor ?? new FakeExecutor();
        ReversalHandler = reversalHandler ?? new FakeReversalHandler();
        Journal = new JsonFileReversalJournal(PathOf("journal.json"));
        ResultsPath = PathOf("results.json");

        var gate = new OperationGate();
        var manifest = new SwitchableManifestSource();
        var approvals = new ProgressApprovalProvider(Approvals);
        IRemediationExecutor[] executors = [_executor];
        var remediation = new RemediationService(manifest, executors, new FakeSmokeTests(), approvals, Journal, gate);
        var rollback = new RollbackService(Journal, [ReversalHandler], gate);

        Controller = new DashboardController(manifest, remediation, executors, rollback, Journal, approvals, gate, ResultsPath, readinessAuditor: auditor);
    }

    public FakeExecutor Executor => _executor as FakeExecutor
        ?? throw new InvalidOperationException("This harness was created with a different executor.");

    public FakeApprovals Approvals { get; } = new();

    public FakeReversalHandler ReversalHandler { get; }

    public JsonFileReversalJournal Journal { get; }

    public DashboardController Controller { get; }

    public string ResultsPath { get; }

    public string PathOf(string name) => Path.Combine(_root, name);

    public string Write(string name, string content)
    {
        var path = PathOf(name);
        File.WriteAllText(path, content);
        return path;
    }

    public DashboardHarness LoadStandard()
    {
        Controller.OpenManifest(Write("manifest.json", StandardManifest));
        Controller.LoadAuditResults(Write("audit.json", StandardAudit));
        return this;
    }

    public IssueItem Issue(string issueId) => Controller.Issues.Single(i => i.Issue.IssueId == issueId);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

public enum FakeMode
{
    Succeed,
    Fail,
    WaitForRelease,
    WaitForCancellation,
}

/// <summary>Executor double for WinGet actions; never runs anything.</summary>
internal sealed class FakeExecutor : IRemediationExecutor
{
    public FakeMode Mode { get; init; } = FakeMode.Succeed;

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CanHandle(RemediationAction action) => action.Method == RemediationMethod.WinGet;

    public ActionPreview CreatePreview(RemediationAction action) =>
        new(action, ["TEST change (simulated)"], "TEST reversal (simulated)");

    public async Task<ExecutionReport> ExecuteAsync(ActionPreview preview, ApprovalDecision approval, CancellationToken cancellationToken)
    {
        if (!approval.Approves(preview))
        {
            return ExecutionReport.Failure("TEST not approved");
        }

        Started.TrySetResult();
        switch (Mode)
        {
            case FakeMode.Fail:
                return ExecutionReport.Failure("TEST installer failed with exit code 0x8A150011");
            case FakeMode.WaitForRelease:
                await Release.Task.WaitAsync(cancellationToken);
                break;
            case FakeMode.WaitForCancellation:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }

        return ExecutionReport.Success(
            "TEST installed and verified",
            "TEST not installed",
            ["TEST-TOOL-A: not installed -> version TEST-VERSION-1 installed"],
            ReversalInfo.Automatic(
                preview,
                "TEST uninstall",
                "TEST not installed",
                ["TEST-TOOL-A: not installed -> version TEST-VERSION-1 installed"],
                new WingetUninstallReversal("TEST.Package.A", "TEST-VERSION-1")));
    }
}

/// <summary>Stands in for the user at the approval dialog.</summary>
internal sealed class FakeApprovals : IApprovalProvider
{
    public bool Approve { get; set; } = true;

    public int Requests { get; private set; }

    public Task<ApprovalDecision> RequestApprovalAsync(ActionPreview preview, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(Approve ? ApprovalDecision.Approve(preview) : ApprovalDecision.Reject(preview, "TEST: the user chose not to apply this change."));
    }
}

internal sealed class FakeSmokeTests : Core.Execution.ISmokeTestRunner
{
    public Task<SmokeTestResult> RunAsync(ActionPreview preview, CancellationToken cancellationToken) =>
        Task.FromResult(new SmokeTestResult
        {
            PreviewId = preview.PreviewId,
            Tool = preview.Tool,
            TargetVersion = preview.Action.TargetVersion,
            Outcome = SmokeTestOutcome.Passed,
            Message = "TEST all smoke tests passed",
            CompletedAt = DateTimeOffset.UtcNow,
        });
}

/// <summary>Reversal handler double; reverses nothing.</summary>
internal sealed class FakeReversalHandler : IReversalHandler
{
    public bool WaitForCancellation { get; init; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CanHandle(ReversalOperation operation) => true;

    public async Task<ReversalAttempt> ReverseAsync(ReversalOperation operation, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return new ReversalAttempt(ReversalAttemptStatus.Reversed, "TEST reversed and verified");
    }
}

/// <summary>Collects progress messages synchronously.</summary>
internal sealed class ListProgress : IProgress<string>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }

    public void Report(string value)
    {
        lock (_messages)
        {
            _messages.Add(value);
        }
    }
}
