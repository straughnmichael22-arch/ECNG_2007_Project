WIth smok test for ghdl and manual remediation guidance for Surfer and Vivado

# Windows EDA Environment Manager – Project 3 (P3A + P3B)

> **The current code is on the branch `p3-integrated`, not `main`.**
> `main` still has the older P3A code and no P3B.

## How to run it

**1. Install the .NET 10 SDK (one time).** Windows 10/11 only.
Download the **SDK** (not "Runtime") for Windows x64: https://dotnet.microsoft.com/download/dotnet/10.0
Check in PowerShell: `dotnet --list-sdks` must show a line starting with `10.0`.

**2. Get the code – choose ONE:**
- **ZIP (no Git needed):** https://github.com/straughnmichael22-arch/ECNG_2007_Project/archive/refs/heads/p3-integrated.zip
  Extract it. Use the folder `ECNG_2007_Project-p3-integrated` (the one that contains `EDAEnvironmentManager.slnx` and the `P3B` folder).
- **Git:** `git clone -b p3-integrated https://github.com/straughnmichael22-arch/ECNG_2007_Project.git`

**3. Open PowerShell in that folder** (in File Explorer: open the folder, click the address bar, type `powershell`, press Enter).

**4. Run P3A (Environment Audit):**
```powershell
dotnet build EDAEnvironmentManager.slnx
dotnet run --project EDAEnvironmentManager.csproj
```

**5. Run P3B (Installation & Remediation dashboard):**
```powershell
cd P3B
dotnet build P3B.slnx
dotnet run --project src\P3B.Remediation.WinForms
```
It opens P3A's manifest and runs P3A's audit automatically. It installs nothing unless you press **Approve** on a supported fix (with the current manifest there are none).

**6. Run the automated tests (optional, from the main folder):**
```powershell
dotnet test EDAEnvironmentManager.slnx     # P3A: 23 tests
dotnet test P3B\P3B.slnx                   # P3B: 413 tests
```
No test installs or changes anything.

## If something goes wrong

| Message | Fix |
|---|---|
| `MSB1011: ... more than one project or solution file` | You ran `dotnet build` without a file name. Use the exact commands above. |
| `NETSDK1045` / "does not support targeting .NET 10.0" / `dotnet` not recognised | Install the .NET 10 **SDK** (step 1), then open a **new** PowerShell window. |
| `Couldn't find a project to run` | You are in the wrong folder. Go to the folder that contains `EDAEnvironmentManager.slnx`. |
| No `P3B` folder / no `EDAEnvironmentManager.Audit` folder | You downloaded `main`. Use the `p3-integrated` link in step 2. |
| Visual Studio can't open the solution | Use the terminal commands above, or Visual Studio 2026 (needed for .NET 10 and `.slnx`). |

## Repository layout

| Folder | What it is |
|---|---|
| `/` (main folder) | **P3A – Environment Audit & Readiness** app (`EDAEnvironmentManager.csproj`) and `eda-tool-manifest.json`. |
| `EDAEnvironmentManager.Audit/` | P3A's audit checks as a library, used by the P3A window and by P3B. |
| `EDAEnvironmentManager.Audit.Tests/` | P3A's automated tests. |
| `P3B/` | **P3B – Installation & Remediation** dashboard, tests and documents (`P3B/README.md`, `P3B/docs/`). |
