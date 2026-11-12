# PROVISIONAL – P3B proposal for the shared audit-state / remediation contract

> **Status (updated): P3A → P3B direction agreed in code; P3B → P3A file still provisional.**
>
> | Part | Status |
> |---|---|
> | Hand-off method | **Agreed:** in-process call to P3A's `EnvironmentAuditor` (proposed library, [P3A-CHANGES.md](../P3A-CHANGES.md)), not a file. |
> | Issue IDs and codes | **Agreed:** P3A's `AuditFinding` codes; `issueId` = `"{type}/{finding}"`; `toolId` = manifest `type`. |
> | State mapping | **Agreed:** `NotInstalled` / `ExtensionNotInstalled` / `EditorNotDetected` → `missing`; `VersionBelowMinimum` / `VersionAboveMaximum` → `unapprovedVersion`. Other codes → incomplete audit data. P3A reports **no** `misconfigured` state. |
> | Re-audit (P3-AC4.1) | **Agreed:** P3B calls P3A's audit after each fix and shows P3A's Ready / Not Ready. |
> | `RemediationReport` / results file (P3B → P3A) | **Provisional:** a record only; P3A does not need to read it. |
> | Audit-results file (`--audit`) | **Provisional:** alternative hand-off, kept for tests. |
>
> Questions 1, 2 and 4 below are answered by the table. Question 3 (what to do about an
> unapproved version) is open, but moot until the manifest gives an install source.

Implemented by `AuditIssue`, `AuditIssueState` and `RemediationRequestFactory` in
`src/P3B.Remediation.Core/Integration/`.

## P3A → P3B: one non-ready finding (`AuditIssue`)

| Field | Required | Meaning | Source |
|---|---|---|---|
| `issueId` | yes | P3A's identifier for the finding, so each action links back to its originating issue. | P3B-DR-01 "originating audit issue" |
| `toolId` | yes | Manifest tool identity. | P3B-DR-01 "affected tool identity"; 3B-IR-001 |
| `state` | yes | `missing`, `misconfigured` or `unapprovedVersion`. | P3A brief: "report missing, misconfigured or unapproved-version states" |
| `detectedVersion` | when state is `unapprovedVersion` | The version P3A found. | P3-StR1.2 "tell approved versions apart from newer, unapproved ones" |
| `detail` | no | Human-readable explanation. | 3A-UR-003 "reason for its non-ready state" |

Every field is optional at the type level. `RemediationRequestFactory` reports **all**
missing or invalid fields at once, instead of failing on the first one, so incomplete audit
data produces a clear message.

## P3B → P3A: action and verification result (`RemediationReport`)

`RemediationReport.From(run)` gives a JSON-friendly report. Enum values are strings.

| Field | Meaning |
|---|---|
| `ToolId`, `IssueId` | Which tool, and which originating P3A issue. |
| `Outcome` | Remediation outcome: `Succeeded`, `Failed`, `Cancelled`, `Unsupported`, `InvalidConfiguration`, `Rejected` or `Busy`. |
| `Message` | Explanation. |
| `TargetVersion` | The approved version P3B targeted. |
| `ObservedChanges` | What actually changed. |
| `Reversibility`, `ReversalJournalEntryId` | `Automatic`, `ManualOnly` or `NothingToReverse`, and the journal record. |
| `Verification` | Separate smoke-test report: `Outcome` (`Passed`, `Failed`, `TimedOut`, `Unsupported` or `Cancelled`), `Message`, and per-check `Kind`, `Description`, `Status` and `Explanation`. |
| `ReauditRequired` | True whenever something was attempted. |
| `Readiness` | Always "P3B does not determine overall readiness. P3A must re-audit the environment." |

- A remediation outcome of `Succeeded` means the installation was verified as present. A passed `Verification` means the tool's declared smoke tests passed. The two are reported separately.
- **Neither** means the environment is Ready. P3A re-audits (P3-AC4.1).

## Proposed file hand-off (used by the dashboard until P3A integration is agreed)

- **P3A → P3B:** an audit results file, `{ "issues": [ AuditIssue, ... ] }` (camelCase), read by `AuditResultsFile`.
- **P3B → P3A:** a results file, `{ "formatVersion": "p3b-provisional-0", "generatedAt", "readiness", "reports": [ RemediationReport, ... ] }`, written by `RemediationResultsFile`. It holds the latest report per issue and is saved automatically to `%LOCALAPPDATA%\P3B\latest-remediation-results.json`.

## Proposed planning rules

| State | P3B behaviour |
|---|---|
| `missing` | Act with the manifest's approved version, if the tool has a remediation method. |
| `unapprovedVersion` | Same as `missing`. P3B never adopts the detected version (3B-DC-005). |
| `misconfigured` | Unsupported until configuration fixes are specified. |
| Tool not in manifest, or method `none` | Unsupported: "No supported automatic fix is available" (3B-UR-003). |

## Questions for P3A

1. In-process call (shared library) or a file/JSON hand-off between two executables?
2. Who owns issue IDs, and what are the real codes or categories (including system-requirement failures, 3A-UR-004)?
3. Should `unapprovedVersion` remediation uninstall the unapproved version, install side by side, or only advise?
4. What exactly can count as a "misconfigured" state that P3B is allowed to fix (user-level settings only)?
