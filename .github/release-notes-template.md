Autodesk MCP Bridge: an MCP server plus add-ins that let MCP clients (Claude Desktop, Claude Code, …) read and edit live **Revit** and **AutoCAD** 2022–2027 sessions on the same Windows PC.

## Downloads

**Easiest install (Claude Desktop):** download `AutodeskMcp-v{{VERSION}}.mcpb`, then in Claude Desktop open **Settings → Desktop app → Manage** (next to Extensions; **Settings → Extensions** in older versions) → **Advanced settings** → **Install Extension…**, choose the file and follow the prompts. When Claude starts the extension, it installs the Revit/AutoCAD add-ins for every year on the PC; restart Revit/AutoCAD afterwards. The extension is not code-signed yet, so Claude may show a warning.

| File | What it is | Extract to |
|---|---|---|
| `AutodeskMcp-v{{VERSION}}.mcpb` | Claude Desktop extension: server + all add-ins | install from Claude Desktop (no extracting) |
| `AutodeskMcp-All-v{{VERSION}}.zip` | Everything + `scripts/install.ps1` (auto-detects your installed years) | anywhere, then run `powershell -ExecutionPolicy Bypass -File scripts\install.ps1` inside the extracted folder |
| `AutodeskMcp-Server-win-x64-v{{VERSION}}.zip` | MCP server, self-contained (no .NET install needed) | anywhere; point your MCP client at `AutodeskMcp.Server.exe` |
| `AutodeskMcp-Revit2022-v{{VERSION}}.zip` | Revit 2022 add-in (.NET Framework 4.8) | `%APPDATA%\Autodesk\Revit\Addins\2022\` |
| `AutodeskMcp-Revit2023-v{{VERSION}}.zip` | Revit 2023 add-in (.NET Framework 4.8) | `%APPDATA%\Autodesk\Revit\Addins\2023\` |
| `AutodeskMcp-Revit2024-v{{VERSION}}.zip` | Revit 2024 add-in (.NET Framework 4.8) | `%APPDATA%\Autodesk\Revit\Addins\2024\` |
| `AutodeskMcp-Revit2025-v{{VERSION}}.zip` | Revit 2025 add-in (.NET 8) | `%APPDATA%\Autodesk\Revit\Addins\2025\` |
| `AutodeskMcp-Revit2026-v{{VERSION}}.zip` | Revit 2026 add-in (.NET 8) | `%APPDATA%\Autodesk\Revit\Addins\2026\` |
| `AutodeskMcp-Revit2027-v{{VERSION}}.zip` | Revit 2027 add-in (.NET 10) | `%APPDATA%\Autodesk\Revit\Addins\2027\` |
| `AutodeskMcp-AutoCAD2022-v{{VERSION}}.zip` | AutoCAD 2022 bundle (R24.1, .NET Framework 4.8) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `AutodeskMcp-AutoCAD2023-v{{VERSION}}.zip` | AutoCAD 2023 bundle (R24.2, .NET Framework 4.8) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `AutodeskMcp-AutoCAD2024-v{{VERSION}}.zip` | AutoCAD 2024 bundle (R24.3, .NET Framework 4.8) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `AutodeskMcp-AutoCAD2025-v{{VERSION}}.zip` | AutoCAD 2025 bundle (R25.0, .NET 8) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `AutodeskMcp-AutoCAD2026-v{{VERSION}}.zip` | AutoCAD 2026 bundle (R25.1, .NET 8) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `AutodeskMcp-AutoCAD2027-v{{VERSION}}.zip` | AutoCAD 2027 bundle (R26.0, .NET 10) | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `SHA256SUMS.txt` | Checksums for the zips and the .mcpb | |

AutoCAD bundles work in AutoCAD and its verticals (Civil 3D, Plant 3D, Advance Steel, …), but not in AutoCAD LT. If you install several AutoCAD years, extract them into the same `AutodeskMcp.bundle` folder.

## Register the server with your MCP client (not needed with the .mcpb)
Claude Code:
```
claude mcp add autodesk -- "C:\path\to\AutodeskMcp.Server.exe"
```
Claude Desktop (`claude_desktop_config.json`):
```json
{ "mcpServers": { "autodesk": { "command": "C:\\path\\to\\AutodeskMcp.Server.exe" } } }
```
Restart Revit or AutoCAD, then call `list_autodesk_instances`.

## Security
The `*_execute_csharp` tools run arbitrary code with your Windows user rights inside Revit or AutoCAD. To turn them off, set `"allowCodeExecution": false` in `%LOCALAPPDATA%\AutodeskMcp\config.json`. For read-only use, set `"allowModifications": false`.

Built from commit {{COMMIT}}. See the README for the full tool list.
