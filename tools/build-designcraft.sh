#!/usr/bin/env bash
# Compiles DesignCraft from the external/designcraft submodule (our fork Kazo-ca/designcraft;
# decision 4 and work item 1 of doc/en/plan.md) with Trunk, then vendors the result in
# src/Kazo.Design/wwwroot/designcraft/ with tools/vendor-designcraft.sh --dir. The
# .github/workflows/build-designcraft.yml workflow runs it; it also runs on a developer machine.
#
# Usage:
#   tools/build-designcraft.sh
#
# Needs: Rust stable (1.95 or later) with the wasm32-unknown-unknown target, Trunk at the version
# below, git and network access (craft-fonts). Like DesignCraft's own releases, the build embeds the
# web faces of storytold/craft-fonts (BIZ UDPGothic and Noto Sans Georgian, for the Japanese and
# Georgian interface) at the revision DesignCraft's release workflow pins. CRAFT_FONTS_DIR, when
# set, names a craft-fonts checkout to use instead of cloning one; it must be on that commit.
#
# Size profile (work item 1, measured in doc/en/plan.md): DesignCraft's release profile is
# opt-level 3 with thin LTO, which gives a 62 MB .wasm, above GitHub's 50 MiB warning. The
# CARGO_PROFILE_RELEASE_* variables below build for size instead (opt-level "s", fat LTO, one codegen
# unit) without changing the fork: about 39 MB.
set -euo pipefail

# The Trunk version of DesignCraft's release workflow (craft-common-actions, install-trunk).
trunk_version="0.21.14"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
designcraft="$repo_root/external/designcraft"

case "${1:-}" in
  "") ;;
  -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
  *) echo "Unknown argument: $1" >&2; exit 2 ;;
esac

fail() { echo "error: $*" >&2; exit 1; }

[[ -f "$designcraft/Cargo.toml" ]] || fail "external/designcraft is empty. Run: git submodule update --init external/designcraft"
# A local change of DesignCraft must go through a commit of the fork, so the provenance names it.
[[ -z "$(git -C "$designcraft" status --porcelain)" ]] || fail "external/designcraft has local changes; commit them in the fork first"

command -v trunk >/dev/null || fail "trunk not found; install Trunk $trunk_version"
actual_trunk="$(trunk --version | awk '{print $2}')"
[[ "$actual_trunk" == "$trunk_version" ]] || fail "trunk $actual_trunk found; DesignCraft's release uses $trunk_version"
rustup target list --installed 2>/dev/null | grep -qx wasm32-unknown-unknown \
  || fail "the wasm32-unknown-unknown target is missing (rustup target add wasm32-unknown-unknown)"

commit="$(git -C "$designcraft" rev-parse HEAD)"
rustc_version="$(rustc --version)"
# The craft-fonts revision of DesignCraft's release workflow (`craft-fonts-ref:`).
fonts_rev="$(awk '$1 == "craft-fonts-ref:" { print $2; exit }' "$designcraft/.github/workflows/release.yml")"
[[ "$fonts_rev" =~ ^[0-9a-f]{40}$ ]] || fail "no craft-fonts-ref in external/designcraft/.github/workflows/release.yml"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fonts="${CRAFT_FONTS_DIR:-}"
if [[ -z "$fonts" ]]; then
  fonts="$work/craft-fonts"
  git init -q "$fonts"
  git -C "$fonts" fetch -q --depth 1 https://github.com/storytold/craft-fonts.git "$fonts_rev"
  git -C "$fonts" checkout -q FETCH_HEAD
fi
[[ "$(git -C "$fonts" rev-parse HEAD)" == "$fonts_rev" ]] || fail "$fonts is not on craft-fonts $fonts_rev"

# trunk build --release in apps/designcraft-web (wasm-opt -Oz, set by its index.html), into a
# temporary folder so the submodule stays clean.
echo "Building DesignCraft $commit (rustc: $rustc_version, trunk $actual_trunk, craft-fonts $fonts_rev)"
(
  export CRAFT_FONTS_DIR="$fonts" CRAFT_FONTS_REQUIRED=1
  export CARGO_PROFILE_RELEASE_OPT_LEVEL=s CARGO_PROFILE_RELEASE_LTO=fat CARGO_PROFILE_RELEASE_CODEGEN_UNITS=1
  export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$designcraft/target}"
  cd "$designcraft/apps/designcraft-web"
  trunk build --release --dist "$work/site"
)

"$repo_root/tools/vendor-designcraft.sh" --dir "$work/site" --craft-fonts-dir "$fonts" \
  --craft-fonts "$fonts_rev" --rustc "$rustc_version" --trunk "$actual_trunk" --profile "opt-level=s, lto=fat, codegen-units=1"
