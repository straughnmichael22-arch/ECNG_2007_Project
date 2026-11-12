namespace P3B.Remediation.Core.Configuration;

/// <summary>
/// The current user's environment variables. There is deliberately no machine-scope access:
/// P3B never changes system-wide environment variables.
/// </summary>
public interface IUserEnvironment
{
    /// <summary>Reads a user variable without expanding embedded references such as <c>%USERPROFILE%</c>.</summary>
    string? GetUserVariable(string name);

    /// <summary>Writes a user variable, preserving its value type. Null removes it.</summary>
    void SetUserVariable(string name, string? value);
}
