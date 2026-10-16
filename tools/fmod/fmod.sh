#!/usr/bin/env bash
# FMOD Studio from the command line (no window): run a JS script on the project, or build the banks.
#   tools/fmod/fmod.sh script <file.js>    run a Studio script on FMODProject/Corrupted (save it inside the script)
#   tools/fmod/fmod.sh build [banks]       build the banks (default all) into Assets/FMODBanks
# Uses FMOD Studio 2.03.14 (the project is serialized Studio.02.03.00; 2.02 would migrate it badly).
# Close the project in FMOD Studio first: the command line tool cannot open a project another Studio holds.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
CL="${FMOD_STUDIO_CL:-/c/Program Files/FMOD SoundSystem/FMOD Studio 2.03.14/fmodstudiocl.exe}"
PROJECT="$ROOT/FMODProject/Corrupted/Corrupted.fspro"
case "${1:-}" in
  script) "$CL" -script "$(cd "$(dirname "$2")" && (pwd -W 2>/dev/null || pwd))/$(basename "$2")" "$PROJECT" ;;
  build) if [ -n "${2:-}" ]; then "$CL" -build -banks "$2" "$PROJECT"; else "$CL" -build "$PROJECT"; fi ;;
  *) sed -n '2,6p' "$0"; exit 1 ;;
esac
