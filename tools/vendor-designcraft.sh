#!/usr/bin/env bash
# Puts a DesignCraft web build in src/Kazo.Design/wwwroot/designcraft/ (decision 4 of
# doc/en/plan.md; the same method as Kazo Photo's vendor-photocraft.sh). That folder is never edited
# by hand: this script replaces it whole and writes designcraft.json, its provenance (version,
# source, name, size and SHA-256 of each file). tests/Kazo.Design.Tests checks the files still match.
#
# Usage:
#   tools/vendor-designcraft.sh --dir <folder> --craft-fonts-dir <checkout> [--craft-fonts <sha>]
#                               [--rustc <text>] [--trunk <version>] [--profile <text>]
#
# <folder> is Trunk's output for apps/designcraft-web of the external/designcraft submodule (one
# designcraft-web-<hash>.js, one designcraft-web-<hash>_bg.wasm, snippets/), as
# tools/build-designcraft.sh makes it. <checkout> is the craft-fonts checkout the build embedded,
# for the licences of its two web faces. There is no "official zip" mode: Kazo Design is always
# built from our fork (decision 4).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="$repo_root/src/Kazo.Design/wwwroot/designcraft"
submodule="$repo_root/external/designcraft"

fail() { echo "$*" >&2; exit 1; }

build_dir=""
fonts_dir=""
fonts_rev=""
rustc_version=""
trunk_version=""
profile=""
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 || "$1" == -h || "$1" == --help ]] || fail "$1 needs a value"
  case "$1" in
    --dir) build_dir="$2"; shift 2 ;;
    --craft-fonts-dir) fonts_dir="$2"; shift 2 ;;
    --craft-fonts) fonts_rev="$2"; shift 2 ;;
    --rustc) rustc_version="$2"; shift 2 ;;
    --trunk) trunk_version="$2"; shift 2 ;;
    --profile) profile="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$build_dir" ]] || fail "--dir <folder> is required (Trunk's output; tools/build-designcraft.sh makes it)"
[[ -d "$build_dir" ]] || fail "$build_dir is not a folder"
[[ -n "$fonts_dir" && -f "$fonts_dir/fonts/manifest.txt" ]] || fail "--craft-fonts-dir <checkout> is required (the craft-fonts the build embedded)"
source_dir="$(cd "$build_dir" && pwd)"

# The provenance names the fork and the submodule's commit, which must be clean so the commit alone
# says what was compiled.
[[ -f "$submodule/Cargo.toml" ]] || fail "external/designcraft is empty. Run: git submodule update --init external/designcraft"
[[ -z "$(git -C "$submodule" status --porcelain)" ]] || fail "external/designcraft has local changes; the provenance could not name what was built"
commit="$(git -C "$submodule" rev-parse HEAD)"
repository="$(git -C "$repo_root" config -f .gitmodules submodule.external/designcraft.url)"
version="$(awk '/^\[/ { in_pkg = ($0 == "[workspace.package]"); next }
                in_pkg && $1 == "version" { gsub(/[" ]/, "", $3); print $3; exit }' "$submodule/Cargo.toml")"
[[ -n "$version" ]] || fail "no [workspace.package] version in external/designcraft/Cargo.toml"

# Escapes a value for a JSON string (the rustc version is free text).
json_string() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '"%s"' "$value"
}

# Exactly one glue module and one .wasm, named by Trunk with the same hash.
shopt -s nullglob
glues=("$source_dir"/designcraft-web-*.js)
wasms=("$source_dir"/designcraft-web-*_bg.wasm)
shopt -u nullglob
if [[ ${#glues[@]} -ne 1 || ${#wasms[@]} -ne 1 ]]; then
  fail "Expected one designcraft-web-*.js and one designcraft-web-*_bg.wasm in $source_dir"
fi
glue="$(basename "${glues[0]}")"
wasm="$(basename "${wasms[0]}")"
[[ "${glue%.js}" == "${wasm%_bg.wasm}" ]] || fail "$glue and $wasm do not come from the same build"

# What the app needs: the glue, the .wasm, the snippets the glue imports, then every licence and
# notice of the release: DesignCraft's own (MIT OR Apache-2.0, NOTICE), its asset list (ASSETS.md),
# the OFL licences of the fonts its .wasm embeds (assets/fonts), the licences of its PDF backend
# (licenses/) and the OFL licences of the two craft-fonts faces of the web build. Left out: Trunk's
# index.html (host/index.html replaces it).
rm -rf "$target"
mkdir -p "$target"
cp "$source_dir/$glue" "$source_dir/$wasm" "$target/"
[[ ! -d "$source_dir/snippets" ]] || cp -R "$source_dir/snippets" "$target/"
for notice in LICENSE-MIT LICENSE-APACHE NOTICE ASSETS.md; do
  [[ -f "$submodule/$notice" ]] || fail "external/designcraft/$notice is missing"
  cp "$submodule/$notice" "$target/"
done
shopt -s nullglob
font_licences=("$submodule"/assets/fonts/OFL-*.txt)
shopt -u nullglob
[[ ${#font_licences[@]} -gt 0 ]] || fail "no assets/fonts/OFL-*.txt in external/designcraft"
cp "${font_licences[@]}" "$target/"
[[ -d "$submodule/licenses" ]] || fail "external/designcraft/licenses is missing"
cp -R "$submodule/licenses" "$target/licenses"
for face in biz-ud-pgothic:BIZUDPGothic noto-sans-georgian:NotoSansGeorgian; do
  [[ -f "$fonts_dir/fonts/${face%%:*}/OFL.txt" ]] || fail "craft-fonts has no fonts/${face%%:*}/OFL.txt"
  cp "$fonts_dir/fonts/${face%%:*}/OFL.txt" "$target/OFL-${face##*:}.txt"
done

# The provenance file, files sorted by path so a new version gives a readable diff.
{
  printf '{\n'
  printf '  "version": "%s",\n' "$version"
  printf '  "source": {\n'
  printf '    "kind": "source-build",\n'
  printf '    "repository": %s,\n' "$(json_string "$repository")"
  printf '    "commit": "%s"' "$commit"
  [[ -z "$fonts_rev" ]] || printf ',\n    "craftFonts": "%s"' "$fonts_rev"
  [[ -z "$rustc_version" ]] || printf ',\n    "rustc": %s' "$(json_string "$rustc_version")"
  [[ -z "$trunk_version" ]] || printf ',\n    "trunk": %s' "$(json_string "$trunk_version")"
  [[ -z "$profile" ]] || printf ',\n    "profile": %s' "$(json_string "$profile")"
  printf '\n  },\n'
  printf '  "glue": "%s",\n' "$glue"
  printf '  "wasm": "%s",\n' "$wasm"
  printf '  "files": [\n'
  first=1
  while IFS= read -r path; do
    size="$(stat -c %s "$target/$path")"
    sha="$(sha256sum "$target/$path" | cut -d' ' -f1)"
    [[ $first -eq 1 ]] || printf ',\n'
    first=0
    printf '    { "path": "%s", "size": %s, "sha256": "%s" }' "$path" "$size" "$sha"
  done < <(cd "$target" && find . -type f ! -path ./designcraft.json | sed 's#^\./##' | LC_ALL=C sort)
  printf '\n  ]\n'
  printf '}\n'
} > "$target/designcraft.json"

echo "DesignCraft $version vendored in ${target#"$repo_root"/}: $glue, $wasm"
