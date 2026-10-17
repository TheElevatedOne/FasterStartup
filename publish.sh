#!/usr/bin/env bash
# Builds the Thunderstore package, ready to upload at https://thunderstore.io/package/create/ :
#   <OUTPUT_DIR>/<NAMESPACE>-<NAME>-<VERSION>.zip
# containing manifest.json, icon.png, README.md (from $README), CHANGELOG.md,
# plugins/<plugin DLL + icon> and patchers/<patcher DLL>.
#
# Usage: ./publish.sh [path/to/FasterStartup.dll] [path/to/FasterStartup.Patcher.dll]
#   (normally run through `make publish`, which builds the DLLs first)
#
# Package metadata is in thunderstore.conf. To release a new version:
#   1. bump VERSION in thunderstore.conf and PluginVersion in src/Plugin/FasterStartupPlugin.cs,
#   2. add a "## <version>" entry at the top of CHANGELOG.md,
#   3. run `make publish` and upload the zip.
set -euo pipefail

cd "$(dirname "$0")"
source ./thunderstore.conf

PLUGIN_DLL=${1:-bin/Release/$NAME.dll}
PATCHER_DLL=${2:-bin/Release/$NAME.Patcher.dll}

die() {
  echo "publish.sh: $*" >&2
  exit 1
}

for tool in zip unzip; do
  command -v "$tool" >/dev/null || die "'$tool' is required"
done

[[ -f "$PLUGIN_DLL" ]] || die "plugin DLL not found at '$PLUGIN_DLL' (run 'make build' first)"
[[ -f "$PATCHER_DLL" ]] || die "patcher DLL not found at '$PATCHER_DLL' (run 'make build' first)"
[[ -f "$README" ]] || die "README '$README' is missing"
[[ -f CHANGELOG.md ]] || die "CHANGELOG.md is missing"
[[ -f icon.png ]] || die "icon.png is missing"

# Package metadata, as Thunderstore validates it.
[[ "$NAMESPACE" =~ ^[A-Za-z0-9_]+$ ]] || die "NAMESPACE may only contain letters, digits and underscores"
[[ "$NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "NAME may only contain letters, digits and underscores"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "VERSION must be Major.Minor.Patch (is '$VERSION')"
((${#DESCRIPTION} <= 250)) || die "DESCRIPTION is ${#DESCRIPTION} characters, Thunderstore allows 250"

# Thunderstore requires a 256x256 PNG icon. Read the size from the PNG IHDR chunk.
[[ "$(head -c 8 icon.png | od -An -tx1 | tr -d ' \n')" == "89504e470d0a1a0a" ]] || die "icon.png is not a PNG"
size=$(od -An -tu1 -j16 -N8 icon.png | awk '{print $1*16777216+$2*65536+$3*256+$4 "x" $5*16777216+$6*65536+$7*256+$8}')
[[ "$size" == "256x256" ]] || die "icon.png must be 256x256 (is $size)"

# The version shown in-game and in the changelog must match the package version.
src_version=$(grep -oP 'PluginVersion\s*=\s*"\K[^"]+' src/Plugin/FasterStartupPlugin.cs)
[[ "$src_version" == "$VERSION" ]] || die "PluginVersion in source ($src_version) != VERSION in thunderstore.conf ($VERSION)"
changelog_version=$(grep -m1 -oP '^## \K[0-9]+\.[0-9]+\.[0-9]+' CHANGELOG.md || true)
[[ "$changelog_version" == "$VERSION" ]] || die "the top CHANGELOG.md entry is '## ${changelog_version:-?}', expected '## $VERSION'"

json_escape() {
  local s=${1//\\/\\\\}
  s=${s//\"/\\\"}
  printf '%s' "$s"
}

deps_json=""
for dep in $DEPENDENCIES; do
  [[ "$dep" =~ ^[A-Za-z0-9_]+-[A-Za-z0-9_]+-[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "bad dependency string '$dep'"
  deps_json+="${deps_json:+, }\"$dep\""
done

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

cat >"$staging/manifest.json" <<JSON
{
  "name": "$(json_escape "$NAME")",
  "version_number": "$VERSION",
  "website_url": "$(json_escape "$WEBSITE_URL")",
  "description": "$(json_escape "$DESCRIPTION")",
  "dependencies": [$deps_json]
}
JSON

# The plugin and its icon (shown by Risk Of Options) go to BepInEx/plugins, the patcher to BepInEx/patchers.
mkdir -p "$staging/plugins" "$staging/patchers"
cp icon.png CHANGELOG.md "$staging/"
cp "$README" "$staging/README.md"
cp "$PLUGIN_DLL" icon.png "$staging/plugins/"
cp "$PATCHER_DLL" "$staging/patchers/"

mkdir -p "$OUTPUT_DIR"
out="$(realpath "$OUTPUT_DIR")/${NAMESPACE}-${NAME}-${VERSION}.zip"
rm -f "$out"
(cd "$staging" && zip -q -X -r "$out" manifest.json icon.png README.md CHANGELOG.md plugins patchers)

echo "Created $out"
unzip -l "$out"
echo "Upload it at https://thunderstore.io/package/create/ (community: Risk of Rain 2)."
