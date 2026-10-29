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
  result="$ROOT/Builds/Autoplay/build-result.json"
  rm -f "$result"
  UNITY_NO_BANNER=1 unity command --project-path "$ROOT" eval --result-only \
    --code 'UnityEditor.EditorApplication.delayCall += CdpBuild.BuildAutoplay; return "queued";' >/dev/null || exit 3
  for _ in $(seq 1 360); do  # 30 min
    [ -f "$result" ] && break
    sleep 5
  done
  [ -f "$result" ] || { echo "build: no result after 30 min (see the editor log)" >&2; exit 5; }
  cat "$result"; echo
  grep -q '"result":"Succeeded"' "$result"
  exit $?
fi

exec bash "$ROOT/tools/autoplay/autoplay-tool.sh" unityctl.sh "$@"
