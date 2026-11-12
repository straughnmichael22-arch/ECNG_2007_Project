# Integration seams

Each seam is an interface or a type where P3B connects to P3A or to a specification. Safety
decisions live in `P3B.Remediation.Core`, which cannot start processes or touch the registry
(enforced by `SafetyTests`). Real process, WinGet and registry code lives in
`P3B.Remediation.Windows`. P3A's audit is reached only through `P3B.Integration.P3A`.

**Status key.** **Agreed:** built on P3A's real code and files. **Provisional:** P3B's proposal, still to be agreed. **Proposed P3A change:** needs the P3A team to accept a change to their repository (see [P3A-CHANGES.md](P3A-CHANGES.md)).

## How P3A and P3B connect

```
eda-tool-manifest.json (P3A, format 1.0)
   ├─ P3A EnvironmentAuditor.Audit()  → AuditReport (OverallReady, components, finding codes)
   │     └─ P3B.Integration.P3A: P3AAuditAdapter / P3AFindingMapper → AuditIssue[] + ReadinessAudit
   └─ P3B ManifestService → P3AManifestSchema → LocalEdaToolManifest → RemediationPlanner
Dashboard: run P3A audit → list issues → fix (or show manual steps) → P3A re-audit → P3A's Ready / Not Ready
```

## Seams

| Seam | Status | Current state |
|---|---|---|
| P3A audit library `EDAEnvironmentManager.Audit` | **Proposed P3A change** | P3A's detection code moved out of `AuditWindow` into a class library, unchanged, with a typed `AuditReport`. On the branch `p3-integrated` of the group repository, awaiting P3A review. P3B (the `P3B/` folder on that branch) finds it automatically (`P3ARepoRoot`). |
| `Integration/IAuditStateSource` | **Agreed** (on the proposed library) | Production implementation: `P3B.Integration.P3A.P3AAuditAdapter`, which runs P3A's audit in-process. |
| `P3AFindingMapper` (P3A findings → `AuditIssue`) | **Agreed** (on the proposed library) | `IssueId` = `"{type}/{finding}"`, `ToolId` = manifest `type`. `NotInstalled`, `ExtensionNotInstalled`, `EditorNotDetected` → `missing`; `VersionBelowMinimum`, `VersionAboveMaximum` → `unapprovedVersion` with P3A's detected version. Every other finding code (`VersionUnknown`, `VersionUnparseable`, `InvalidMinimumVersion`, `InvalidMaximumVersion`, unknown future codes) is passed on unchanged and reported by `RemediationRequestFactory` as **incomplete audit data**. P3A's placeholders ("Not Available", "Not Checked") are never passed on as versions. |
| `Integration/IReadinessAuditor` (re-audit) | **Agreed** (on the proposed library) | `P3AAuditAdapter`. The dashboard runs it at start-up, after every fix that may have changed the computer, and on request. Its `Verdict` is P3A's `OverallReady`, and it is the **only** Ready / Not Ready the dashboard shows. |
| `Manifest/P3AManifestSchema` | **Agreed** | Reads P3A's `eda-tool-manifest.json` (format `1.0`, names not case-sensitive, as P3A reads them). Validates names, types (unique), version ranges (`System.Version`, as P3A), and that `downloadUrl` is an http(s) address. All load statuses are kept. The file is opened read-only. |
| `Manifest/ProvisionalManifestSchema` | **Provisional** ([proposal](proposals/local-eda-tool-manifest.PROVISIONAL.md)) | Kept only because it is the one format that can express an **automatic** install (exact version + WinGet package ID), which P3A's manifest does not have yet. Chosen by `manifestVersion` in `ManifestFormats`. |
| `Actions/RemediationPlanner` | **Agreed** for P3A's manifest | Vivado is always refused (manual steps still shown). Extension groups are refused: VS Code installs extensions only through `code.cmd`, which P3B never runs. `downloadUrl` / `instructions` → `ManualGuidance`. Method `none` with no steps → "No supported automatic fix". |
| `Configuration/ConfigurationService` | **Not wired, by design** | P3A's audit reports no "misconfigured" state at all (its findings are installed / version / extension only), so there is no user-level setting for P3B to fix. Any future misconfigured finding stays "No supported automatic fix" (3B-UR-003) until P3A reports one that is a user-level setting such as the user PATH. |
| `Installation/IInstaller` | Implemented | `WingetInstaller`, and `VsCodeExtensionInstaller` (**provisional** method `vscodeExtension`; runs `Code.exe` + `cli.js` taken from VS Code's `code.cmd`, which is read but never run). Neither is used with P3A's manifest, which gives no package IDs or exact extension versions. |
| Official-archive installer | **Not built** | Neither manifest format has a SHA-256 field; archives may only be installed after their checksum is verified. |
| `Verification/SmokeTestService` | Implemented | Runs after an automatic fix. P3A's GHDL `smokeTest` is left to P3A: P3B never installs GHDL, so it never needs to verify a GHDL install. |
| `Integration/RemediationReport`, `RemediationResultsFile` | **Provisional** | P3B → P3A results file. With the in-process re-audit, P3A does not need to read it; it remains a record. |
| `Integration/AuditResultsFile` (`--audit`) | **Provisional** | Alternative file hand-off, kept for testing and for running P3B without P3A's library. |
| `Reversal/JsonFileReversalJournal` | Implemented | Location still a group decision (`AppPaths`). |

## Rules for anyone integrating

1. Take field names, tool IDs, versions, sources and issue codes **only** from P3A's code and manifest, or from a supplied specification.
2. Do not move test fixtures into `src/`, and do not reference test projects from the app.
3. Keep both `SafetyTests` passing: tests may use only fakes, plus the harmless `P3B.TestProcess` helper. P3A's audit engine is replaced in tests by `FakeP3AAuditEngine` and `FakeReadinessAuditor`.
4. Never add `--force`, `--override`, machine scope, or automatic source-agreement acceptance to `WingetCommands`.
5. P3B never shows Ready / Not Ready from its own results. Only `ReadinessAudit.Verdict` (P3A) may produce that label.
6. Do not change P3A's repository without the P3A team: propose changes in [P3A-CHANGES.md](P3A-CHANGES.md).
