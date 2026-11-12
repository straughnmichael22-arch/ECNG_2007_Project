# P3B – Installation & Remediation (Windows EDA Environment Manager)

ECNG2007 Project 3, subproject B. Pairs with P3A (Environment Audit & Readiness).

> **Integrated with P3A.** P3B reads P3A's real `eda-tool-manifest.json` and runs P3A's own
> audit in-process (through a proposed `EDAEnvironmentManager.Audit` library, not yet accepted
> by the P3A team: [docs/P3A-CHANGES.md](docs/P3A-CHANGES.md)). The dashboard's Ready / Not
> Ready is always P3A's verdict. P3A's manifest gives no install sources, so with it P3B offers
> **manual steps** (Surfer, Vivado) or **no automatic fix** (VS Code, GHDL, extensions); the
> automatic WinGet path is implemented and tested with fakes against a provisional format.
> What is agreed and what is still provisional: [docs/INTEGRATION-SEAMS.md](docs/INTEGRATION-SEAMS.md),
> [docs/BLOCKERS.md](docs/BLOCKERS.md).

## Layout

| Path | Purpose |
|---|---|
| `src/P3B.Remediation.Core` | UI-free decision logic (`net10.0`): planning, approval, `InstallationService`, `ConfigurationService`, `RollbackService`, reversal journal. Cannot start processes or touch the registry. |
| `src/P3B.Remediation.Windows` | Windows integrations (`net10.0-windows`): shell-free `ProcessRunner`, `WingetInstaller`, `WingetUninstallHandler`, `VsCodeExtensionInstaller` and its uninstall handler, user-only `UserEnvironment`. |
| `src/P3B.Integration.P3A` | The P3A adapter: runs P3A's audit (`P3AAuditAdapter`) and converts its findings (`P3AFindingMapper`). References P3A's audit library in the same repository. |
| `tests/P3B.Integration.P3A.Tests` | Adapter tests with a fake P3A audit engine, plus read-only tests of P3A's real manifest. |
| `src/P3B.Remediation.WinForms` | Desktop dashboard (`net10.0-windows`). `Presentation/DashboardController` holds the workflow without any Forms code; `MainForm` and `ApprovalDialog` are the screens. |
| `tests/P3B.Remediation.WinForms.Tests` | Dashboard workflow tests using the real services with fake executors. |
| `tests/P3B.Remediation.Core.Tests` | xUnit tests using in-memory fakes only. |
| `tests/P3B.Remediation.Windows.Tests` | xUnit tests: WinGet logic against a scripted process runner; `ProcessRunner` against the harmless `P3B.TestProcess` helper. |
| `tests/.../Fixtures` | **Test-only** data. Never referenced by the app; not a manifest. |

## Safety rules (enforced in code and tests)

- Nothing is installed or changed without an approval of the exact preview shown.
- Vivado is never installed, activated or licensed, whatever the manifest says.
- Only the manifest's approved target version is installed; newer unapproved versions are never adopted.
- VS Code extensions (provisional `vscodeExtension` method) are installed as exactly `id@version` for the current user, through `Code.exe` started without a shell; VS Code's `code.cmd` is only read. No `--force`.
- Official archives are not installed: no manifest supplies a SHA-256 checksum.
- WinGet runs per-user only (`--scope user`), from the `winget` source only, never with `--force`/`--override`, and never accepts source agreements on the user's behalf. Its availability is checked first.
- Processes run without a shell, with arguments passed individually, exit code / stdout / stderr captured, and a timeout.
- Success is reported only after the target version is verified as installed; an installer's exit code alone never counts.
- Smoke tests run only the checks the manifest declares for each tool. Their results are reported separately from the remediation outcome, explain every failure, and never claim the environment is Ready: that is P3A's re-audit.
- Only user-level environment variables can be changed; there is no machine-scope code.
- One remediation or rollback at a time. Reversal information is journalled and kept until reversed or explicitly discarded after a warning. Only reliably reversible changes are reversed automatically.

## Build and test

Requires the .NET 10 SDK (pinned in `global.json`). P3B lives in the `P3B/` folder of the group
repository, next to P3A's code and its audit library, on the branch `p3-integrated`:

```powershell
git clone -b p3-integrated https://github.com/straughnmichael22-arch/ECNG_2007_Project.git
cd ECNG_2007_Project\P3B
dotnet build P3B.slnx
dotnet test P3B.slnx
```

P3A's library is found automatically there (or in a clone named `ECNG_2007_Project` next to a
separate P3B folder). Elsewhere: `dotnet build P3B.slnx -p:P3ARepoRoot=<path>`.

The tests never run WinGet, install software, or touch the registry, user environment or
network. The only real process they start is the harmless `P3B.TestProcess` helper.

## Running the dashboard

```powershell
dotnet run --project src\P3B.Remediation.WinForms
```

With no arguments the dashboard opens P3A's `eda-tool-manifest.json` (copied next to the app)
and runs P3A's audit. Optional: `--manifest <file>`, `--audit <audit-results.json>` (load a
file instead of running P3A's audit), `--data-folder <folder>` (journal and results somewhere
other than `%LOCALAPPDATA%\P3B`).

Verification status, traceability and limitations: [docs/VERIFICATION-REPORT.md](docs/VERIFICATION-REPORT.md).
Design: `docs/P3B-SDD.docx` (Software Design Document). Tests: `docs/P3B-Test-Plan.docx` (25 pages, 125 test cases).

1. **P3A audit.** The box at the top shows *P3A overall environment: Ready / Not Ready*, exactly as P3A's audit reports it, and the **P3A audit (all components)** tab lists every component. **Run P3A audit** repeats it at any time.
2. **Issues.** Each non-ready component is listed and marked **Supported**, **Manual steps**, **Not supported**, **Incomplete audit data** or **Manifest needed**.
3. **Manual steps** (for example Surfer or Vivado): the details show the manifest's numbered steps; **Open download page** opens the manifest's official page; after following them, **I've done this - check again** runs P3A's audit again.
4. **Supported** issues (only with a manifest that has an install source): **Fix selected issue...** shows the approval screen (tool, target version, changes, reversal). Progress is shown while the window stays usable; **Cancel** stops the installer and records partial changes.
5. After any fix that may have changed the computer, P3B runs **P3A's audit again automatically**. P3B's own result stays on the **P3B remediation result** and **P3B smoke tests** tabs, and in the **P3B status** / **P3B verification** columns; it is never turned into Ready / Not Ready. If the re-audit cannot run, the banner says *Re-audit needed*.
6. Reversals and discards both ask for confirmation. Results are also saved automatically to `latest-remediation-results.json`.

Files are kept in `%LOCALAPPDATA%\P3B` (`reversal-journal.json`, `latest-remediation-results.json`), set in `AppPaths`.

**Audit results file (provisional):** `{ "issues": [ { "issueId", "toolId", "state", "detectedVersion", "detail" } ] }`, where `state` is `missing`, `unapprovedVersion` or `misconfigured`.

## Decisions

- Target framework: .NET 10 (LTS), as P3A.
- P3A integration: in-process, through P3A's own audit code; issue IDs `"{type}/{finding}"`.
- Test framework: xUnit.
- Concurrency (P3B-PR-01): a request made while an action is active is rejected as `Busy`, not queued.
