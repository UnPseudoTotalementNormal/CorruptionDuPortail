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
#   unityctl.sh prune [days] [--dry-run]  drop images of PASSED runs older than N days (2), in every checkout
#   unityctl.sh park                      worktree done (PR merged): free its Library + Temp (~4 GB), keep code/Builds/runs
#   unityctl.sh unpark                    resume a parked worktree: re-seed Library from the main checkout (minutes)
#
# Environment:
#   AUTOPLAY_PROJECT        project root (default: current directory)
#   AUTOPLAY_GUARDED_FILES  files that must match git before a build (default: ProjectSettings + GraphicsSettings)
#   AUTOPLAY_ARGS           extra player args, e.g. "-autoplay-visual-picker" (game options)
#   AUTOPLAY_CLIENT_ARGS / AUTOPLAY_CLIENT1_ARGS   play-net only: args for every client / for client1 only
#   -autoplay-video in AUTOPLAY_ARGS / AUTOPLAY_CLIENT1_ARGS: film those processes (video.mp4 in each run folder)
#   AUTOPLAY_CLIENT1_RELAUNCH_ARGS / _DELAY (5 s)  play-net only: relaunch client1 once if its game dies first (crash-at)
#   AUTOPLAY_TIMESCALE (4)  AUTOPLAY_TIMEOUT (900 s)  AUTOPLAY_SCENARIO (build)
#   AUTOPLAY_KEEP_IMAGES_DAYS (2)  retention applied automatically before play-build / play-net
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

# Main checkout of this repository (= ROOT outside a git worktree).
MAIN="$(cd "$(git -C "$ROOT" rev-parse --path-format=absolute --git-common-dir 2>/dev/null)/.." 2>/dev/null && (pwd -W 2>/dev/null || pwd))" || MAIN="$ROOT"
same_dir() { [ "$(echo "${1%/}" | tr 'A-Z\\' 'a-z/')" = "$(echo "${2%/}" | tr 'A-Z\\' 'a-z/')" ]; }
prune_runs() { $PY "$HERE/prune_runs.py" --project "$ROOT" "$@" || true; }
editor_running() {  # an editor (GUI or batchmode) holds this checkout
  powershell.exe -NoProfile -Command "\$r='$ROOT'.Replace('\','/').TrimEnd('/').ToLower();
    if (Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ? { \$_.CommandLine -and \$_.CommandLine.Replace('\','/').ToLower().Contains(\$r) }) { exit 0 } else { exit 1 }"
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
    prune_runs --quiet
    args="-autoplay -autoplay-seed $seed -autoplay-port $port -autoplay-out $ROOT/AutoplayRuns"
    args="$args -autoplay-scenario ${AUTOPLAY_SCENARIO:-build} -autoplay-timescale ${AUTOPLAY_TIMESCALE:-4} ${AUTOPLAY_ARGS:-}"
    args="$args -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile $ROOT/Logs/autoplay-player-$seed.log"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$HERE/launch-background.ps1" \
      -Exe "$EXE" -TimeoutSeconds "${AUTOPLAY_TIMEOUT:-900}" -PlayerArgs "$args" -AlertsRoot "$ROOT/AutoplayRuns" || true
    $PY "$HERE/make_videos.py" "$(ls -td "$ROOT"/AutoplayRuns/*/ | head -1)" || true
    "$0" last-run
    ;;
  play-net)
    # 1 host + N real clients of the same build over loopback UDP, all without focus; then the desync comparison.
    clients="${2:?clients}"; seed="${3:-$RANDOM}"; port="${4:-7870}"
    prune_runs --quiet
    port=$($PY -c "
import socket
for p in range($port, 7900):
    s=socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try: s.bind(('127.0.0.1', p)); print(p); break
    except OSError: pass
    finally: s.close()")
    # Two runs started in the same second with the same seed would share a folder (journals mixed): claim it atomically.
    out="$ROOT/AutoplayRuns/net-$(date +%Y%m%d-%H%M%S)-c$clients-seed$seed"; base="$out"; n=1
    mkdir -p "$ROOT/AutoplayRuns"
    until mkdir "$out" 2>/dev/null; do n=$((n+1)); out="$base-$n"; done
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
  prune)
    shift; days="${1:-}"; [ -n "$days" ] && [ "${days#--}" = "$days" ] && shift || days=""
    prune_runs ${days:+--days "$days"} "$@"
    ;;
  park)
    # Reversible: only what `unpark` re-creates goes (Library, Temp). Code, branch, Builds (play-build still works) and
    # runs (pruned by retention) stay. Never on the main checkout, never under a live editor.
    if same_dir "$ROOT" "$MAIN"; then echo "park is for worktrees, not the main checkout ($ROOT)" >&2; exit 2; fi
    if editor_running; then echo "an editor is open on $ROOT — stop it first" >&2; exit 3; fi
    du -sh "$ROOT/Library" 2>/dev/null || true
    rm -rf "$ROOT/Library" "$ROOT/Temp"
    echo "parked $ROOT (resume: unityctl.sh unpark, then relaunch the headless editor)"
    ;;
  unpark)
    if [ -d "$ROOT/Library" ]; then echo "Library already present: nothing to do"; exit 0; fi
    if same_dir "$ROOT" "$MAIN" || [ ! -d "$MAIN/Library" ]; then echo "no main-checkout Library to seed from ($MAIN)" >&2; exit 2; fi
    # Same exclusions as the headless recipe: no compiled assemblies, Bee cache or pipeline port file of the main editor.
    rc=0; MSYS2_ARG_CONV_EXCL='*' robocopy "$(cygpath -w "$MAIN/Library")" "$(cygpath -w "$ROOT/Library")" /E /MT:16 /NFL /NDL /NJH /NP       /XF '*.lock' UnityLockfile /XD ScriptAssemblies Bee Pipeline || rc=$?
    [ "$rc" -lt 8 ] || { echo "robocopy failed ($rc)" >&2; exit 1; }
    rm -f "$ROOT/Library/Pipeline/.unity-pipeline-port"
    echo "Library re-seeded. Launch the headless editor (tools/HEADLESS_UNITY.md), then: unityctl.sh compile"
    ;;
  *) sed -n '2,28p' "$0"; exit 2 ;;
esac
