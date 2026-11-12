using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>A smoke check used only by tests, so fake handlers never claim real check kinds.</summary>
internal sealed record TestOnlyCheck(string Name) : SmokeCheckDefinition
{
    public override string Kind => "testOnly";

    public override string Describe() => $"TEST check {Name}";
}

internal enum FakeCheckBehaviour
{
    Pass,
    Fail,
    Unsupported,
    HangUntilCancelled,
    Throw,
}

/// <summary>Scripted handler for <see cref="TestOnlyCheck"/>; behaviour is chosen per check name.</summary>
internal sealed class FakeSmokeCheckHandler : ISmokeCheckHandler
{
    private readonly List<string> _ran = [];

    public Dictionary<string, FakeCheckBehaviour> Behaviours { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Ran => _ran;

    public bool CanHandle(SmokeCheckDefinition check) => check is TestOnlyCheck;

    public async Task<SmokeCheckResult> RunAsync(SmokeCheckDefinition check, RemediationAction action, CancellationToken cancellationToken)
    {
        var name = ((TestOnlyCheck)check).Name;
        _ran.Add(name);

        switch (Behaviours.GetValueOrDefault(name, FakeCheckBehaviour.Pass))
        {
            case FakeCheckBehaviour.Fail:
                return SmokeCheckResult.For(check, SmokeCheckStatus.Failed, $"TEST {name} failed");
            case FakeCheckBehaviour.Unsupported:
                return SmokeCheckResult.For(check, SmokeCheckStatus.Unsupported, $"TEST {name} unsupported");
            case FakeCheckBehaviour.HangUntilCancelled:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            case FakeCheckBehaviour.Throw:
                throw new InvalidOperationException($"TEST {name} exception");
            default:
                return SmokeCheckResult.For(check, SmokeCheckStatus.Passed, $"TEST {name} passed");
        }
    }
}

/// <summary>In-memory file system: only the listed files exist.</summary>
internal sealed class FakeFileProbe : IFileProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string ExpandEnvironmentVariables(string path) =>
        Variables.Aggregate(path, (current, variable) =>
            current.Replace($"%{variable.Key}%", variable.Value, StringComparison.OrdinalIgnoreCase));

    public bool FileExists(string path) => Files.Contains(path);
}

/// <summary>In-memory command lookup.</summary>
internal sealed class FakeCommandLocator : ICommandLocator
{
    public Dictionary<string, string> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Locate(string command) => Commands.GetValueOrDefault(command);
}
