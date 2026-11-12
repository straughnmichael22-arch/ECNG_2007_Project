using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Results;

namespace P3B.Remediation.Core.Execution;

/// <summary>
/// Runs the approved post-remediation check for a tool (P3-StR2.2).
/// </summary>
/// <remarks>
/// INTERFACE ONLY: smoke-test definitions are expected to come from the manifest, which is
/// not available yet. No production implementation exists.
/// </remarks>
public interface ISmokeTestRunner
{
    Task<SmokeTestResult> RunAsync(ActionPreview preview, CancellationToken cancellationToken);
}
