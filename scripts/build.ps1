<#
.SYNOPSIS
  Builds the MCP server and every Revit/AutoCAD add-in variant and stages them under artifacts\.

.DESCRIPTION
  Requires the .NET 10 SDK (it also builds the net8.0 add-ins for 2025/2026).
  Output layout:
    artifacts\server\AutodeskMcp.Server.exe                     (self-contained, win-x64)
    artifacts\revit\<year>\AutodeskMcp.addin
    artifacts\revit\<year>\AutodeskMcp\AutodeskMcp.Revit.dll    (+ AutodeskMcp.Bridge.dll)
    artifacts\autocad\AutodeskMcp.bundle\PackageContents.xml
    artifacts\autocad\AutodeskMcp.bundle\Contents\<year>\AutodeskMcp.AutoCAD.dll

.EXAMPLE
  pwsh scripts/build.ps1
  pwsh scripts/build.ps1 -Years 2026 -SkipTests
#>
param(
    [ValidateSet('2025', '2026', '2027')]
    [string[]] $Years = @('2025', '2026', '2027'),
    [string] $Configuration = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

if (-not $SkipTests) {
    Write-Host '== Tests' -ForegroundColor Cyan
    Invoke-Dotnet test --project (Join-Path $root 'tests/AutodeskMcp.Tests')
}

Write-Host '== MCP server (win-x64, self-contained single file)' -ForegroundColor Cyan
Invoke-Dotnet publish (Join-Path $root 'src/AutodeskMcp.Server') -c $Configuration -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $artifacts 'server')

$bundle = Join-Path $artifacts 'autocad/AutodeskMcp.bundle'
New-Item -ItemType Directory -Force $bundle | Out-Null
Copy-Item (Join-Path $root 'deploy/AutoCAD/AutodeskMcp.bundle/PackageContents.xml') $bundle

foreach ($year in $Years) {
    Write-Host "== Revit $year" -ForegroundColor Cyan
    Invoke-Dotnet build (Join-Path $root 'src/AutodeskMcp.Revit') -c $Configuration "-p:RevitVersion=$year"
    $revitOut = Join-Path $root "src/AutodeskMcp.Revit/bin/$Configuration/Revit$year"
    $revitDest = Join-Path $artifacts "revit/$year"
    New-Item -ItemType Directory -Force (Join-Path $revitDest 'AutodeskMcp') | Out-Null
    Copy-Item (Join-Path $revitOut 'AutodeskMcp.addin') $revitDest
    Copy-Item (Join-Path $revitOut 'AutodeskMcp.*.dll'), (Join-Path $revitOut 'AutodeskMcp.*.pdb') (Join-Path $revitDest 'AutodeskMcp')

    Write-Host "== AutoCAD $year" -ForegroundColor Cyan
    Invoke-Dotnet build (Join-Path $root 'src/AutodeskMcp.AutoCAD') -c $Configuration "-p:AutoCADVersion=$year"
    $acadOut = Join-Path $root "src/AutodeskMcp.AutoCAD/bin/$Configuration/AutoCAD$year"
    $acadDest = Join-Path $bundle "Contents/$year"
    New-Item -ItemType Directory -Force $acadDest | Out-Null
    Copy-Item (Join-Path $acadOut 'AutodeskMcp.*.dll'), (Join-Path $acadOut 'AutodeskMcp.*.pdb') $acadDest
}

Write-Host "`nDone. Artifacts in $artifacts" -ForegroundColor Green
Write-Host "Install with: pwsh scripts/install.ps1"
