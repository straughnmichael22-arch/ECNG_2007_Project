# Proposed changes to P3A's repository (for the P3A team)

P3B needs to run P3A's audit and read its results as data. Today every check lives in private
methods of `AuditWindow`, and results are only drawn as cards, so no other program can use them.
The changes below are the smallest that make that possible **without changing what P3A detects
or shows**.

They are on the branch **`p3-integrated`** of
https://github.com/straughnmichael22-arch/ECNG_2007_Project, based on `3Av2` (commit `0ea15ae`).
The same branch adds P3B in the `P3B/` folder. `main` and `3Av2` are unchanged. Please review,
then merge it (for example through a pull request from `p3-integrated`).

## Changes

| # | File | Change | Why |
|---|---|---|---|
| 1 | `EDAEnvironmentManager.Audit/EDAEnvironmentManager.Audit.csproj` | **New** class library (`net10.0`, no UI). | So P3A's window and P3B's dashboard run the same checks. |
| 2 | `EDAEnvironmentManager.Audit/EdaManifest.cs` | **Moved** `EdaManifest`, `EdaTool`, `SmokeTestSettings`, `RemediationSettings`, `EdaExtension`, `ComponentActions` out of `AuditWindow.cs`, character for character, same namespace `EDAEnvironmentManager`. | Shared model. |
| 3 | `EDAEnvironmentManager.Audit/EdaManifestLoader.cs` | **Moved** `LoadManifest` as `EdaManifestLoader.TryLoad(path, out statusMessage)`. Same JSON options and the same four status messages. | Loading without a window. |
| 4 | `EDAEnvironmentManager.Audit/EnvironmentAuditor.cs` | **Moved** `FindExecutables`, `ResolveWhereResult`, `FindExecutable`, `FindUsingWhere`, `FindUsingManifest`, `ExpandManifestPath`, `GetToolVersion`, `ParseToolVersion`, `RunVersionCommand`, `IsVersionApproved`, `TryFindInstalledExtension` **unchanged**. The decision parts of `AddToolResult` / `AddExtensionResult` are now `AuditTool` / `AuditExtension`, returning data instead of drawing a card. `GetVersionApprovalReason` became `ClassifyVersion`, with the same messages, also returning a finding code. New `Audit(manifest)` returns an `AuditReport`. | The audit as a callable API. |
| 5 | `EDAEnvironmentManager.Audit/AuditReport.cs` | **New**: `AuditReport` (`ManifestVersion`, `AuditedAt`, `Components`, `OverallReady`, `OverallStatus`), `ComponentAuditResult` (the card's fields plus `Kind`, `Type`, `Finding`, `CountsAsReady`), and `AuditFinding`: `None`, `NotInstalled`, `VersionUnknown`, `VersionUnparseable`, `VersionBelowMinimum`, `VersionAboveMaximum`, `InvalidMinimumVersion`, `InvalidMaximumVersion`, `EditorNotDetected`, `ExtensionNotInstalled`, `CheckDisabled`. | One code per reason the window already shows, so other programs need not parse reason text. |
| 6 | `AuditWindow.cs` | Removed the moved code; `LoadManifest` calls `EdaManifestLoader`; `RunAuditButton_Click` calls `EnvironmentAuditor.Audit` and draws one card per result with the **same** `CreateResultCard`. Smoke test and remediation code is untouched. 32 lines added, 896 removed. | The window keeps its exact behaviour. |
| 7 | `EDAEnvironmentManager.csproj` | Excludes `EDAEnvironmentManager.Audit\**` from the app's own compile items and references the library. | Otherwise the library's files would be compiled twice. |
| 8 | `EDAEnvironmentAudit.csproj` | **Deleted.** It was identical to `EDAEnvironmentManager.csproj`. | Two project files in one folder make `dotnet build` fail (MSB1011). |
| 9 | `EDAEnvironmentManager.slnx` | **New** solution file with both projects. | One-command build: `dotnet build EDAEnvironmentManager.slnx`. |
| 10 | `.gitignore` | **New**: `bin/`, `obj/`. | Keeps build output out of the repository. |
| 11 | `EDAEnvironmentManager.csproj` | Also excludes `P3B\**` from the app's compile, content and resource items. | P3B is now a folder in the same repository; without this the app would try to compile P3B's files. |
| 12 | `README.md` | Adds a short repository layout and build section. | So everyone can build both parts. |

Verified: `dotnet build EDAEnvironmentManager.slnx -warnaserror` → 0 warnings, 0 errors. The
app's output contains `EDAEnvironmentManager.Audit.dll` and `eda-tool-manifest.json`.

Behaviour notes:
- Two extension result reasons are new text in `ComponentAuditResult.Reason` ("The editor (…) was not detected, …", "None of the approved extensions is installed."). The card does not show them, exactly as before.
- `AuditReport.OverallReady` is computed the same way as the old `allReady` loop: a disabled extension check still counts as ready.

## Observations (not changed, for P3A to decide)

1. `RunVersionCommand` builds a `cmd.exe /c` command line by string concatenation for `.bat`/`.cmd` files (Vivado). A path or argument containing `&` or `"` could be misread. Consider rejecting such characters, or requiring `.exe`.
2. `RunGhdlCommand` waits with no timeout (`WaitForExit()`), so a hung GHDL would freeze the smoke test. `RunVersionCommand` uses 15 s.
3. `.github/workflows/dotnet-desktop.yml` is the unedited template (targets .NET 8, placeholder names), so CI cannot pass.
4. `README.md` is one line; build and run instructions would help markers.
