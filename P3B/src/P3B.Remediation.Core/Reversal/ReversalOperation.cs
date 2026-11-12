using System.Text.Json.Serialization;

namespace P3B.Remediation.Core.Reversal;

/// <summary>
/// Machine-readable description of how to undo one change. Data only: the code that performs
/// a reversal lives in an <see cref="IReversalHandler"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(WingetUninstallReversal), "wingetUninstall")]
[JsonDerivedType(typeof(RestoreUserEnvironmentVariableReversal), "restoreUserEnvironmentVariable")]
[JsonDerivedType(typeof(VsCodeExtensionUninstallReversal), "vscodeExtensionUninstall")]
public abstract record ReversalOperation;

/// <summary>Uninstall exactly the package and version P3B installed.</summary>
public sealed record WingetUninstallReversal(string PackageId, string Version) : ReversalOperation;

/// <summary>
/// Restore a user-level environment variable to its prior value (null means it did not exist),
/// but only while it still holds the value P3B applied.
/// </summary>
public sealed record RestoreUserEnvironmentVariableReversal(string Name, string? PriorValue, string AppliedValue) : ReversalOperation;

/// <summary>Uninstall exactly the VS Code extension and version P3B installed.</summary>
public sealed record VsCodeExtensionUninstallReversal(string ExtensionId, string Version) : ReversalOperation;
