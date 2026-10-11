#!/usr/bin/env bash
# Runs a tool of the autoplay package (com.unpseudo.autoplay: its own repository, pinned by tag in Packages/manifest.json).
#   tools/autoplay/autoplay-tool.sh --dir                       print the package's Tools~ folder
#   tools/autoplay/autoplay-tool.sh <tool>.py|.sh|.ps1 [args…]  run that tool, from the project root
# Tools~ = Packages/com.unpseudo.autoplay/Tools~ when a local clone overrides the package (to work on the package),
# else the newest Library/PackageCache/com.unpseudo.autoplay@*/Tools~ (read-only: never edit it, it is replaced on update).
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
export PYTHONDONTWRITEBYTECODE=1

find_in() {
  [ -f "$1/Packages/com.unpseudo.autoplay/package.json" ] && { echo "$1/Packages/com.unpseudo.autoplay/Tools~"; return; }
  local d; d="$(ls -td "$1"/Library/PackageCache/com.unpseudo.autoplay@*/Tools~ 2>/dev/null | head -1)"
  [ -n "$d" ] && echo "${d%/}"
}

tools_dir() {
  local d; d="$(find_in "$ROOT")"
  [ -n "$d" ] && { echo "$d"; return; }
  # A worktree without Library yet (fresh, or parked for `unityctl.sh unpark`): borrow the main checkout's copy.
  local main; main="$(cd "$(git -C "$ROOT" rev-parse --git-common-dir 2>/dev/null)/.." 2>/dev/null && (pwd -W 2>/dev/null || pwd))"
  [ -n "$main" ] && d="$(find_in "$main")"
  [ -n "$d" ] && { echo "autoplay tools borrowed from the main checkout ($d)" >&2; echo "$d"; return; }
  echo "autoplay package not resolved in $ROOT: open the project in Unity once (or run 'unity command package_resolve')" >&2
  return 1
}

TOOLS="$(tools_dir)" || exit 2
[ "${1:-}" = "--dir" ] && { echo "$TOOLS"; exit 0; }
[ -n "${1:-}" ] || { sed -n '2,6p' "$0" >&2; exit 2; }
tool="$1"; shift
[ -f "$TOOLS/$tool" ] || { echo "no tool '$tool' in $TOOLS" >&2; exit 2; }
cd "$ROOT" || exit 2
case "$tool" in
  *.py)  exec python -B -X utf8 "$TOOLS/$tool" "$@" ;;
  *.sh)  exec bash "$TOOLS/$tool" "$@" ;;
  *.ps1) exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$TOOLS/$tool" "$@" ;;
  *)     echo "unknown tool type: $tool" >&2; exit 2 ;;
esac
