<#
.SYNOPSIS
  Installs (or removes) the Autodesk MCP Bridge for the current Windows user. No admin rights needed.

.DESCRIPTION
  Copies files produced by scripts/build.ps1:
    MCP server -> %LOCALAPPDATA%\AutodeskMcp\server\AutodeskMcp.Server.exe
    Revit      -> %APPDATA%\Autodesk\Revit\Addins\<year>\   (per-user; also valid for Revit 2027, which
                  no longer reads C:\ProgramData add-in folders)
    AutoCAD    -> %APPDATA%\Autodesk\ApplicationPlugins\AutodeskMcp.bundle
  By default it installs for every Revit/AutoCAD year found under C:\Program Files\Autodesk.
  It does NOT edit your MCP client configuration; it prints the snippet to add.

.PARAMETER RevitYears
  Revit years to install instead of the detected ones. With -File (as in the examples) PowerShell passes
  every argument as one plain string, so give ONE year per run (-RevitYears 2026); "2025,2026" or @(...)
  are not split into several years. For several years, run the script once per year, or omit the
  parameter and let it detect them. A literal @() or an empty value means "none" (same as -SkipRevit).

.PARAMETER AutoCADYears
  Same as -RevitYears, for AutoCAD. A literal @() or an empty value means "none" (same as -SkipAutoCAD).

.PARAMETER SkipRevit
  Do not install the Revit add-in.

.PARAMETER SkipAutoCAD
  Do not install the AutoCAD bundle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -RevitYears 2026 -SkipAutoCAD
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -SkipRevit
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall

  Works in the Windows PowerShell 5.1 that ships with Windows (no need for PowerShell 7 / pwsh).
#>
param(
    [string[]] $RevitYears,
    [string[]] $AutoCADYears,
    [switch] $SkipRevit,
    [switch] $SkipAutoCAD,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$supported = @('2022', '2023', '2024', '2025', '2026', '2027')

function Get-InstalledYears([string] $pattern) {
    $supported | Where-Object { Test-Path (Join-Path $env:ProgramFiles ("Autodesk\" + ($pattern -f $_))) }
}

# With -File, "-AutoCADYears @()" arrives as the string '@()'. Treat that, '' and blanks as "none".
function Get-RequestedYears([string[]] $years) {
    if ($null -eq $years) { return $null }
    return , @($years | Where-Object { $null -ne $_ -and $_.Trim() -ne '' -and $_.Trim() -ne '@()' } |
        ForEach-Object { $_.Trim() })
}

$RevitYears = Get-RequestedYears $RevitYears
$AutoCADYears = Get-RequestedYears $AutoCADYears
if ($SkipRevit -and $RevitYears.Count -gt 0) { throw 'Use either -SkipRevit or -RevitYears, not both.' }
if ($SkipAutoCAD -and $AutoCADYears.Count -gt 0) { throw 'Use either -SkipAutoCAD or -AutoCADYears, not both.' }
if ($SkipRevit) { $RevitYears = @() }
if ($SkipAutoCAD) { $AutoCADYears = @() }

if ($null -eq $RevitYears) { $RevitYears = @(Get-InstalledYears 'Revit {0}') }
if ($null -eq $AutoCADYears) {
    # AutoCAD and verticals (Civil 3D, Plant 3D, ...) share the bundle, so install it if any is present.
    $AutoCADYears = @($supported | Where-Object {
        $y = $_; Get-ChildItem (Join-Path $env:ProgramFiles 'Autodesk') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "^AutoCAD.* $y$" }
    })
}

$serverDir = Join-Path $env:LOCALAPPDATA 'AutodeskMcp\server'
$bundleDir = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AutodeskMcp.bundle'

if ($Uninstall) {
    foreach ($year in $supported) {
        $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
        Remove-Item (Join-Path $addins 'AutodeskMcp.addin') -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $addins 'AutodeskMcp') -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $bundleDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $serverDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Removed. Config and logs remain in %LOCALAPPDATA%\AutodeskMcp.' -ForegroundColor Green
    return
}

if (-not (Test-Path (Join-Path $artifacts 'server/AutodeskMcp.Server.exe'))) {
    throw "Build output not found next to this script (expected an artifacts folder). Extract the whole AutodeskMcp-All zip and run this script from its scripts folder."
}

New-Item -ItemType Directory -Force $serverDir | Out-Null
Copy-Item (Join-Path $artifacts 'server/*') $serverDir -Recurse -Force
Write-Host "MCP server  -> $serverDir"

if ($RevitYears.Count -eq 0) { Write-Host 'Revit       -> skipped (none requested or detected)' }
foreach ($year in $RevitYears) {
    $src = Join-Path $artifacts "revit/$year"
    if (-not (Test-Path $src)) { Write-Warning "No Revit $year build in artifacts; skipped."; continue }
    $dest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $src '*') $dest -Recurse -Force
    Write-Host "Revit $year  -> $dest"
}

if ($AutoCADYears.Count -gt 0) {
    New-Item -ItemType Directory -Force $bundleDir | Out-Null
    Copy-Item (Join-Path $artifacts 'autocad/AutodeskMcp.bundle/*') $bundleDir -Recurse -Force
    Write-Host "AutoCAD     -> $bundleDir (years: $($AutoCADYears -join ', '))"
} else {
    Write-Host 'AutoCAD     -> skipped (none requested or detected)'
}

$exe = (Join-Path $serverDir 'AutodeskMcp.Server.exe').Replace('\', '\\')
Write-Host "`nAdd this to your MCP client (e.g. Claude Desktop claude_desktop_config.json):" -ForegroundColor Cyan
Write-Host @"
{
  "mcpServers": {
    "autodesk": { "command": "$exe" }
  }
}
"@
Write-Host "Claude Code:  claude mcp add autodesk -- `"$(Join-Path $serverDir 'AutodeskMcp.Server.exe')`""
Write-Host "`nThen restart Revit/AutoCAD so the add-in loads." -ForegroundColor Green
