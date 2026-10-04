#!/usr/bin/env bash
# Corruption du Portail wrapper around the autoplay package CLI (Packages/com.unpseudo.autoplay/Tools~/unityctl.sh).
# Adds the project's guarded settings (FMOD settings included) and keeps `tools/autoplay/unityctl.sh <cmd>` working.
#   AUTOPLAY_VISUAL_PICKER=1  -> real card picker on screen + capture bursts (game option -autoplay-visual-picker)
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
export AUTOPLAY_PROJECT="$ROOT"
export AUTOPLAY_GUARDED_FILES="Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset ProjectSettings/ProjectSettings.asset ProjectSettings/GraphicsSettings.asset Assets/Settings/PC_RPAsset.asset"
export AUTOPLAY_ARGS="${AUTOPLAY_ARGS:-} ${AUTOPLAY_VISUAL_PICKER:+-autoplay-visual-picker}"
exec bash "$ROOT/Packages/com.unpseudo.autoplay/Tools~/unityctl.sh" "$@"
