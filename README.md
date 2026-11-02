# Autodesk MCP Bridge — Revit & AutoCAD 2025–2027

A [Model Context Protocol](https://modelcontextprotocol.io) server that lets MCP clients (Claude Desktop,
Claude Code, etc.) read and edit **live** Revit and AutoCAD sessions on the same Windows computer.

| Product | Years | Add-in target | API reference package |
|---|---|---|---|
| Revit | 2025, 2026 | `net8.0-windows` | `Nice3point.Revit.Api.*` 2025.4.60 / 2026.4.10 |
| Revit | 2027 | `net10.0-windows` | `Nice3point.Revit.Api.*` 2027.3.0 |
| AutoCAD (+ verticals: Civil 3D, Plant 3D, Advance Steel…) | 2025 (R25.0), 2026 (R25.1) | `net8.0-windows` | `AutoCAD.NET` 25.0.1 / 25.1.0 (official) |
| AutoCAD | 2027 (R26.0) | `net10.0-windows` | `AutoCAD.NET` 26.0.0 (official) |

AutoCAD LT is not supported because it has no .NET API.

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
  process* against the exact assemblies the host has loaded. Only the finished IL is sent to the add-in,
  which loads it into a collectible `AssemblyLoadContext`. Because Roslyn never loads inside Revit or
  AutoCAD, it can't conflict with versions shipped by the host or other add-ins.
* **Shared `AutodeskMcp.Bridge` library** (net8.0, no dependencies): pipe framing, discovery, policy,
  main-thread work items, safe JSON conversion.

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

You need the **.NET 10 SDK**; it builds both the net8 and net10 targets. You don't need Revit or AutoCAD
installed to build.

```bash
pwsh scripts/build.ps1
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
* tests on Linux and Windows;
* builds all six add-ins (Revit and AutoCAD × 2025, 2026, 2027) with warnings treated as errors;
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

```powershell
pwsh scripts/install.ps1            # auto-detects installed Revit/AutoCAD years
```

Then register the server with your MCP client and restart Revit/AutoCAD.

Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):

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

Where files go:
* Revit: `%APPDATA%\Autodesk\Revit\Addins\<year>\AutodeskMcp.addin` + `AutodeskMcp\`. Revit 2027 no
  longer reads `C:\ProgramData\Autodesk\Revit\Addins`. Per-user folders still work.
* AutoCAD: `%APPDATA%\Autodesk\ApplicationPlugins\AutodeskMcp.bundle`. One bundle serves 2025, 2026 and
  2027. If SECURELOAD shows a warning, add the bundle folder to `TRUSTEDPATHS` or choose *Always load*.

## Security and policy

* The pipe is created with `PipeOptions.CurrentUserOnly`, so other Windows users can't connect. Each
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

Verified in CI-like conditions (Linux, .NET 10 SDK):
* All 6 add-in variants compile with 0 warnings against the official/reference API assemblies, and so
  does the server.
* 14 automated tests pass. They cover pipe transport, token authentication, policy enforcement,
  timeout-cancellation, stale-instance pruning, out-of-process compilation and loading of scripts
  (including error line mapping), and an end-to-end MCP stdio session (`initialize` → `tools/list` →
  `tools/call` proxied to a fake host).

**Not yet verified:** running inside actual Revit or AutoCAD. The host-specific handlers
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
