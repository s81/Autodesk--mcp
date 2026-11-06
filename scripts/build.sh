#!/usr/bin/env bash
# Bash equivalent of scripts/build.ps1 (Linux/macOS/CI). Requires the .NET 10 SDK and python3 (for zipping).
#
# Builds the MCP server (win-x64, self-contained) and every Revit/AutoCAD add-in year, stages them under
# artifacts/ (same layout as build.ps1, so scripts/install.ps1 works), and writes per-version zips plus the
# Claude Desktop extension (AutodeskMcp-v<version>.mcpb) to dist/.
#
# Usage: scripts/build.sh [--skip-tests] [years...]      e.g. scripts/build.sh --skip-tests 2026
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG=Release
SKIP_TESTS=0
YEARS=()
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    2022|2023|2024|2025|2026|2027) YEARS+=("$arg") ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done
[ ${#YEARS[@]} -eq 0 ] && YEARS=(2022 2023 2024 2025 2026 2027)

ART="$ROOT/artifacts"
DIST="$ROOT/dist"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
rm -rf "$ART" "$DIST"
mkdir -p "$ART" "$DIST"

if [ "$SKIP_TESTS" -eq 0 ]; then
  echo "== Tests"
  dotnet test --project "$ROOT/tests/AutodeskMcp.Tests"
fi

echo "== MCP server (win-x64, self-contained single file)"
dotnet publish "$ROOT/src/AutodeskMcp.Server" -c "$CONFIG" -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$ART/server" -nologo

BUNDLE="$ART/autocad/AutodeskMcp.bundle"
mkdir -p "$BUNDLE"
cp "$ROOT/deploy/AutoCAD/AutodeskMcp.bundle/PackageContents.xml" "$BUNDLE/"

for y in "${YEARS[@]}"; do
  echo "== Revit $y"
  dotnet build "$ROOT/src/AutodeskMcp.Revit" -c "$CONFIG" -p:RevitVersion="$y" -nologo
  out="$ROOT/src/AutodeskMcp.Revit/bin/$CONFIG/Revit$y"
  mkdir -p "$ART/revit/$y/AutodeskMcp"
  cp "$out/AutodeskMcp.addin" "$ART/revit/$y/"
  cp "$out"/AutodeskMcp.*.dll "$out"/AutodeskMcp.*.pdb "$ART/revit/$y/AutodeskMcp/"

  echo "== AutoCAD $y"
  dotnet build "$ROOT/src/AutodeskMcp.AutoCAD" -c "$CONFIG" -p:AutoCADVersion="$y" -nologo
  out="$ROOT/src/AutodeskMcp.AutoCAD/bin/$CONFIG/AutoCAD$y"
  mkdir -p "$BUNDLE/Contents/$y"
  cp "$out"/AutodeskMcp.*.dll "$out"/AutodeskMcp.*.pdb "$BUNDLE/Contents/$y/"
done

echo "== Packaging"
YEARS_CSV="$(IFS=,; echo "${YEARS[*]}")"
python3 - "$ROOT" "$ART" "$DIST" "$VERSION" "$YEARS_CSV" <<'PY'
import os, sys, zipfile
root, art, dist, version, years = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(",")

def add_tree(z, src, arc_prefix):
    for base, _, files in os.walk(src):
        for f in sorted(files):
            full = os.path.join(base, f)
            z.write(full, os.path.join(arc_prefix, os.path.relpath(full, src)))

def make(name, entries):
    path = os.path.join(dist, name)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for src, arc in entries:
            if os.path.isdir(src): add_tree(z, src, arc)
            else: z.write(src, arc)
    print(f"  {name}  ({os.path.getsize(path)/1_048_576:.1f} MB)")

pc = os.path.join(art, "autocad", "AutodeskMcp.bundle", "PackageContents.xml")
for y in years:
    # Revit: extract into %APPDATA%\Autodesk\Revit\Addins\<year>\
    make(f"AutodeskMcp-Revit{y}-v{version}.zip", [(os.path.join(art, "revit", y), "")])
    # AutoCAD: extract into %APPDATA%\Autodesk\ApplicationPlugins\  (a bundle with only this year)
    make(f"AutodeskMcp-AutoCAD{y}-v{version}.zip", [
        (pc, "AutodeskMcp.bundle/PackageContents.xml"),
        (os.path.join(art, "autocad", "AutodeskMcp.bundle", "Contents", y), f"AutodeskMcp.bundle/Contents/{y}"),
    ])
make(f"AutodeskMcp-Server-win-x64-v{version}.zip", [(os.path.join(art, "server"), "")])

# Claude Desktop extension: double-click to install. The server copies the add-ins from addins/ into the
# Revit/AutoCAD folders on start (src/AutodeskMcp.Server/AddinSetup.cs). Staged under artifacts/mcpb.
stage = os.path.join(art, "mcpb")
os.makedirs(stage, exist_ok=True)
with open(os.path.join(root, "deploy", "mcpb", "manifest.json"), encoding="utf-8") as f:
    manifest = f.read().replace("{{VERSION}}", version)
with open(os.path.join(stage, "manifest.json"), "w", encoding="utf-8") as f:
    f.write(manifest)
mcpb_entries = [
    (os.path.join(stage, "manifest.json"), "manifest.json"),
    (os.path.join(art, "server"), "server"),
    (os.path.join(art, "autocad", "AutodeskMcp.bundle"), "addins/autocad/AutodeskMcp.bundle"),
]
for y in years:
    mcpb_entries.append((os.path.join(art, "revit", y), f"addins/revit/{y}"))
make(f"AutodeskMcp-v{version}.mcpb", mcpb_entries)
# Everything + installer, laid out so scripts/install.ps1 runs straight from the extracted folder.
make(f"AutodeskMcp-All-v{version}.zip", [
    (art, "artifacts"),
    (os.path.join(root, "scripts", "install.ps1"), "scripts/install.ps1"),
    (os.path.join(root, "deploy", "config.example.json"), "config.example.json"),
    (os.path.join(root, "README.md"), "README.md"),
])
PY

# Validate the extension manifest with the official MCPB tool when Node.js is available (it is on CI).
if command -v npx >/dev/null 2>&1; then
  echo "== Validating the .mcpb manifest"
  npx --yes @anthropic-ai/mcpb@2.1.2 validate "$ART/mcpb/manifest.json"
else
  echo "== Skipping .mcpb manifest validation (npx not found)"
fi

echo
echo "Done. Staged files: $ART"
echo "Zips:               $DIST"
