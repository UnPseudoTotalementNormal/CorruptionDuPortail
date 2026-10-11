#!/usr/bin/env bash
# Corruption du Portail wrapper around the autoplay package CLI (Tools~/unityctl.sh of com.unpseudo.autoplay, found by
# autoplay-tool.sh: a local clone in Packages/ or the read-only Library/PackageCache copy).
# Adds the project's guarded settings (FMOD settings included) and keeps `tools/autoplay/unityctl.sh <cmd>` working.
#   AUTOPLAY_VISUAL_PICKER=1  -> real card picker on screen + capture bursts (game option -autoplay-visual-picker)
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
export AUTOPLAY_PROJECT="$ROOT"
export AUTOPLAY_GUARDED_FILES="Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset ProjectSettings/ProjectSettings.asset ProjectSettings/GraphicsSettings.asset Assets/Settings/PC_RPAsset.asset"
export AUTOPLAY_ARGS="${AUTOPLAY_ARGS:-} ${AUTOPLAY_VISUAL_PICKER:+-autoplay-visual-picker}"
exec bash "$ROOT/tools/autoplay/autoplay-tool.sh" unityctl.sh "$@"
