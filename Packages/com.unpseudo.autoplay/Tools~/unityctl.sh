#!/usr/bin/env bash
# Autoplay helper around the official Unity CLI (`unity`), for an Editor already open on the project — typically a
# headless one, which no modal dialog can block:
#   "<Unity.exe>" -batchmode -automated -projectPath <project> -logFile <project>/Logs/batch-editor.log
#
#   unityctl.sh compile                   refresh + recompile, print compile ground truth + CS errors
#   unityctl.sh playmode <filter>         PlayMode tests (explicit included), async + wait, summary
#   unityctl.sh editmode [filter]         EditMode tests, summary (failures listed)
#   unityctl.sh build                     Development player -> Builds/Autoplay/ (refuses if guarded settings drifted)
#   unityctl.sh play-build [seed] [port]  one autoplay game in that build, launched WITHOUT focus, always killed
#   unityctl.sh play-net <clients> [seed] [port]  host + N real network clients (separate processes), desync check
#   unityctl.sh last-run                  summary of the newest AutoplayRuns/*/report.json
#
# Environment:
#   AUTOPLAY_PROJECT        project root (default: current directory)
#   AUTOPLAY_GUARDED_FILES  files that must match git before a build (default: ProjectSettings + GraphicsSettings)
#   AUTOPLAY_ARGS           extra player args, e.g. "-autoplay-visual-picker" (game options)
#   AUTOPLAY_CLIENT_ARGS / AUTOPLAY_CLIENT1_ARGS   play-net only: args for every client / for client1 only
#   -autoplay-video in AUTOPLAY_ARGS / AUTOPLAY_CLIENT1_ARGS: film those processes (video.mp4 in each run folder)
#   AUTOPLAY_CLIENT1_RELAUNCH_ARGS / _DELAY (5 s)  play-net only: relaunch client1 once if its game dies first (crash-at)
#   AUTOPLAY_TIMESCALE (4)  AUTOPLAY_TIMEOUT (900 s)  AUTOPLAY_SCENARIO (build)
#
# Long commands block until done: run them in the background from an agent session.
set -euo pipefail
export UNITY_NO_BANNER=1
HERE="$(cd "$(dirname "$0")" && (pwd -W 2>/dev/null || pwd))"
ROOT="${AUTOPLAY_PROJECT:-$(pwd -W 2>/dev/null || pwd)}"
[ -d "$ROOT/Assets" ] && [ -d "$ROOT/ProjectSettings" ] || { echo "not a Unity project: $ROOT (set AUTOPLAY_PROJECT)" >&2; exit 2; }
U() { unity command --project-path "$ROOT" "$@"; }
PY="python -X utf8"
PRODUCT="$(sed -n 's/^  productName: //p' "$ROOT/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
EXE="$ROOT/Builds/Autoplay/${PRODUCT:-Game}.exe"

wait_ready() {
  for _ in $(seq 1 120); do
    s=$(U editor_status --result-only 2>/dev/null | $PY -c "import sys,json;d=json.load(sys.stdin);print(d.get('status'),d.get('compiling'),d.get('domainReloadInProgress'))" 2>/dev/null || true)
    [ "$s" = "ready False False" ] && return 0
    sleep 3
  done
  echo "editor not ready: $s" >&2; return 1
}

status_file() { echo "$ROOT/Temp/pipeline_test_status.json"; }
test_running() {
  local S; S="$(status_file)"
  [ -f "$S" ] && ! $PY -c "import json,sys;d=json.load(open(r'$S',encoding='utf-8-sig'));sys.exit(0 if d.get('status') in ('completed','failed','error') else 1)" 2>/dev/null
}

summarize_results() {  # reads a run_tests / status JSON on stdin
  $PY -c "
import sys,json;d=json.load(sys.stdin)
print(json.dumps(d.get('Summary') or d.get('summary')))
for t in (d.get('Results') or d.get('results') or []):
    r=str(t.get('Status') or t.get('Result') or t.get('result') or t.get('status')).lower()
    if r not in ('passed','pass'): print('FAIL', t.get('FullName') or t.get('Name') or t.get('name'), str(t.get('Message') or t.get('message'))[:800])"
}

case "${1:-}" in
  compile)
    # A recompile during a PlayMode run reloads the domain and silently kills the run: refuse.
    if test_running; then echo "a PlayMode test run is in progress — not recompiling" >&2; exit 3; fi
    U clear_console --result-only >/dev/null 2>&1 || true
    U eval --code 'UnityEditor.AssetDatabase.Refresh(); return "ok";' --result-only >/dev/null
    U recompile --result-only >/dev/null 2>&1 || true
    sleep 3; wait_ready
    U console_status --result-only | $PY -c "import sys,json;g=json.load(sys.stdin).get('groundTruth') or {};print('compilationFailed=',g.get('compilationFailed'))"
    U console --level error --tail 50 --result-only | $PY -c "
import sys,json
for e in json.load(sys.stdin).get('entries',[]):
    m=e.get('message','')
    if 'error CS' in m: print(m[:300])"
    ;;
  playmode)
    filter="${2:?filter}"
    rm -f "$(status_file)"
    U run_tests --mode PlayMode --filter "$filter" --include_explicit true --async_tests true --timeout 1800 --result-only >/dev/null
    until [ -f "$(status_file)" ] && ! test_running; do sleep 3; done
    summarize_results < "$(status_file)"
    ;;
  editmode)
    U run_tests --mode EditMode ${2:+--filter "$2"} --timeout 900 --result-only | summarize_results
    ;;
  build)
    # An editor that ever ran with a broken script mapping can silently re-save gutted settings (seen: an audio
    # middleware settings asset emptied => audio dead in the player). Refuse to build unless they match git.
    guarded="${AUTOPLAY_GUARDED_FILES:-ProjectSettings/ProjectSettings.asset ProjectSettings/GraphicsSettings.asset}"
    # shellcheck disable=SC2086
    if ! git -C "$ROOT" diff --ignore-cr-at-eol --quiet -- $guarded; then
      echo "guarded settings differ from git — restore them (git checkout) before building:" >&2
      # shellcheck disable=SC2086
      git -C "$ROOT" diff --ignore-cr-at-eol --stat -- $guarded >&2
      exit 4
    fi
    U build --target StandaloneWindows64 --outputPath "$EXE" --options '["Development"]' --confirm true --result-only >/dev/null
    until U build_status --result-only 2>/dev/null | $PY -c "import sys,json;d=json.load(sys.stdin);sys.exit(0 if d.get('status')=='completed' else 1)" 2>/dev/null; do sleep 5; done
    U build_status --result-only | $PY -c "
import sys,json;d=json.load(sys.stdin);r=d.get('report') or d.get('buildReport') or d
s=r.get('summary',r) if isinstance(r,dict) else {}
print('result=',s.get('result'),'errors=',s.get('totalErrors'))"
    ;;
  play-build)
    # Launched without focus (SW_SHOWNOACTIVATE) through launch-background.ps1, which hands the focus back if Unity
    # activates itself, waits (bounded) and ALWAYS kills the player at the end — no stray process keeps a port.
    seed="${2:-$RANDOM}"; port="${3:-7851}"
    args="-autoplay -autoplay-seed $seed -autoplay-port $port -autoplay-out $ROOT/AutoplayRuns"
    args="$args -autoplay-scenario ${AUTOPLAY_SCENARIO:-build} -autoplay-timescale ${AUTOPLAY_TIMESCALE:-4} ${AUTOPLAY_ARGS:-}"
    args="$args -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile $ROOT/Logs/autoplay-player-$seed.log"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$HERE/launch-background.ps1" \
      -Exe "$EXE" -TimeoutSeconds "${AUTOPLAY_TIMEOUT:-900}" -PlayerArgs "$args" || true
    $PY "$HERE/make_videos.py" "$(ls -td "$ROOT"/AutoplayRuns/*/ | head -1)" || true
    "$0" last-run
    ;;
  play-net)
    # 1 host + N real clients of the same build over loopback UDP, all without focus; then the desync comparison.
    clients="${2:?clients}"; seed="${3:-$RANDOM}"; port="${4:-7870}"
    port=$($PY -c "
import socket
for p in range($port, 7900):
    s=socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try: s.bind(('127.0.0.1', p)); print(p); break
    except OSError: pass
    finally: s.close()")
    out="$ROOT/AutoplayRuns/net-$(date +%Y%m%d-%H%M%S)-c$clients-seed$seed"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$HERE/launch-net.ps1" -Exe "$EXE" -Clients "$clients"       -Seed "$seed" -Port "$port" -OutRoot "$out" -TimeoutSeconds "${AUTOPLAY_TIMEOUT:-900}"       -CommonArgs "-autoplay-timescale ${AUTOPLAY_TIMESCALE:-4} ${AUTOPLAY_ARGS:-}"       -ClientArgs "${AUTOPLAY_CLIENT_ARGS:-}" -FirstClientArgs "${AUTOPLAY_CLIENT1_ARGS:-}"       -RelaunchArgs "${AUTOPLAY_CLIENT1_RELAUNCH_ARGS:-}" -RelaunchDelaySeconds "${AUTOPLAY_CLIENT1_RELAUNCH_DELAY:-5}" || true
    $PY "$HERE/make_videos.py" "$out" || true
    $PY "$HERE/compare_runs.py" "$out" || true
    ;;
  last-run)
    R=$(ls -td "$ROOT"/AutoplayRuns/*/ | head -1)
    $PY -c "
import json,os;d=json.load(open(os.path.join(r'$R','report.json'),encoding='utf-8-sig'))
print('run:', r'$R')
for k in ('game','scenario','seed','outcome','failureReason','realSeconds','errorCount'): print(k,'=',d.get(k))
print('facts:', ', '.join(d.get('facts',[])))
print('counters:', ', '.join(d.get('counters',[])))
print('trace:', ' > '.join(d.get('stateTrace',[])))
for r in d.get('finalRoster',[]): print('  ',r)
errs={}
for e in d.get('errors',[]): errs[e[:200]]=errs.get(e[:200],0)+1
for e,n in sorted(errs.items(), key=lambda x:-x[1])[:15]: print(f'  ERR x{n}', e)
print('files:', len(os.listdir(r'$R')))"
    ;;
  *) sed -n '2,25p' "$0"; exit 2 ;;
esac
