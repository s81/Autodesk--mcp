# Autodesk MCP Bridge — Revit & AutoCAD 2022–2027

A [Model Context Protocol](https://modelcontextprotocol.io) server that lets MCP clients (Claude Desktop,
Claude Code, etc.) read and edit **live** Revit and AutoCAD sessions on the same Windows computer.

**User manual (English and Arabic):** [Autodesk MCP User Manual](https://claude.ai/artifact/KBaMnZaeiadpGxVjJMFYcD).
It covers everyday use, example requests and troubleshooting for Revit/AutoCAD users, and installation and
settings for IT staff.

| Product | Years | Add-in target | API reference package |
|---|---|---|---|
| Revit | 2022, 2023, 2024 | `net48` (.NET Framework 4.8) | `Nice3point.Revit.Api.*` 2022.1.80 / 2023.1.90 / 2024.3.60 |
| Revit | 2025, 2026 | `net8.0-windows` | `Nice3point.Revit.Api.*` 2025.4.60 / 2026.4.10 |
| Revit | 2027 | `net10.0-windows` | `Nice3point.Revit.Api.*` 2027.3.0 |
| AutoCAD (+ verticals: Civil 3D, Plant 3D, Advance Steel…) | 2022 (R24.1), 2023 (R24.2), 2024 (R24.3) | `net48` (.NET Framework 4.8) | `AutoCAD.NET` 24.1.51000 / 24.2.0 / 24.3.0 (official) |
| AutoCAD | 2025 (R25.0), 2026 (R25.1) | `net8.0-windows` | `AutoCAD.NET` 25.0.1 / 25.1.0 (official) |
| AutoCAD | 2027 (R26.0) | `net10.0-windows` | `AutoCAD.NET` 26.0.0 (official) |

AutoCAD LT is not supported because it has no .NET API.

The 2022–2024 add-ins ship **no extra DLLs**: .NET Framework 4.8 has no built-in JSON support, so the bridge
includes its own small JSON reader/writer (`AutodeskMcp.Bridge.Json`) instead of System.Text.Json, which would
add about seven DLLs that can clash with other add-ins loaded in the same Revit/AutoCAD process.

## Architecture

```
 MCP client ──stdio──► AutodeskMcp.Server.exe ──named pipe (per user, token)──► Revit add-in   ─ExternalEvent─► Revit API
 (Claude)              (.NET 10, MCP C# SDK,                                 └► AutoCAD add-in ─main thread──► AutoCAD API
                        Roslyn compiler)
```

* **Add-in in each app (`AutodeskMcp.Revit` / `AutodeskMcp.AutoCAD`).** Revit and AutoCAD APIs only work
  inside the app, on its main thread. When the app starts, the add-in opens a named pipe and writes a
  discovery file to `%LOCALAPPDATA%\AutodeskMcp\instances\`. Requests are marshalled to the main thread:
  Revit uses `ExternalEvent`; AutoCAD posts to a window created on the main thread. If a request has not
  **started** within the timeout (for example because a modal dialog is open), it is cancelled and is
  guaranteed never to run later.
* **MCP server (`AutodeskMcp.Server`).** Clients launch it over stdio. It finds the running instances,
  forwards tool calls to them, and returns JSON. It keeps running while you close and reopen Revit or
  AutoCAD.
* **C# execution without Roslyn inside the host.** `*_execute_csharp` snippets are compiled *in the server
  process* against the exact assemblies the host has loaded. Only the finished IL is sent to the add-in.
  Because Roslyn never loads inside Revit or AutoCAD, it can't conflict with versions shipped by the host or
  other add-ins. On 2025–2027 the script is loaded into a collectible `AssemblyLoadContext` and unloaded
  afterwards. .NET Framework 4.8 (2022–2024) cannot unload assemblies, so there each executed script stays in
  memory (a few KB) until Revit/AutoCAD is closed.
* **Shared `AutodeskMcp.Bridge` library** (net48 + net8.0, no dependencies): pipe framing, discovery,
  policy, main-thread work items, safe JSON conversion.

## Tools

| Tool | Description |
|---|---|
| `list_autodesk_instances` | Running Revit/AutoCAD sessions with the add-in, plus their policy flags |
| **Revit** | |
| `revit_get_session_info` | Version, open documents, active view, project length unit, selection count |
| `revit_list_elements` | Filter by category (`OST_Walls` or the UI name), class, view, type/instance, name; paged |
| `revit_get_elements` | Instance/type parameters (display value + raw value + storage type), location, bounding box |
| `revit_get_selection` / `revit_set_selection` | Read or set the UI selection |
| `revit_list_views` / `revit_list_levels` | Views and sheets; levels with elevations |
| `revit_get_schedule_data` | Schedule header and body cells, as displayed |
| `revit_set_parameters` | Batch parameter edits in one transaction; all apply or none do |
| `revit_create_walls` | Straight walls; **explicit `units` required** (mm/cm/m/ft/in) |
| `revit_delete_elements` | Delete (reports dependents that Revit also deleted) |
| `revit_execute_csharp` | Run a C# method body with `uiapp`, `app`, `uidoc`, `doc`, `output`; optional auto-transaction |
| **AutoCAD** | |
| `autocad_get_session_info` | Version, drawings, INSUNITS/LUNITS, current layer, extents, layouts, busy state |
| `autocad_list_layers` / `autocad_create_layer` | Read layers; create a layer or update one |
| `autocad_list_entities` / `autocad_get_entities` | Model space or a layout; filter by DXF type and layer; geometry, attributes, dynamic properties |
| `autocad_create_entities` | line, polyline, circle, arc, point, text, mtext |
| `autocad_modify_entities` | Change layer/color, move, rotate, scale, delete |
| `autocad_list_blocks` / `autocad_insert_block` | Block definitions; insert with attribute values |
| `autocad_send_command` | Queue a command-line string (runs asynchronously) |
| `autocad_execute_csharp` | Run a C# method body with `doc`, `db`, `ed`, `tr`, `output`; commit or abort |

Every modifying tool is a single Undo step. Revit warnings are collected and returned instead of opening
dialogs that would block Revit. Revit errors roll the transaction back with a readable message. AutoCAD
writes are refused while a user command is running.

## Build

You need the **.NET 10 SDK**; it builds every target (net48, net8.0, net10.0). You don't need Revit or
AutoCAD installed to build.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

On Linux, macOS or CI, `scripts/build.sh` does the same and also writes per-version zips to `dist/`:

```bash
scripts/build.sh
```

Or build a single target:

```bash
dotnet build src/AutodeskMcp.Revit   -c Release -p:RevitVersion=2026
```

```bash
dotnet build src/AutodeskMcp.AutoCAD -c Release -p:AutoCADVersion=2027
```

```bash
dotnet test --project tests/AutodeskMcp.Tests
```

## CI

`.github/workflows/ci.yml` runs on every pull request and on every push to `main`:
* tests on Linux and Windows; on Windows they also run on .NET Framework 4.8, and a separate .NET Framework
  4.8 process plays the 2022–2024 add-in against the real .NET 10 client and MCP server;
* builds all twelve add-ins (Revit and AutoCAD × 2022–2027) with warnings treated as errors;
* checks that the self-contained Windows server still publishes.

To block merging when CI fails, make the single **CI result** check required (Settings → Branches →
branch protection for `main`).

## Release

`.github/workflows/release.yml` runs the tests, builds every version, and publishes a GitHub release with
the per-version zips and `SHA256SUMS.txt`:

```bash
git tag v0.1.0
```

```bash
git push origin v0.1.0
```

* The tag must match `<Version>` in `Directory.Build.props`, otherwise the workflow fails. Bump that
  version first for a new release.
* A tag with a suffix (`v0.2.0-beta.1`) becomes a pre-release.
* You can also start it manually: Actions → Release → Run workflow. There you can choose pre-release
  and draft.

## Install (Windows, per user, no admin rights)

1. Download `AutodeskMcp-All-v<version>.zip` from the latest release and **extract the whole zip** to a
   folder (don't run anything from inside the zip).
2. Open **Windows PowerShell** in the extracted folder and run the installer. It detects your installed
   Revit/AutoCAD years. `-ExecutionPolicy Bypass` is needed because Windows blocks downloaded scripts by
   default; it applies to this one command only.
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install.ps1
   ```
   To install only one product, add `-SkipRevit` or `-SkipAutoCAD`. To pick a year instead of the
   detected ones, add `-RevitYears 2026` (one year per run: with `-File`, values such as `2025,2026`
   or `@()` are passed as plain text, not as a list):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -RevitYears 2026 -SkipAutoCAD
   ```
3. Register the server with Claude (below), then **fully quit Claude** (taskbar tray icon → Quit) and
   restart Revit/AutoCAD. If Claude still doesn't show the server, restart the PC.

Claude Desktop: Settings → Developer → **Edit Config** opens `claude_desktop_config.json`. Add (keep the
double backslashes):

```json
{
  "mcpServers": {
    "autodesk": { "command": "C:\\Users\\<you>\\AppData\\Local\\AutodeskMcp\\server\\AutodeskMcp.Server.exe" }
  }
}
```

Claude Code:

```bash
claude mcp add autodesk -- "C:\Users\<you>\AppData\Local\AutodeskMcp\server\AutodeskMcp.Server.exe"
```

### Use it in Claude

Only the **Claude Desktop app** (or Claude Code) on the same PC can start the server; the claude.ai website
and mobile apps cannot. There is nothing to launch by hand: open Revit or AutoCAD with a project/drawing,
then ask Claude in plain words, for example *"List my running Autodesk instances"* or *"How many walls are
in the active Revit model?"*, and click **Allow** when Claude asks to use the tool. If Claude answers
*"No running revit instance … was found"*, Claude works but the add-in did not load: check
`%LOCALAPPDATA%\AutodeskMcp\logs\`.

Where files go:
* Revit: `%APPDATA%\Autodesk\Revit\Addins\<year>\AutodeskMcp.addin` + `AutodeskMcp\`. Revit 2027 no
  longer reads `C:\ProgramData\Autodesk\Revit\Addins`. Per-user folders still work.
* AutoCAD: `%APPDATA%\Autodesk\ApplicationPlugins\AutodeskMcp.bundle`. One bundle serves 2022–2027. If SECURELOAD shows a warning, add the bundle folder to `TRUSTEDPATHS` or choose *Always load*.

## Security and policy

* The pipe only accepts the current Windows user (`PipeOptions.CurrentUserOnly` on 2025–2027; the same
  access list built by hand on 2022–2024, granting read/write only). Run Claude and Revit/AutoCAD at the
  same permission level: if one is started with *Run as administrator* and the other is not, Windows
  treats them as different owners and the connection is refused. When Revit/AutoCAD runs as administrator,
  Windows makes the *Administrators* group the owner, so elevated programs of other administrator accounts
  on the same PC also pass the pipe check (they still need the token). Administrators can already read
  and control every process on the PC, so this gives them nothing new; avoid running elevated on shared PCs
  if that matters to you. Each
  request must also carry a random 256-bit token. The token is stored only in your profile's discovery
  file.
* Nothing listens on the network.
* Policy lives in `%LOCALAPPDATA%\AutodeskMcp\config.json` (see `deploy/config.example.json`). The add-in
  enforces it, so an MCP client can't bypass it:
  ```json
  { "enabled": true, "allowModifications": true, "allowCodeExecution": true, "mainThreadTimeoutSeconds": 120 }
  ```
  Set `allowCodeExecution: false` to turn off the `*_execute_csharp` tools, and `allowModifications: false`
  for read-only use. Restart the host app after changing the file.
* **`*_execute_csharp` runs arbitrary code with your user rights inside Revit/AutoCAD.** Only connect
  MCP clients you trust, and review the code your client asks to run. Most clients show tool arguments
  before you approve them.
* Logs: `%LOCALAPPDATA%\AutodeskMcp\logs\`.

## Status: what has and hasn't been verified

Verified automatically on every pull request (GitHub Actions, Linux and Windows):
* All 12 add-in variants (Revit/AutoCAD × 2022–2027) compile with warnings treated as errors against the
  official/reference API assemblies, and the server publishes.
* The tests cover pipe transport, token authentication, policy enforcement, timeout-cancellation,
  stale-instance pruning, out-of-process compilation and loading of scripts (including error line mapping),
  the built-in JSON (including 500 random documents checked against System.Text.Json), and end-to-end MCP
  stdio sessions. On Windows they also run on .NET Framework 4.8, and a separate .NET Framework 4.8 host
  process is driven by the real .NET 10 client and MCP server, including a C# script compiled against that
  host's assemblies.

**Not verified automatically:** running inside actual Revit or AutoCAD (2022–2024 are new in 0.2.0). The host-specific handlers
(`RevitCommands`, `AcadCommands`) compile against the real APIs but have not been exercised in a live
session. Before production use, test each year you deploy, at minimum:
1. The app starts and a file appears in `%LOCALAPPDATA%\AutodeskMcp\instances`.
2. `list_autodesk_instances` shows the session as `reachable: true`.
3. One read tool, one write tool followed by Undo, and one `execute_csharp` call.

## Notes

* **AutoCAD 2027 ships its own local MCP server** for Autodesk Assistant. This project is independent of
  it. Both can be installed at the same time.
* **.NET 8 reaches end of support on 10 Nov 2026.** Revit and AutoCAD 2025/2026 still run on .NET 8, so
  those add-ins target it. The MCP server is self-contained .NET 10.
* **`AutoCAD.NET` 25.0.2 and 25.1.1 target net10.0 on NuGet**, which conflicts with the .NET 8 runtime
  documented for AutoCAD 2025/2026. This project pins 25.0.1 and 25.1.0, which target net8.0.
