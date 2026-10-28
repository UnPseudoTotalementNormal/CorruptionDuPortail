#!/usr/bin/env bash
# FMOD Studio from the command line (no window): run a JS script on the project, build the banks, check them.
#   tools/fmod/fmod.sh script <file.js>    run a Studio script on FMODProject/Corrupted (save it inside the script)
#   tools/fmod/fmod.sh build               build every bank into Assets/FMODBanks, then check them against the project
#   tools/fmod/fmod.sh check               banks vs project, event by event (what CI and the commit hook run)
# Uses FMOD Studio 2.03.14 (the project is serialized Studio.02.03.00; 2.02 would migrate it badly).
# Close the project in FMOD Studio first: the command line tool cannot open a project another Studio holds.
# Always build every bank: a bank built alone leaves its event paths out of Master.strings.bank. See README.md.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
CL="${FMOD_STUDIO_CL:-/c/Program Files/FMOD SoundSystem/FMOD Studio 2.03.14/fmodstudiocl.exe}"
PROJECT="${FMOD_PROJECT:-$ROOT/FMODProject/Corrupted/Corrupted.fspro}"  # FMOD_PROJECT: a copy, for tests
check() { python "$ROOT/tools/fmod/banks.py" diff > /dev/null || { python "$ROOT/tools/fmod/banks.py" diff; exit 1; }; echo "banks match FMODProject"; }
case "${1:-}" in
  script) "$CL" -script "$(cd "$(dirname "$2")" && (pwd -W 2>/dev/null || pwd))/$(basename "$2")" "$PROJECT" ;;
  build) "$CL" -build "$PROJECT"; check ;;
  check) check ;;
  *) sed -n '2,8p' "$0"; exit 1 ;;
esac
