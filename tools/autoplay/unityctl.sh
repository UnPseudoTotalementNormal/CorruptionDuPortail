#!/usr/bin/env bash
# Corruption du Portail wrapper around the autoplay package CLI (Tools~/unityctl.sh of com.unpseudo.autoplay, found by
# autoplay-tool.sh: a local clone in Packages/ or the read-only Library/PackageCache copy).
# Adds the project's guarded settings (FMOD settings included) and keeps `tools/autoplay/unityctl.sh <cmd>` working.
# `build` is the project's own (CdpBuild.BuildAutoplay): the playtest logs (GameLog) are compiled in, every autoplay
# process writes them to its local log (read with `python tools/logs/logs.py --local …`), marked autoplay.
#   AUTOPLAY_VISUAL_PICKER=1  -> real card picker on screen + capture bursts (game option -autoplay-visual-picker)
#   AUTOPLAY_LOGS=online      -> also upload the GameLog lines to Firestore (same quota as the testers: on demand only)
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
export AUTOPLAY_PROJECT="$ROOT"
export AUTOPLAY_GUARDED_FILES="Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset ProjectSettings/ProjectSettings.asset ProjectSettings/GraphicsSettings.asset Assets/Settings/PC_RPAsset.asset"
export AUTOPLAY_ARGS="${AUTOPLAY_ARGS:-} ${AUTOPLAY_VISUAL_PICKER:+-autoplay-visual-picker}"
[ "${AUTOPLAY_LOGS:-}" = online ] && AUTOPLAY_ARGS="$AUTOPLAY_ARGS -playtest-logs-upload"

if [ "${1:-}" = "build" ]; then
  # Same guard as the package's build: an editor that ran with a broken script mapping can re-save gutted settings.
  # shellcheck disable=SC2086
  if ! git -C "$ROOT" diff --ignore-cr-at-eol --quiet -- $AUTOPLAY_GUARDED_FILES; then
    echo "guarded settings differ from git — restore them (git checkout) before building:" >&2
    # shellcheck disable=SC2086
    git -C "$ROOT" diff --ignore-cr-at-eol --stat -- $AUTOPLAY_GUARDED_FILES >&2
    exit 4
  fi
  # AUTOPLAY_STEAM=1 -> Steam flavor (CDP_STEAM) in Builds/AutoplaySteam/, for the steam-list scenarios.
  if [ "${AUTOPLAY_STEAM:-}" = 1 ]; then method=BuildAutoplaySteam; out=AutoplaySteam; else method=BuildAutoplay; out=Autoplay; fi
  result="$ROOT/Builds/$out/build-result.json"
  rm -f "$result"
  # A busy editor (heavy CPU elsewhere, a scene loading) answers "Main thread operation timed out after 5000ms" while the
  # delayCall still gets queued and the build still runs: wait for the result anyway. Any other failure is fatal and said.
  if ! queued=$(UNITY_NO_BANNER=1 unity command --project-path "$ROOT" eval --result-only \
      --code 'UnityEditor.EditorApplication.delayCall += CdpBuild.'"$method"'; return "queued";' 2>&1); then
    if echo "$queued" | grep -q "timed out"; then
      echo "build: the editor answered late (main thread busy); waiting for the build anyway" >&2
    else
      echo "build: could not queue the build in the editor:" >&2; echo "$queued" >&2
      exit 3
    fi
  fi
  for _ in $(seq 1 360); do  # 30 min
    [ -f "$result" ] && break
    sleep 5
  done
  [ -f "$result" ] || { echo "build: no result after 30 min (see the editor log)" >&2; exit 5; }
  cat "$result"; echo
  grep -q '"result":"Succeeded"' "$result"
  exit $?
fi

# A run plays the last build: a script changed since then would silently not be in it (2026-11-21, a new lever ignored
# by a filmed run). Refuse, unless AUTOPLAY_STALE_OK=1 (a run meant to replay the old build).
case "${1:-}" in play-build|play-net)
  if [ "${AUTOPLAY_STEAM:-}" = 1 ]; then out=AutoplaySteam; else out=Autoplay; fi
  stamp="$ROOT/Builds/$out/build-result.json"
  if [ -f "$stamp" ] && [ "${AUTOPLAY_STALE_OK:-}" != 1 ]; then
    newer=$(find "$ROOT/Assets" -name '*.cs' -newer "$stamp" -print 2>/dev/null | head -3)
    if [ -n "$newer" ]; then
      echo "stale build: scripts changed since Builds/$out was built — run 'tools/autoplay/unityctl.sh build' first" >&2
      echo "(AUTOPLAY_STALE_OK=1 to play the old build anyway). Newer:" >&2
      echo "$newer" >&2
      exit 6
    fi
  fi
;; esac

exec bash "$ROOT/tools/autoplay/autoplay-tool.sh" unityctl.sh "$@"
