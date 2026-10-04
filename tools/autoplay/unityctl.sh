#!/usr/bin/env bash
# Autoplay helper around the official Unity CLI, for an Editor already open on THIS checkout
# (typically a headless one: Unity.exe -batchmode -automated -projectPath <repo> — no modal dialog can block it).
#
#   tools/autoplay/unityctl.sh compile                 # refresh + recompile, print compile ground truth + CS errors
#   tools/autoplay/unityctl.sh playmode <filter>       # run PlayMode tests async (explicit included), wait, summarize
#   tools/autoplay/unityctl.sh editmode [filter]       # run EditMode tests, summarize
#   tools/autoplay/unityctl.sh build                   # Development player -> Builds/Autoplay/ (autoplay bootstrap inside)
#   tools/autoplay/unityctl.sh play-build [seed] [port] # one windowed autoplay game in that build (real screenshots)
#   tools/autoplay/unityctl.sh last-run                # summarize the newest AutoplayRuns/*/report.json
#
# Long commands block until done: launch them in the background from an agent session.
set -euo pipefail
export UNITY_NO_BANNER=1
ROOT="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
U() { unity command --project-path "$ROOT" "$@"; }

wait_ready() {
  for _ in $(seq 1 120); do
    s=$(U editor_status --result-only 2>/dev/null | python -c "import sys,json;d=json.load(sys.stdin);print(d.get('status'),d.get('compiling'),d.get('domainReloadInProgress'))" 2>/dev/null || true)
    [ "$s" = "ready False False" ] && return 0
    sleep 3
  done
  echo "editor not ready: $s" >&2; return 1
}

test_running() {
  [ -f "$ROOT/Temp/pipeline_test_status.json" ] && ! python -c "import json,sys;d=json.load(open(r'$ROOT/Temp/pipeline_test_status.json',encoding='utf-8-sig'));sys.exit(0 if d.get('status') in ('completed','failed','error') else 1)" 2>/dev/null
}

case "${1:-}" in
  compile)
    # A recompile during a PlayMode run reloads the domain and silently kills the run: refuse.
    if test_running; then echo "a PlayMode test run is in progress — not recompiling" >&2; exit 3; fi
    U clear_console --result-only >/dev/null 2>&1 || true
    U eval --code 'UnityEditor.AssetDatabase.Refresh(); return "ok";' --result-only >/dev/null
    U recompile --result-only >/dev/null 2>&1 || true
    sleep 3; wait_ready
    U console_status --result-only | python -c "import sys,json;g=json.load(sys.stdin).get('groundTruth') or {};print('compilationFailed=',g.get('compilationFailed'))"
    U console --level error --tail 50 --result-only | python -c "
import sys,json
for e in json.load(sys.stdin).get('entries',[]):
    m=e.get('message','')
    if 'error CS' in m: print(m[:300])"
    ;;
  playmode)
    filter="${2:?filter}"
    rm -f "$ROOT/Temp/pipeline_test_status.json"
    U run_tests --mode PlayMode --filter "$filter" --include_explicit true --async_tests true --timeout 1800 --result-only >/dev/null
    S="$ROOT/Temp/pipeline_test_status.json"
    until [ -f "$S" ] && python -c "import json,sys;d=json.load(open(r'$S',encoding='utf-8-sig'));sys.exit(0 if d.get('status') in ('completed','failed','error') else 1)" 2>/dev/null; do sleep 3; done
    python -c "
import json;d=json.load(open(r'$S',encoding='utf-8-sig'))
print(json.dumps(d.get('summary')))
for t in (d.get('Results') or d.get('results') or []):
    r=t.get('Status') or t.get('Result') or t.get('result'); print(r, t.get('FullName') or t.get('Name') or t.get('name') or '', str(t.get('Message') or t.get('message') or '')[:800])"
    ;;
  editmode)
    U run_tests --mode EditMode ${2:+--filter "$2"} --timeout 900 --result-only | python -c "
import sys,json;d=json.load(sys.stdin);print(json.dumps(d.get('Summary') or d.get('summary')))
for t in (d.get('Results') or d.get('results') or []):
    r=str(t.get('Status') or t.get('Result') or t.get('result') or t.get('status')).lower()
    if r not in ('passed','pass'): print('FAIL', t.get('FullName') or t.get('Name') or t.get('name'), str(t.get('Message') or t.get('message'))[:300])"
    ;;
  build)
    # Development player (DEVELOPMENT_BUILD → AutoplayBootstrap compiled in) into Builds/Autoplay/ (gitignored).
    # An editor that ever ran with broken script mapping can silently re-save gutted settings (seen: FMODStudioSettings
    # 517 -> 5 lines => FMOD dead in the player). Refuse to build unless critical settings match git (EOL ignored).
    if ! git -C "$ROOT" diff --ignore-cr-at-eol --quiet -- Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset \
         ProjectSettings/ProjectSettings.asset ProjectSettings/GraphicsSettings.asset Assets/Settings/PC_RPAsset.asset; then
      echo "critical settings differ from git — restore them (git checkout) before building:" >&2
      git -C "$ROOT" diff --ignore-cr-at-eol --stat -- Assets/Plugins/FMOD/Resources ProjectSettings Assets/Settings >&2
      exit 4
    fi
    out="$ROOT/Builds/Autoplay/CorruptionDuPortail.exe"
    U build --target StandaloneWindows64 --outputPath "$out" --options '["Development"]' --confirm true --result-only >/dev/null
    until U build_status --result-only 2>/dev/null | python -c "import sys,json;d=json.load(sys.stdin);sys.exit(0 if d.get('status')=='completed' else 1)" 2>/dev/null; do sleep 5; done
    U build_status --result-only | python -c "
import sys,json;d=json.load(sys.stdin);r=d.get('report') or d.get('buildReport') or d
s=r.get('summary',r) if isinstance(r,dict) else {}
print('result=',s.get('result'),'errors=',s.get('totalErrors'),'time=',s.get('totalTime'),'size=',s.get('totalSize'))"
    ;;
  play-build)
    # One windowed autoplay game in the dev build: real rendering → real screenshots. The process is ALWAYS killed
    # at the end (timeout or not) so no stray player keeps a UDP port bound.
    exe="$ROOT/Builds/Autoplay/CorruptionDuPortail.exe"
    seed="${2:-$RANDOM}"; port="${3:-7851}"; timeout_s="${AUTOPLAY_TIMEOUT:-900}"
    # Launched without focus (SW_SHOWNOACTIVATE) through launch-background.ps1, which also hands the focus back if
    # Unity activates itself, waits (bounded) and ALWAYS kills the player at the end.
    args="-autoplay -autoplay-seed $seed -autoplay-port $port -autoplay-out $ROOT/AutoplayRuns"
    args="$args -autoplay-timescale ${AUTOPLAY_TIMESCALE:-4} ${AUTOPLAY_VISUAL_PICKER:+-autoplay-visual-picker} ${AUTOPLAY_SOUND:+-autoplay-sound}"
    args="$args -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile $ROOT/Logs/autoplay-player-$seed.log"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ROOT/tools/autoplay/launch-background.ps1"       -Exe "$exe" -TimeoutSeconds "$timeout_s" -PlayerArgs "$args" || true
    "$0" last-run
    ;;
  last-run)
    R=$(ls -td "$ROOT"/AutoplayRuns/*/ | head -1)
    python -c "
import json,os;d=json.load(open(os.path.join(r'$R','report.json'),encoding='utf-8-sig'))
print('run:', r'$R')
for k in ('outcome','failureReason','realSeconds','days','powersUsed','votesCast','errorCount'): print(k,'=',d[k])
print('trace:', ' > '.join(d['stateTrace']))
for r in d['finalRoster']: print('  ',r)
for e in d['errors'][:20]: print('  ERR',e[:240])
print('files:', len(os.listdir(r'$R')))"
    ;;
  *) sed -n '2,14p' "$0"; exit 2 ;;
esac
