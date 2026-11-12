# PROVISIONAL – P3B proposal for the Local EDA Tool Manifest

> **Status (updated): superseded for the group's tools; kept for automatic installs only.**
>
> **Agreed:** the group's manifest is P3A's `eda-tool-manifest.json` (`manifestVersion` "1.0").
> P3B reads it with `P3AManifestSchema`. Mapping: `tools[].type` → tool identity,
> `name` → display name, `minimumApprovedVersion` / `maximumApprovedVersion` → approved range,
> `remediation.downloadUrl` + `instructions` → manual steps shown in the dashboard,
> `extensions[]` → extension groups. See [INTEGRATION-SEAMS.md](../INTEGRATION-SEAMS.md).
>
> **Still provisional (this document):** P3A's format has no exact version to install and no
> package identifier, so it cannot describe an automatic install. The fields below
> (`approvedVersions`, `remediation.method` = `winget` + `source`, `smokeTests`) remain P3B's
> proposal for what P3A's manifest would need to add, and the `p3b-provisional-0` format is kept
> for tests. It contains no real tool IDs, versions or installation sources.

Implemented by `ProvisionalManifestSchema` and `ManifestService` in `src/P3B.Remediation.Core/Manifest/`.

## Format

- UTF-8 JSON (a byte-order mark is tolerated). Comments and trailing commas are not allowed.
- Property names are case-sensitive.
- **Properties P3B does not use are ignored**, so P3A can add detection data without breaking P3B.
- The file is only ever opened for reading, with read sharing (P3B-DR-02).

## Fields P3B reads

Each field is traced to the text that implies the manifest must carry it.

| Field | Required | Meaning | Source in course documents |
|---|---|---|---|
| `manifestVersion` | yes | Must equal `p3b-provisional-0`. Any other value → *unsupported version*, so a real v1 file is never misread. | Manifest is versioned ("v1") |
| `tools` | yes, ≥ 1 | Tool definitions. | Brief: "which tools are supported" |
| `tools[].id` | yes, unique | Tool identity, matched against P3A's `toolId`. | 3B-IR-001 "supported tool identities" |
| `tools[].displayName` | no | Name shown to users. | — (UI convenience) |
| `tools[].approvedVersions` | yes, ≥ 1 | Course-approved versions. **Proposed rule:** the first entry is the version P3B installs. | Brief: "which versions are approved"; 3B-IR-001 "approved versions" |
| `tools[].remediation.method` | yes | `winget`, `vscodeExtension`, `officialArchive`, `linkToInstaller` or `none`. | 3B-IR-004 / 3B-DC-002 (WinGet, official archives); P3B brief ("linked to their normal installers") |
| `tools[].remediation.source` | unless method is `none` | WinGet package ID, VS Code extension ID (`publisher.name`), archive URL or installer page, used verbatim. | 3B-IR-004 "staff-approved source"; brief "where/how they should be … invoked" |
| `tools[].smokeTests` | no | Post-remediation checks for this tool, run in order (see below). If absent, verification is reported as *Unsupported*. | 3B-IR-001 "locations and invocation metadata"; P3-StR2.2 "approved post-fix checks" |

Not proposed yet (left for the real schema): detection data (P3A), archive checksums, install scope.

## `vscodeExtension` method (provisional)

Added so the course VHDL extension P3A checks for can be installed automatically once an exact
approved version is known. P3A's manifest lists approved extension IDs but no version, so it is
not used with P3A's manifest yet.

```json
{ "id": "TEST-EXTENSION", "displayName": "Test extension", "approvedVersions": [ "1.2.3" ],
  "remediation": { "method": "vscodeExtension", "source": "test-publisher.test-extension" },
  "smokeTests": [ { "kind": "installedVersion" } ] }
```

- `source` must be `publisher.name` (letters, digits, hyphens); anything else makes the manifest invalid.
- P3B installs exactly `source@approvedVersions[0]` for the current user's VS Code profile, without `--force`, and
  reports success only after `--list-extensions --show-versions` shows that version.
- VS Code's `code.cmd` is never run. P3B reads it, and if it has the layout VS Code ships, runs the `Code.exe`
  and `cli.js` it names (both inside the VS Code folder) with `ELECTRON_RUN_AS_NODE=1`, as the script would.
- Reversal: automatic `--uninstall-extension` when P3B added the extension; manual-only when it replaced another version.
- An `officialArchive` entry would also need a SHA-256 field. There is none, so archive installs are not implemented.

## `smokeTests` (provisional)

Each tool declares only the checks that apply to it. P3B does not assume every tool supports
the same check.

| `kind` | Fields | Passes when |
|---|---|---|
| `installedVersion` | — | The installer (WinGet) reports the approved version as installed. |
| `commandOnPath` | `command` | The command is found on the user or system PATH, read fresh from Windows. |
| `fileExists` | `path` (absolute; may start with e.g. `%LOCALAPPDATA%`) | The file exists. |
| `versionCommand` | `command`, optional `arguments` (strings), optional `timeoutSeconds` (1–300, default 30) | The `.exe` starts, exits with 0, and its output shows the approved version as a whole version (`1.2` does not match `1.20` or `11.2`). Scripts (`.cmd`/`.bat`) are never run. |
| `userEnvironmentVariable` | `name`, `contains` | The user-level variable contains the text (case-insensitive). |

- `command` must be a bare name (`tool`, `tool.exe`) or an absolute path. Relative paths, quotes and shell characters are rejected.
- An unrecognised `kind` is not an error. It is reported as *Unsupported* when run, never guessed at.

```json
"smokeTests": [
  { "kind": "installedVersion" },
  { "kind": "commandOnPath", "command": "TEST-tool" },
  { "kind": "versionCommand", "command": "TEST-tool.exe", "arguments": [ "--version" ], "timeoutSeconds": 20 }
]
```

## Example (fake values)

```json
{
  "manifestVersion": "p3b-provisional-0",
  "tools": [
    {
      "id": "TEST-TOOL-A",
      "displayName": "Test tool A",
      "approvedVersions": [ "TEST-VERSION-1" ],
      "remediation": { "method": "winget", "source": "TEST-SOURCE-A" }
    },
    {
      "id": "TEST-TOOL-NO-FIX",
      "approvedVersions": [ "TEST-VERSION-1" ],
      "remediation": { "method": "none" }
    }
  ]
}
```

## Load outcomes

| Status | When |
|---|---|
| `Loaded` | Valid. |
| `NotConfigured` | No manifest location configured. |
| `Missing` | No file at the configured path. |
| `Unreadable` | File exists but cannot be read (locked, access denied, I/O error). |
| `Malformed` | Not well-formed JSON. |
| `UnsupportedVersion` | `manifestVersion` is not `p3b-provisional-0`. |
| `Invalid` | Required fields missing or wrong; every problem is listed with its JSON path, e.g. `$.tools[0].id`. |

## Questions for P3A and staff

1. Will staff supply a schema file (JSON Schema?) with v1, and is the format JSON at all?
2. Does v1 contain installation sources and WinGet IDs, or only detection information?
3. With several approved versions, which one should P3B install?
4. How does the manifest mark Vivado and VS Code as "link to installer only"?
5. Are smoke tests defined in the manifest? If so, does the `smokeTests` proposal above cover them, and what are each tool's real version command and expected output?
