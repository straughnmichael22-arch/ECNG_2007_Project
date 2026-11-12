using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>P3B-PR-01: one active remediation action at a time.</summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task Request_made_while_an_action_is_active_is_rejected_as_busy()
    {
        var executor = new FakeExecutor { Mode = FakeExecutionMode.WaitForRelease };
        var service = TestSupport.Service(executor);

        var first = service.RemediateAsync(TestSupport.Request());
        await executor.Started.Task.WaitAsync(TestSupport.Timeout);
        Assert.True(service.IsBusy);

        var second = await service.RemediateAsync(TestSupport.Request(TestManifests.ToolB));

        Assert.Equal(RemediationOutcome.Busy, second.Remediation.Outcome);
        Assert.Equal(RemediationService.BusyMessage, second.Remediation.Message);
        Assert.Null(second.Remediation.Preview);

        executor.Release.SetResult();
        var firstRun = await first.WaitAsync(TestSupport.Timeout);

        Assert.Equal(RemediationOutcome.Succeeded, firstRun.Remediation.Outcome);
        Assert.Equal(1, executor.ExecuteCalls);
        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Many_simultaneous_requests_never_execute_more_than_one_action()
    {
        const int requestCount = 20;
        var executor = new FakeExecutor { Mode = FakeExecutionMode.WaitForRelease };
        var service = TestSupport.Service(executor);

        var runs = Enumerable.Range(0, requestCount)
            .Select(i => Task.Run(() => service.RemediateAsync(TestSupport.Request(issue: $"TEST-ISSUE-{i}"))))
            .ToArray();

        await executor.Started.Task.WaitAsync(TestSupport.Timeout);

        // Every other request should be refused immediately while the first is held.
        var deadline = DateTime.UtcNow + TestSupport.Timeout;
        while (runs.Count(t => t.IsCompleted) < requestCount - 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        executor.Release.SetResult();
        var results = await Task.WhenAll(runs).WaitAsync(TestSupport.Timeout);

        Assert.Equal(1, executor.MaxConcurrentExecutions);
        Assert.Equal(1, executor.ExecuteCalls);
        Assert.Single(results, r => r.Remediation.Outcome == RemediationOutcome.Succeeded);
        Assert.Equal(requestCount - 1, results.Count(r => r.Remediation.Outcome == RemediationOutcome.Busy));
    }

    [Fact]
    public async Task Service_accepts_a_new_request_after_the_previous_one_completes()
    {
        var executor = new FakeExecutor();
        var service = TestSupport.Service(executor);

        var first = await service.RemediateAsync(TestSupport.Request());
        var second = await service.RemediateAsync(TestSupport.Request(TestManifests.ToolB));

        Assert.Equal(RemediationOutcome.Succeeded, first.Remediation.Outcome);
        Assert.Equal(RemediationOutcome.Succeeded, second.Remediation.Outcome);
        Assert.Equal(2, executor.ExecuteCalls);
    }

    [Fact]
    public async Task Busy_state_is_released_after_an_executor_exception()
    {
        var executor = new FakeExecutor { Mode = FakeExecutionMode.Throw };
        var service = TestSupport.Service(executor);

        var first = await service.RemediateAsync(TestSupport.Request());
        var second = await service.RemediateAsync(TestSupport.Request());

        Assert.False(service.IsBusy);
        Assert.Equal(RemediationOutcome.Failed, first.Remediation.Outcome);
        Assert.Equal(RemediationOutcome.Failed, second.Remediation.Outcome);
    }
}
