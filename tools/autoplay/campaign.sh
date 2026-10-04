#!/usr/bin/env bash
# Corruption du Portail autoplay campaign: every scenario in tools/autoplay/scenarios + random games, one summary.
#   tools/autoplay/campaign.sh [random builds (2)] [random nets (1)] [clients (3)]
# Needs an up-to-date dev build (tools/autoplay/unityctl.sh build). Long: run it in the background.
# Known, accepted issues are ignored for the random games (keep this list short and explained):
#   - CardEffect…effectData : card effects lose their data when the board re-creates cards (reported, left as is)
#   - ClientLoadedSynchronization : NGO NRE after the launcher's connect retry (suspected harness artefact)
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
cd "$ROOT" || exit 2
exec python -X utf8 "$ROOT/Packages/com.unpseudo.autoplay/Tools~/campaign.py" \
  --ctl "$ROOT/tools/autoplay/unityctl.sh" --project "$ROOT" \
  --scenarios "$ROOT/tools/autoplay/scenarios" \
  --random-builds "${1:-2}" --random-nets "${2:-1}" --clients "${3:-3}" --max-days 4 \
  --ignore "CardEffect(TechnoBeacon|BoolEnabler): effectData" "ClientLoadedSynchronization"
