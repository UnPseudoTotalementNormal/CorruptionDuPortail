# Driving Unity headless — own editor instance + official Unity CLI

How an agent (or a script) compiles, tests and builds this project **without** a visible editor, without touching the
user's own editor, and without ever being blocked by a modal dialog. Verified on Unity 6000.5.0f1 with
`com.unity.pipeline` 0.8.0-exp.1 and the `unity` CLI 1.0.0-beta.12 (winget `Unity.CLI`).

## Why headless

- A GUI editor blocks on any modal dialog (package repair prompts, "NetworkObject missing", "save changes?"…) and
  stops processing CLI commands until a human clicks (`unity command editor_status` then reports `blocked_by_dialog`).
- In **batchmode** `EditorUtility.DisplayDialog` answers "cancel" immediately (`DisplayDialog` → false,
  `DisplayDialogComplex` → 1, checked with `unity command eval`). Nothing can block an unattended session, and nothing
  appears on the user's screen.
- One editor per checkout: each worktree gets **its own** instance. Headless is the **default**; an agent works in the
  user's open (visual) editor only when the user asks for it, and asks when it is unclear which editor to use.

## 1. Start an instance on a checkout

```bash
# Optional, saves a long first import on a fresh worktree: seed its Library from the main checkout.
# Exclude compiled assemblies, the Bee cache and the pipeline port file (see traps below).
# Same thing in one command: tools/autoplay/unityctl.sh unpark (and `park` frees it again once the work is merged).
robocopy "<main>\Library" "<worktree>\Library" /E /MT:16 /XF *.lock UnityLockfile /XD ScriptAssemblies Bee Pipeline
# (robocopy exit code 1 = files copied = success)

# Never keep a copied port file: it points at the MAIN editor.
rm -f Library/Pipeline/.unity-pipeline-port

# Launch (Git Bash, from the checkout root). No -quit: the editor stays alive and serves the CLI.
"/c/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Unity.exe" -batchmode -automated \
  -projectPath "$(pwd -W)" -logFile "$(pwd -W)/Logs/batch-editor.log"
```

From an agent session: launch it as a **background** task (it runs until killed). Background tasks are capped at
2 hours: when the instance disappears, relaunch it the same way.

Wait until it is ready and answers for **this** checkout:

```bash
export UNITY_NO_BANNER=1
unity command --project-path "$(pwd -W)" editor_status --result-only   # status=ready, compiling=false, projectPath = this checkout
unity pipeline list                                                     # one row per editor, each with its own port (7800, 7801…)
```

Stop it by PID, after checking its command line contains `-batchmode` and this checkout's path (never the main
editor's PID). The Hub command line of a GUI editor contains an `-accessToken`: never print it.

## 2. Drive it with the Unity CLI

Always pass `--project-path "$(pwd -W)"` (several editors may be running). `--result-only` gives compact JSON.

| Need | Command |
|---|---|
| Ready / compiling? | `unity command editor_status` |
| Recompile + errors | `unity command eval --code 'UnityEditor.AssetDatabase.Refresh(); return "ok";'` then `unity command recompile`, poll `editor_status`, then `unity command console_status` (`groundTruth.compilationFailed`) and `unity command console --level error --tail 50` |
| EditMode tests | `unity command run_tests --mode EditMode [--filter X] --timeout 900` (synchronous) |
| PlayMode tests | `unity command run_tests --mode PlayMode --filter X --async_tests true [--include_explicit true]`, then poll `Temp/pipeline_test_status.json` until `status` is `completed` (a synchronous PlayMode run is refused: entering play mode reloads the domain) |
| Run C# in the editor | `unity command eval --code '…; return x;'` (a statement body: it needs `return`) |
| New script | `unity command create_folder --path …` then `unity command create_script --name X --path …`, then write its content (a Unity-side create avoids silent compile exclusion) |
| Packages | `unity command package_resolve` (needed after adding an embedded package) |
| Dev player build | `unity command build --target StandaloneWindows64 --outputPath <exe> --options '["Development"]' --confirm true`, poll `build_status` |
| Discover | `unity command --query <term> [--detail full]` |

The wrapper `tools/autoplay/unityctl.sh` does all of this: `compile`, `editmode [filter]`, `playmode <filter>`,
`build`, `play-build`, `play-net`, `last-run` (see `Packages/com.unpseudo.autoplay/README.md` for autoplay).

## 3. What a headless editor cannot do

- **The end of frame never comes in batchmode** (`WaitForEndOfFrame`, `Awaitable.EndOfFrameAsync`,
  `UniTask.WaitForEndOfFrame()` never resume). This game waits on it (`GameManager.WaitAFrameAndNextGameState`…), so a
  full game stalls in a batchmode editor, and no screenshot can be taken there.
  ⇒ headless editor = compile, unit/network tests, builds; **complete games and screenshots = a windowed
  Development build** (`tools/autoplay/unityctl.sh build`, then `play-build` / `play-net`, launched without focus).

## 4. Traps (each one was hit)

| Trap | What happens | Rule |
|---|---|---|
| Copied Library with its `ScriptAssemblies` / `Bee` | script↔class mapping of the other checkout; a prefab loses a component (seen: `TransformCompositorComponent` on `Card` → NRE → game stalled) | exclude them when seeding (recommended, not yet re-verified from scratch); repair: `CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)` then force-reimport the script and the prefabs |
| Copied `.unity-pipeline-port` | CLI calls with `--project-path <worktree>` reach the main editor | delete it before launching |
| Cold GUI open of a seeded Library | assets imported before scripts load: settings re-saved gutted (`FMODStudioSettings.asset` 517 → 5 lines = no audio in builds) | open headless only; `git checkout` such files; `unityctl.sh build` refuses to build if guarded settings drifted from git |
| Recompiling during a PlayMode run | domain reload kills the run (no report) and can leak its UDP socket | `unityctl.sh compile` refuses while a run is in flight; autoplay picks a free port |
| Editor import noise | ~228 `.shadergraph.meta`, `link.xml` deletion, FMOD cache, URP/Graphics settings rewritten | never commit them: stage explicit files only; `git checkout -- Assets/AddressableAssetsData/link.xml*` |
| `bash` from Python on Windows | resolves to WSL's `System32\bash.exe` | call Git Bash explicitly (`C:\Program Files\Git\bin\bash.exe`) |
| Git Bash paths in native Python | `/c/Users/…` unreadable | use `pwd -W` (`C:/Users/…`) |
| PowerShell variables | case-insensitive: `$clientArgs` overwrites a `$ClientArgs` parameter | distinct names |
| `core.autocrlf=true` worktrees | FMOD macOS `Info.plist` rewritten CRLF → blocking "Repair FMOD libraries" dialog | `.gitattributes` keeps them LF |

## 5. Ports

| What | Ports |
|---|---|
| `com.unity.pipeline` editor servers (TCP, loopback) | 7800, 7801… one per running editor |
| Runtime pipeline in dev players (if ever enabled) | 7900–7949 |
| Autoplay games (UDP) | 7850–7899 — never 7777 / 7788 (playtests, PlayMode suites) |
