# P3B Installation & Remediation – Verification and Integration Review

**Date:** 11 November 2026 (updated after P3A integration and the VS Code extension installer).
**Scope:** P3B against the Project 3 brief (P3-StR2.1–4.1, P3-AC2.1–4.1) and the group SRS (P3B-PR, 3B-UR, 3B-IR, P3B-DR, 3B-DC, FR-004).

## Summary

| | |
|---|---|
| Build | `dotnet build P3B.slnx`: **0 warnings, 0 errors** (warnings are treated as errors) |
| Automated tests | **413 of 413 passed**: Core 238, Windows 100, WinForms 42, Integration.P3A 33 |
| P3A integration | P3B runs P3A's own audit in-process and reads P3A's real `eda-tool-manifest.json` (format 1.0). Ready / Not Ready is only ever P3A's verdict. The audit library is a **proposed P3A change on the branch `p3-integrated`, awaiting P3A review** ([P3A-CHANGES.md](P3A-CHANGES.md)). |
| Real-window checks | PR-02 **20/20**, PR-03 **20/20** (`tools/Measure-UiResponsiveness.ps1`, before integration). The integrated dashboard was also run against P3A's real audit of the development machine; it installed nothing. |
| Real software installed during verification | **None.** Every installer and system change was a test double. The only real processes started were the harmless `P3B.TestProcess` helper, read-only `winget --version` / `winget list`, P3A's read-only audit (`where.exe`, version queries) and the P3B window itself. |
| Environment | Windows 11 Home 10.0.26200, .NET SDK 10.0.100, WinGet v1.29.380 (development machine) |

**Main limitations:**
- **P3A's manifest has no install sources** (no package ID, archive URL or checksum, and only version *ranges*). With it, P3B cannot install anything automatically: it shows manual steps (Surfer, Vivado) or "No supported automatic fix" (VS Code, GHDL, extensions). The automatic paths (WinGet, VS Code extension) are verified with a scripted process runner against the **provisional** manifest format.
- **No resettable Windows VM** has been provided, so no real installation or reversal has been run (3B-DC-006).

### Status key

| Status | Meaning |
|---|---|
| **Pass** | Automated tests demonstrate the requirement, and all of them pass. |
| **Pass (P3A, pending acceptance)** | Passes against P3A's real code and manifest, through the proposed audit library the P3A team has not yet accepted. |
| **Pass (provisional)** | Passes against P3B's *provisional* manifest format or TEST fixtures. Must be re-run when real data is supplied. |
| **Pass (dev machine)** | Measured on the development machine; the SRS requires the agreed reference machine. |
| **Partial** | Implemented and partly evidenced; the rest needs something not yet available (see Notes). |
| **Not verified** | Implemented or ready, but no test evidence is possible yet. |
| **Not implemented** | Deliberately not built (see Notes). |

Test names are `Class.Method` in `tests/`. "Core", "Win", "UI" and "P3A" refer to the `P3B.Remediation.Core.Tests`, `.Windows.Tests`, `.WinForms.Tests` and `P3B.Integration.P3A.Tests` projects.

## 1. SRS requirements traceability

| ID | Implemented component | Source files | Covering tests | Status | Notes |
|---|---|---|---|---|---|
| **P3B-PR-01** One active remediation at a time | `OperationGate` shared by `RemediationService` and `RollbackService`; dashboard refuses a P3A audit, fix or reversal while another runs | `Core/Execution/OperationGate.cs`, `RemediationService.cs`, `Core/Reversal/RollbackService.cs`, `WinForms/Presentation/DashboardController.cs` | Core `ConcurrencyTests` (4); `RemediationJournalTests.Remediation_is_refused_while_a_shared_gate_is_held_by_a_rollback`; `RollbackServiceTests.Reversal_is_refused_while_remediation_holds_the_shared_gate`; UI `DashboardControllerTests.Only_one_operation_runs_at_a_time_and_the_ui_can_see_it`, `ReauditFlowTests.P3A_audit_is_not_started_while_a_fix_is_running` | **Pass** | |
| **P3B-PR-02** UI acknowledges commands ≤ 1 s, 19 of 20 | Work runs off the UI thread; immediate busy state | `DashboardController.cs`, `MainForm.cs` | UI `PerformanceTrialTests.PR02_…` (20/20); real window script (20/20; 157–508 ms) | **Pass (dev machine)** | Evidence in `docs/evidence/`. P3A's audit also runs off the UI thread. Repeat on the reference machine. |
| **P3B-PR-03** Outcome shown ≤ 2 s after completion, 19 of 20 | Result, journal, smoke test and results file processed straight after completion | `RemediationService.cs`, `DashboardController.cs`, `MainForm.cs` | UI `PerformanceTrialTests.PR03_…` (20/20); real window script (20/20; 193–600 ms) | **Pass (dev machine)** | P3B's outcome is shown before the automatic P3A re-audit starts; the re-audit's own time (a few seconds) is P3A's. |
| **3B-UR-001** Show tool, target version, changes, reversal before approval | `InstallationService.CreatePreview`, installer `DescribeInstall`, `ApprovalDialog` | `Core/Installation/*`, `Windows/VsCode/VsCodeExtensionInstaller.cs`, `WinForms/ApprovalDialog.cs` | Core `InstallationServiceTests.Preview_shows_tool_target_version_changes_and_reversal_method`, `ApprovalTests` (5); UI `LabelAndDialogTests.Approval_text_shows_tool_version_changes_reversal_and_reversibility` | **Pass** | |
| **3B-UR-002** Remediation and smoke-test outcomes as distinct labelled results | Separate results; "P3B remediation result" / "P3B smoke tests" tabs; P3A's verdict in its own box | `Core/Results/*`, `MainForm.cs`, `StatusText.cs` | Core `ResultSeparationTests` (5), `RemediationReportTests.Report_separates_remediation_outcome_from_verification`; UI `LabelAndDialogTests.Verification_labels`, `ReauditFlowTests.Fix_that_P3A_still_rejects_stays_Not_Ready_and_keeps_P3B_result_visible` | **Pass** | |
| **3B-UR-003** State that no supported automatic fix exists | Planner; "Not supported", "Manual steps", "No automatic fix" labels | `Core/Actions/RemediationPlanner.cs`, `ManualGuidance.cs`, `StatusText.cs` | Core `OutcomeTests` (3 unsupported cases), `ManualGuidancePlannerTests` (11); P3A `RealP3AManifestTests.Tools_with_no_remediation_in_the_manifest_have_no_automatic_fix`; UI `DashboardControllerTests.Supported_and_unsupported_…`, `ReauditFlowTests.Manual_steps_issue_shows_the_manifest_steps_then_P3A_rechecks_it` | **Pass** | |
| **3B-UR-004** 4 of 5 students identify change, reversal, outcome unaided | Dashboard and approval dialog designed for this | `MainForm.cs`, `ApprovalDialog.cs` | None possible automatically | **Not verified** | Needs the usability study with 5 students (§5). |
| **3B-IR-001** Read the manifest independently, no hard-coded tools | `ManifestService` → `ManifestFormats` → `P3AManifestSchema` (P3A 1.0) or `ProvisionalManifestSchema` | `Core/Manifest/*` | Core `P3AManifestSchemaTests` (16), `ManifestServiceTests` (16), `ManifestSmokeTestParsingTests` (15); P3A `RealP3AManifestTests` (8) | **Pass** | P3A's real manifest is read and validated. The only fixed rule is the Vivado *block*. |
| **3B-IR-002** Accept audit state; return action and verification results | `P3AAuditAdapter` (`IAuditStateSource`), `P3AFindingMapper`, `RemediationRequestFactory`, `RemediationReport` | `src/P3B.Integration.P3A/*`, `Core/Integration/*` | P3A `P3AFindingMapperTests` (17: valid, incomplete and unknown-state findings), `P3AAuditAdapterTests` (8); Core `AuditContractTests` (11), `RemediationReportTests` (4), `ResultsFilesTests` (7) | **Pass (P3A, pending acceptance)** | Built on P3A's real finding codes. The P3B → P3A results file stays provisional (a record; P3A re-audits directly). |
| **3B-IR-003** Report an invalid or unreadable manifest before remediating | `RemediationService` loads the manifest first; P3A audit refuses a manifest not in P3A's format | `RemediationService.cs`, `ManifestService.cs`, `P3AAuditAdapter.cs` | Core `OutcomeTests.Unusable_manifest_is_reported_before_any_preview_or_execution`, `…Invalid_manifest_file_stops_remediation_before_execution`, `P3AManifestSchemaTests.Invalid_P3A_manifest_is_reported_with_the_path_of_the_problem` (8); P3A `P3AAuditAdapterTests.Manifest_in_another_format_is_not_audited` | **Pass** | |
| **3B-IR-004** Install via WinGet, official archives or staff-approved source | `WingetInstaller`; `VsCodeExtensionInstaller` (VS Code's own command line, run as `Code.exe` without a shell) | `Windows/WinGet/*`, `Windows/VsCode/*`, `Windows/Processes/*` | Win `WingetCommandsTests` (19), `WingetInstallerTests` (21), `VsCodeExtensionInstallerTests` (28), `ProcessRunnerTests` (7) | **Partial** | Both installers tested with a scripted process runner only. **Official archives not implemented:** neither manifest format has a SHA-256 field (see §4). No real install run (no VM). |
| **3B-IR-005** Verifiable with fixtures or test doubles without P3A | Fakes for every external seam, including P3A's audit (`FakeP3AAuditEngine`, `FakeReadinessAuditor`) | `tests/**` | Core `SafetyTests` (6), `IntegrationScenarioTests` (4); P3A `P3AAuditAdapterTests.Every_P3A_audit_engine_in_these_tests_is_a_fake`; UI `ReauditFlowTests` (9) | **Pass (provisional)** | Staff fixtures not supplied. |
| **P3B-DR-01** Link each action to tool and issue; record target, prior state, changes, outcome, smoke tests | `RemediationAction`, `RemediationResult`, `ReversalJournalEntry`, `RemediationReport`; issue IDs `"{type}/{finding}"` from P3A | `Core/Actions/*`, `Core/Results/*`, `Core/Reversal/*` | Core `ActionRecordTests` (2), `RemediationJournalTests.Successful_change_is_recorded_in_the_journal`, `JsonFileReversalJournalTests` (6), `VsCodeExtensionManifestTests.Extension_uninstall_reversal_survives_the_journal_format` | **Pass** | |
| **P3B-DR-02** Manifest read-only; keep reversal info until reversed or discarded after warning | Read-only manifest model and file access (both formats); `JsonFileReversalJournal` | `Core/Manifest/*`, `Core/Reversal/JsonFileReversalJournal.cs` | Core `ManifestReadOnlyTests` (6), `P3AManifestSchemaTests.P3A_manifest_file_is_read_without_being_changed`, `RollbackServiceTests.Cancelled_reversal_keeps_the_record`; UI `DashboardControllerTests.Discarding_requires_acknowledgement` | **Pass** | Journal location: group decision pending. |
| **3B-DC-001** Only approved open-source tools and user-level settings | Planner limited to manifest entries; WinGet `--scope user`; extensions go to the user's VS Code profile; user-only environment interface | `RemediationPlanner.cs`, `WingetCommands.cs`, `VsCodeExtensionInstaller.cs`, `Core/Configuration/*` | Core `RemediationPlannerTests` (9); Win `WingetCommandsTests.No_command_forces_overrides_targets_the_machine_or_accepts_source_agreements`, `VsCodeExtensionInstallerTests.No_command_ever_forces_an_install` | **Pass (provisional)** | Which tools may be installed comes from the manifest; P3A's has no install sources yet. |
| **3B-DC-002** Course-approved sources, preferring WinGet or official archives | Source taken only from the manifest | `WingetCommands.cs`, `InstallModels.cs`, `VsCodeExtensionInstaller.cs` | Win `WingetCommandsTests.Install_is_exact_version_pinned_per_user_and_winget_source_only`, `VsCodeExtensionInstallerTests.Approved_install_runs_the_exact_pinned_command_…` | **Pass (provisional)** | Archives not implemented (no checksum). |
| **3B-DC-003** Changes explicit and reversible | Mandatory preview with reversal method; journal; `RollbackService`; WinGet and VS Code extension uninstall handlers | `ActionPreview.cs`, `InstallationService.cs`, `Core/Reversal/*`, `WingetUninstallHandler.cs`, `VsCodeExtensionInstaller.cs` | Core `RollbackServiceTests` (10), `InstallationServiceTests` (15); Win `WingetUninstallHandlerTests` (5), `VsCodeExtensionInstallerTests.Reversal_*` (3), `…Replacing_another_version_is_manual_only_reversal`; UI `LabelAndDialogTests.Only_a_completed_reversal_is_labelled_reversed` | **Pass** | Reversal of a real install not yet run (DC-006). |
| **3B-DC-004** Never install or license Vivado | `ProhibitedSoftwarePolicy` in the planner and in `InstallationService` | `Core/Safety/ProhibitedSoftwarePolicy.cs` | Core `RemediationPlannerTests.Vivado_is_always_unsupported_whatever_the_manifest_says`, `InstallationServiceTests.Vivado_is_never_installed_even_if_the_manifest_allows_it`, `IntegrationScenarioTests.Vivado_is_refused_end_to_end_…`, `ManualGuidancePlannerTests.Vivado_stays_prohibited_but_the_manifest_steps_are_still_shown`; P3A `RealP3AManifestTests.Vivado_is_never_installed_but_P3A_manual_steps_are_shown` | **Pass** | P3A's own manual steps are shown; P3B performs none of them. |
| **3B-DC-005** Never install or upgrade to unapproved versions | Target version only from the manifest's exact approved list; pinned commands (`--version`, `id@version`); never forced | `RemediationAction.cs`, `InstallationService.cs`, `WingetCommands.cs`, `VsCodeExtensionCommands` | Core `InstallationServiceTests.Only_the_manifest_approved_target_version_is_installed_…`, `VsCodeExtensionManifestTests.Extension_install_cannot_be_defined_without_an_exact_version`; Win `WingetCommandsTests.Install_is_exact_version_pinned_…`, `VsCodeExtensionInstallerTests.Unsafe_identifier_or_version_is_rejected_…` (4) | **Pass** | A P3A *range* is never turned into an install target. VS Code's extension auto-update must be checked on the VM (§4). |
| **3B-DC-006** Test installation in the resettable Windows environment | – | – | None | **Not verified** | No VM provided. Deliberately no real installs on the development machine. |
| **3B-DC-007** Independently testable without P3A or Project 4 | Fakes and fixtures; P3A reached only through interfaces | `tests/**` | All 413 tests run with P3A's audit replaced by fakes; Core `SafetyTests` (6) | **Pass** | Building still needs P3A's library source (the clone). |
| **FR-004** Audit → fix → re-audit cycle | `DashboardController.RunP3AAuditAsync`, automatic re-audit after any fix that may have changed the computer, `IReadinessAuditor` / `P3AAuditAdapter` | `DashboardController.cs`, `StatusText.cs`, `src/P3B.Integration.P3A/*` | UI `ReauditFlowTests` (9), incl. `Problem_then_fix_then_P3A_reaudit_reports_Ready`, `Fix_that_P3A_still_rejects_stays_Not_Ready_…`, `If_the_reaudit_cannot_run_the_dashboard_asks_for_one_instead_of_guessing`; P3A `P3AAuditAdapterTests` (8) | **Pass (P3A, pending acceptance)** | Joint flow also run on the development machine against P3A's real audit (no fix applied). |

## 2. Project brief traceability

| ID | Component | Covering tests | Status | Notes |
|---|---|---|---|---|
| **P3-StR2.1** Install/configure approved OSS tools and user-level settings | `InstallationService` + `WingetInstaller` + `VsCodeExtensionInstaller`; `ConfigurationService` | Core `InstallationServiceTests` (15), `ConfigurationServiceTests` (13); Win `WingetInstallerTests.Full_install_through_installation_service_with_scripted_winget`, `VsCodeExtensionInstallerTests` (28) | **Partial** | Logic verified with doubles; no real install. `ConfigurationService` is not wired because P3A reports no misconfigured state. |
| **P3-AC2.1** Installs/configures correctly; changes clear and reversible | As above, plus journal and rollback | "Clear and reversible": 3B-DC-003 (Pass). "Installs correctly": none on real software | **Partial** | Needs the VM, install sources in the manifest, and staff test states. |
| **P3-StR2.2** Run approved post-fix checks; report result | `SmokeTestService` and handlers (incl. installed-version check for extensions) | Core `SmokeTestServiceTests` (11), `SmokeCheckHandlerTests` (12), `ManifestSmokeTestParsingTests` (15); Win `SmokeCheckTests` (13) | **Pass (provisional)** | P3A's GHDL smoke test stays with P3A, since P3B never installs GHDL. |
| **P3-AC2.2** Expected check result; Vivado not installed or licensed | Smoke tests; `ProhibitedSoftwarePolicy` | As above; Vivado tests in 3B-DC-004 | **Partial** | Vivado part **Pass**. Real-tool check needs the VM. |
| **P3-StR3.1 / P3-AC3.1** Each subproject reads the manifest on its own | `ManifestService` + `P3AManifestSchema` (P3B's own reader of P3A's file) | Core `P3AManifestSchemaTests` (16), `ManifestReadOnlyTests` (6); P3A `RealP3AManifestTests` (8) | **Pass** | P3B reads P3A's file with its own validating reader; P3A reads it with `EdaManifestLoader`. |
| **P3-StR4.1 / P3-AC4.1** Audit → fix → re-check cycle | P3A audit at start-up → issues → fix or manual steps → automatic P3A re-audit → P3A's Ready / Not Ready, kept apart from P3B's results | UI `ReauditFlowTests` (9); P3A `P3AAuditAdapterTests` (8), `P3AFindingMapperTests.Verdict_is_P3A_overall_result_and_every_component_is_kept` | **Pass (P3A, pending acceptance)** | The full cycle with a *real* fix needs the VM and an install source in the manifest. |

## 3. Requested verification tasks

| # | Task | Evidence | Result |
|---|---|---|---|
| 1 | Build the entire solution | `dotnet build P3B.slnx` | 0 warnings, 0 errors |
| 2 | Run all unit tests | `dotnet test P3B.slnx` | 413/413 passed |
| 3 | Manifest validation (both formats) | `P3AManifestSchemaTests` (16), `ManifestServiceTests` (16), `ManifestSmokeTestParsingTests` (15), `VsCodeExtensionManifestTests` (18), `RealP3AManifestTests` (8) | Passed |
| 4 | Supported, manual and unsupported scenarios | `OutcomeTests`, `RemediationPlannerTests`, `ManualGuidancePlannerTests`, `DashboardControllerTests`, `ReauditFlowTests.Manual_steps_…` | Passed |
| 5 | Installation failures | `InstallationServiceTests` (failure cases), `WingetInstallerTests`, `VsCodeExtensionInstallerTests.Failed_install_keeps_the_exit_code`, `…Exit_code_zero_without_the_version_listed_is_not_success` | Passed |
| 6 | User cancellation | `OutcomeTests.Cancellation_during_*`, `InstallationServiceTests.Cancelled_installation_…`, `ProcessRunnerTests.Cancellation_…`, `DashboardControllerTests.Cancelled_fix_is_labelled_cancelled` | Passed |
| 7 | Rollback and reversal information | `RollbackServiceTests` (10), `JsonFileReversalJournalTests` (6), `WingetUninstallHandlerTests` (5), `VsCodeExtensionInstallerTests.Reversal_*` (3) | Passed |
| 8 | Smoke-test failures | `SmokeTestServiceTests`, `SmokeCheckTests` | Passed |
| 9 | Operation timeouts | `ProcessRunnerTests.Timeout_…`, `WingetInstallerTests.Install_timeout_…`, `SmokeCheckTests.Version_command_timeout_is_timed_out` | Passed |
| 10 | Vivado never installed or licensed | Tests under 3B-DC-004 | Passed |
| 11 | Unapproved versions rejected | Tests under 3B-DC-005 | Passed |
| 12 | One operation at a time | Tests under P3B-PR-01 | Passed |
| 13 | Audit data without P3A | `IntegrationScenarioTests` (4), `ReauditFlowTests` (fake P3A) | Passed |
| 14 | P3A adapter: valid, incomplete and unknown-state findings | `P3AFindingMapperTests` (17) | Passed |
| 15 | Re-audit: problem → fix → Ready, and fix → still Not Ready | `ReauditFlowTests.Problem_then_fix_then_P3A_reaudit_reports_Ready`, `…Fix_that_P3A_still_rejects_stays_Not_Ready_…` | Passed |
| 16 | VS Code extension install, verify and reverse (fakes) | `VsCodeExtensionInstallerTests` (28), `ProcessRunnerTests.Environment_variables_are_set_and_removed_for_the_child_process_only` | Passed |
| PR | Performance | `PerformanceTrialTests` (2) and the real-window script; see `docs/evidence/` | 20/20 and 20/20 on the development machine |

## 4. Gap audit and remaining limitations

**Gap audit (11 November 2026).** Every SRS ID above and P3-StR2.1–4.1 were re-checked against the code.

| Gap | Action |
|---|---|
| VS Code extensions could not be installed (P3A detects an extension; P3B had no installer). | **Fixed:** `VsCodeExtensionInstaller` + `VsCodeExtensionUninstallHandler`, provisional method `vscodeExtension`. VS Code's `code.cmd` is **read, never run**: P3B runs `Code.exe` + `cli.js` with `ELECTRON_RUN_AS_NODE=1`, exactly as the script does, with no shell. |
| `ProcessRunner` could not set a variable for one child process. | **Fixed:** `ProcessRequest.Environment` (child process only; tested with the helper process). |
| Closing the window during P3A's audit told the user to press Cancel, which is disabled then. | **Fixed:** the message now says the audit changes nothing and will finish shortly. |
| After a declined fix, the result said "P3A's audit was run again", which was not true (3B-UR-002). | **Fixed:** the note now says nothing changed, so P3A was not re-run. Found while capturing the SDD screenshots. |
| Read-only text boxes (results, approval details) showed all their text highlighted when focused. | **Fixed:** the selection is cleared on focus; the approval dialog's default focus is now "Do not change anything", so Enter never approves by accident (3B-DC-003). |
| Official-archive installer (3B-IR-004, 3B-DC-002). | **Not built, by rule:** neither P3A's manifest nor the provisional format has a SHA-256 field, and archives may only be installed after a checksum is verified. Needs staff to supply approved archive URLs *and* checksums (blocker 12). |
| Misconfigured states (P3-StR2.1). | **None to wire:** P3A's audit reports no misconfigured state. |

**Still needing staff, the group or a VM:**
1. **Install sources and exact versions** in the manifest (package IDs, extension versions, archive URLs + SHA-256). Until then nothing can be installed automatically.
2. **No real installation or reversal has been run** (3B-DC-006, P3-AC2.1, P3-AC2.2): needs the resettable VM.
3. **VS Code extension auto-update** (3B-DC-005): VS Code may update an extension later. Whether `--install-extension id@version` stops auto-update must be confirmed on the VM; P3A's re-audit and P3B's installed-version check would detect a change.
4. **P3A acceptance** of the audit library ([P3A-CHANGES.md](P3A-CHANGES.md)) and where the joint code lives.
5. **3B-UR-004 usability study** (§5).
6. **Reference machine** for PR-02/PR-03.
7. **WinGet limits:** per-user only (blocker 8); a truncated package ID in `winget list` blocks the fix safely; source agreements are never accepted for the user.
8. **Vivado detection uses name markers** ("vivado", "xilinx"). P3A's manifest names it recognisably.
9. **Journal location** (blocker 7).

The SRS ID typo "3A-DC-003" (for 3B-DC-003) was searched for in every P3B document, the test-plan generator and the shared verification page; it does not appear in any of them.

Full list of blockers: `docs/BLOCKERS.md`.

## 5. Running and re-verifying

**Requirements:** Windows 10/11, .NET SDK 10 (`global.json`), the group repository on branch `p3-integrated` (P3B is its `P3B/` folder), and WinGet / VS Code for real installs.

```powershell
dotnet build P3B.slnx                       # expect 0 warnings, 0 errors
dotnet test  P3B.slnx                       # expect 413 passed; installs nothing
```

**Joint audit → fix → re-audit flow:**

```powershell
dotnet run --project src\P3B.Remediation.WinForms
```

The dashboard opens P3A's manifest, runs P3A's audit and shows P3A's verdict. Select an issue: *Manual steps* issues show P3A's steps and **I've done this - check again**; *Supported* issues (provisional manifest with an install source) show **Fix selected issue...**, after which P3A's audit runs again automatically.

- **Approving a supported fix with a real manifest runs a real per-user install.** Do that only in the resettable VM.

**Performance trials (P3B-PR-02/03),** on the agreed reference machine:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Measure-UiResponsiveness.ps1 -Trials 20 -OutputCsv trials.csv
```

**Usability study (3B-UR-004):** five representative students, each unaided.
1. Start the dashboard; P3A's audit runs.
2. Select an issue and open its details or the approval screen.
3. The student states the proposed change and how it would be reversed, then declines.
4. Repeat with a run that completes (in the VM). The student states P3B's outcome and P3A's verdict, and that only P3A's audit decides Ready.

Pass if at least 4 of 5 students answer all three correctly.
