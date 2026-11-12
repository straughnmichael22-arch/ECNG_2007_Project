# Blockers – information needed from staff or the group

Nothing below may be guessed. Each item blocks the listed work. Items marked **Resolved** are
kept so the history stays visible.

| # | Missing information | Owner | Status / blocks |
|---|---|---|---|
| 1 | Local EDA Tool Manifest | P3A / staff | **Resolved for reading:** P3B reads P3A's `eda-tool-manifest.json` (format 1.0). Still open: whether staff will issue a different "Manifest v1". |
| 2 | Approved tool versions | P3A (via manifest) | **Resolved:** P3A's manifest gives approved ranges, shown in the dashboard. It gives no single version to install, which feeds #3. |
| 3 | Which open-source tools P3B may install, and from which sources (WinGet IDs / official archive URLs) and exact versions | Staff + P3A | **Open.** P3A's manifest has no install sources, so **no automatic install is possible**: GHDL and VS Code show "No supported automatic fix", Surfer shows its manual steps. `WingetInstaller` is ready once the manifest adds a package ID and an exact version. |
| 4 | Shared P3A/P3B tool-state/remediation contract | P3A + P3B | **Agreed in code, pending P3A acceptance:** finding codes and `"{type}/{finding}"` IDs from the proposed `EDAEnvironmentManager.Audit` library. See [P3A-CHANGES.md](P3A-CHANGES.md). |
| 5 | Real smoke-test definitions per tool | Staff / P3A | **Partly resolved:** P3A defines the GHDL smoke test and runs it itself. P3B's smoke tests only matter after an automatic install, which #3 blocks. |
| 6 | Staff audit-state fixtures and prescribed remediation/install test states | Staff | **Open.** Fixture-based verification (3B-IR-005). |
| 7 | Where the reversal journal file is stored | Group | **Open.** Defaults to `%LOCALAPPDATA%\P3B\reversal-journal.json`, set in `AppPaths`. |
| 8 | Confirmation that per-user installs only (`--scope user`) are acceptable | Group / staff | **Open.** Implemented as user-only. |
| 9 | Resettable Windows test environment | Staff | **Open.** No real installation has been run (3B-DC-006). |
| 10 | "Agreed reference Windows machine" | Group / staff | **Open.** Timing trials (P3B-PR-02, P3B-PR-03). |
| 11 | Confirmation that P3A also targets .NET 10 | P3A | **Resolved:** P3A targets `net10.0-windows`. |
| 12 | Approved archive sources and integrity data (SHA-256) | Staff | **Open.** Neither manifest format has a checksum field, so the `OfficialArchive` installer is deliberately **not built**: archives may only be installed after their SHA-256 is verified. |
| 13 | Which configuration fixes are supported for "misconfigured" tools | Staff / P3A | **Resolved for now:** P3A reports no misconfigured state, so `ConfigurationService` stays unwired and any such finding is "No supported automatic fix". Reopen if P3A adds one. |
| 14 | P3A team to accept the audit-library extraction | P3A | **New, open.** The library and P3B are on the branch `p3-integrated` of the group repository, waiting for the P3A team to review and merge. See [P3A-CHANGES.md](P3A-CHANGES.md). |
| 16 | Exact approved version of each VS Code extension | P3A / staff | **New, open.** P3A's manifest lists approved extension IDs but no version, so P3B shows manual guidance. The `VsCodeExtensionInstaller` (provisional method `vscodeExtension`) is ready once an exact version is given. |
| 17 | Whether VS Code auto-updates an extension installed as `id@version` | VM test | **New, open.** Affects 3B-DC-005 after installation; to be checked on the resettable VM. |
| 15 | Where the integrated code lives (one repository or two) | Group | **Resolved:** P3B is the `P3B/` folder of the group repository on branch `p3-integrated`, next to P3A and its audit library. |
