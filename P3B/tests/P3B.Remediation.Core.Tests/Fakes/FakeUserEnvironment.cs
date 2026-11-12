using P3B.Remediation.Core.Configuration;

namespace P3B.Remediation.Core.Tests.Fakes;

/// <summary>In-memory user environment; the real user environment is never touched.</summary>
internal sealed class FakeUserEnvironment : IUserEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, writes are silently ignored, to test verification.</summary>
    public bool IgnoreWrites { get; init; }

    public int Writes { get; private set; }

    public string? GetUserVariable(string name) => _variables.GetValueOrDefault(name);

    public void SetUserVariable(string name, string? value)
    {
        Writes++;
        if (IgnoreWrites)
        {
            return;
        }

        if (value is null)
        {
            _variables.Remove(name);
        }
        else
        {
            _variables[name] = value;
        }
    }

    /// <summary>Sets a value as if another program had changed it, without counting a P3B write.</summary>
    public void SetExternally(string name, string? value)
    {
        if (value is null)
        {
            _variables.Remove(name);
        }
        else
        {
            _variables[name] = value;
        }
    }
}
