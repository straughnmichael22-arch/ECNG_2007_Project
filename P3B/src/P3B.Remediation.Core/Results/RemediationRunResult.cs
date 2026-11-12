namespace P3B.Remediation.Core.Results;

/// <summary>
/// Everything one remediation request produced: the remediation result and, only if the
/// remediation succeeded, a separate smoke-test result (3B-UR-002).
/// </summary>
public sealed record RemediationRunResult(RemediationResult Remediation, SmokeTestResult? SmokeTest);
