WIth smok test for ghdl and manual remediation guidance for Surfer and Vivado

## Repository layout (branch `p3-integrated`)

| Folder | What it is |
|---|---|
| `/` (root) | **P3A – Environment Audit & Readiness**: the audit window (`EDAEnvironmentManager.csproj`) and `eda-tool-manifest.json`. |
| `EDAEnvironmentManager.Audit/` | P3A's audit checks as a library, used by the P3A window and by P3B (see `P3B/docs/P3A-CHANGES.md`). |
| `P3B/` | **P3B – Installation & Remediation**: the remediation dashboard, its tests and documents (`P3B/README.md`, `P3B/docs/`). |

## Build (needs the .NET 10 SDK)

```powershell
dotnet build EDAEnvironmentManager.slnx      # P3A
cd P3B
dotnet build P3B.slnx                         # P3B (0 warnings expected)
dotnet test P3B.slnx                          # 413 tests; installs nothing
dotnet run --project src\P3B.Remediation.WinForms   # dashboard: runs P3A's audit, then offers fixes
```
