using System.Diagnostics;
using System.Text.Json;
using P3B.Remediation.Windows.Processes;

namespace P3B.Remediation.Windows.Tests;

/// <summary>
/// The real <see cref="ProcessRunner"/>, exercised only against the harmless P3B.TestProcess
/// helper (it echoes arguments, writes to stderr, sleeps and exits; no files, registry or network).
/// </summary>
public class ProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static ProcessRequest Helper(TimeSpan timeout, params string[] arguments) =>
        new(TestProcessHelper.ExecutablePath, arguments, timeout);

    [Fact]
    public async Task Exit_code_standard_output_and_standard_error_are_captured()
    {
        var result = await new ProcessRunner().RunAsync(Helper(Generous, "fail", "TEST error text", "7"), CancellationToken.None);

        Assert.Equal(ProcessCompletion.Exited, result.Completion);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("TEST standard output", result.StandardOutput);
        Assert.Equal("TEST error text", result.StandardError);
    }

    [Fact]
    public async Task Arguments_with_shell_characters_arrive_literally()
    {
        string[] tricky = ["two words", "quote\"inside", "a&b|c", "semi;colon", "%PATH%", "$(whoami)", "trailing\\", "--force", ""];

        var result = await new ProcessRunner().RunAsync(Helper(Generous, ["echo-args", .. tricky]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(tricky, JsonSerializer.Deserialize<string[]>(result.StandardOutput));
    }

    [Fact]
    public async Task Timeout_stops_the_process_and_is_reported()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await new ProcessRunner().RunAsync(Helper(TimeSpan.FromMilliseconds(500), "sleep", "30000"), CancellationToken.None);

        Assert.Equal(ProcessCompletion.TimedOut, result.Completion);
        Assert.Null(result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Cancellation_stops_the_process_and_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(Helper(Generous, "sleep", "30000"), cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Standard_input_is_closed_so_nothing_waits_for_input()
    {
        var result = await new ProcessRunner().RunAsync(Helper(TimeSpan.FromSeconds(10), "read-stdin"), CancellationToken.None);

        Assert.Equal(ProcessCompletion.Exited, result.Completion);
        Assert.Equal("0", result.StandardOutput);
    }

    [Fact]
    public async Task Missing_executable_is_reported_as_failed_to_start()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"p3b-missing-{Guid.NewGuid():N}.exe");

        var result = await new ProcessRunner().RunAsync(new ProcessRequest(missing, [], Generous), CancellationToken.None);

        Assert.Equal(ProcessCompletion.FailedToStart, result.Completion);
        Assert.False(string.IsNullOrWhiteSpace(result.StartError));
    }

    [Fact]
    public async Task Environment_variables_are_set_and_removed_for_the_child_process_only()
    {
        var set = new ProcessRequest(
            TestProcessHelper.ExecutablePath, ["print-env", "P3B_TEST_VARIABLE"], Generous, new Dictionary<string, string?> { ["P3B_TEST_VARIABLE"] = "TEST value" });
        var removed = new ProcessRequest(
            TestProcessHelper.ExecutablePath, ["print-env", "COMPUTERNAME"], Generous, new Dictionary<string, string?> { ["COMPUTERNAME"] = null });

        var setResult = await new ProcessRunner().RunAsync(set, CancellationToken.None);
        var removedResult = await new ProcessRunner().RunAsync(removed, CancellationToken.None);

        Assert.Equal("TEST value", setResult.StandardOutput);
        Assert.Equal("(unset)", removedResult.StandardOutput);
        Assert.Null(Environment.GetEnvironmentVariable("P3B_TEST_VARIABLE"));
    }
}
