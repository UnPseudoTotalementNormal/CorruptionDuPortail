# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Corruption du Portail** — Asymmetric multiplayer social deduction game (Werewolf/Mafia style), Unity **6000.5.0f1**.

Stack: Unity + Netcode for GameObjects (NGO) + UTP/Unity Relay + Facepunch (Steam) transport + FMOD + UniTask + DOTween + uGUI/UI Toolkit. Main branch: `Dev` (target for PRs).

## Documentation

Entry point: `_bmad-output/index.md`. **Before writing game code, read `_bmad-output/project-context.md`**: the project-specific rules whose violation compiles clean and fails silently (DI lanes, NGO/UITK gotchas, test harness traps). It is hand-maintained, so add a rule when you hit a new silent trap and keep it short.

Workflow skills kept: `gds-quick-dev` (spec + implement), `gds-investigate` (forensic bug case), `gds-code-review` (adversarial review; required before merging stories tagged `# REVIEW-REQUIRED`). Shipped specs move to `_bmad-output/archive/specs/`. Archive, never delete.

## UI work

Before designing or changing any screen: read Poyo's UI slop list (`~/.claude/ui-slop.md`, outside the repo, kept
up to date at his request) and show mockups first with the `maquettes` skill (`tools/maquettes/`, served on port
5198). He answers on the mockup site itself (`<subject>/retours.txt`): read it, never re-ask the same choices.

## Commits

- **Never** add Claude / AI as a commit author or co-author. No `Co-Authored-By: Claude ...` trailers, no AI attribution in commits or PRs.
- **English** only — subject and body.
- **Always include a body** explaining the *why*. Never subject-only, even for `ci`/`chore`/`docs`.
- Subject = conventional commits. Body adds a `UX:` line when the change affects the player experience; skip for pure infra/CI/tooling but still write a body.
- Commit bodies are surfaced to Discord by `Build.yml` — the `UX:` line doubles as a real-time team briefing.

Format:
```
feat(ui): animate role picker entrance/exit

Tween the role card in and out on a dedicated layer so the reveal is progressive.

UX: the player sees their role appear gradually — reduces confusion during role distribution.
```

## Critical patterns (must follow — silent breakage if ignored)

- **`GetSafeRpcTarget(clientId)`** — Wrap every RPC target. `clientId >= 100` is a simulated bot; the Host intercepts the RPC instead of sending it over the wire. Direct `ServerRpc/ClientRpc` calls without this wrapper silently break the bot-debug flow.
- **`IsLocalOrSimulated(clientId)`** — Use instead of `IsLocalClient` anywhere the Host may act on behalf of a simulated identity. Plain `IsLocalClient` is wrong in this codebase.
- **Server authority strict** — Mutate game state on server only. Clients propose via `ServerRpc`s.
- **Async = UniTask** (`UniTask`, `UniTaskVoid`), never `System.Threading.Tasks.Task`.
- **Audio = FMOD** via `AudioSystem/GameAudioManager`. Never `AudioSource` for gameplay sounds. `FMODProject/` is
  the source of every sound: build **every** bank from it and commit it with `Assets/FMODBanks/` (`tools/fmod/README.md`;
  the `fmod_guard` hook and CI check it).
- **Every feature ships with its playtest logs** — in the same commit, unasked: `GameLog.Info|Warn("cat", "evt.name", key, value…)` for each player action (with its target), each silent refusal (early `return` on an input, with the reason), each step of a sequence, each server decision. A feature without logs is invisible in playtest reports. Rules: `project-context.md` § Playtest logs. The `log_guard` hook blocks a game-code commit with no `GameLog` call; write `Logs: none (<why>)` in the body only when there is truly nothing to log (shader, pure refactor).

## Build / Test / Run — use the official Unity CLI (MCP = fallback only)

**Priority: the official Unity CLI** (`unity`, installed via winget) driving the open Editor through the `com.unity.pipeline` package. Use it by default and keep learning it — lean on the `unity-cli` skill and `unity command --query <term>` to discover commands. The CoplayDev Unity MCP (`mcp__UnityMCP__*`) stays installed **only as a fallback** when the CLI can't do something or is broken.

Run from Git Bash at repo root with `UNITY_NO_BANNER=1`; add `--result-only` for compact JSON.

| Action | Unity CLI (default) | MCP fallback |
|---|---|---|
| Editor ready / compiling? | `unity command editor_status` | `editor_state` resource |
| Compile errors after a change | `unity command console_status` (`groundTruth.compilationFailed`) then `unity command console --level error --tail 20` | `read_console` |
| Run tests | `unity command run_tests --mode EditMode\|PlayMode --filter <name> --timeout 600` (pipe JSON to a file — it lists every test) | `run_tests` + `get_test_job` |
| Run C# in the Editor (read SO/prefab data, inspect state) | `unity command eval --code '...; return x;'` (statement body — needs `return`) or `run_script` | `execute_code` is **broken** here |
| Inspect / mutate scene, GameObjects, components | `get_scene_hierarchy`, `find_gameobjects`, `get_component_properties`, `set_serialized_field`, `batch` (transactional, one Undo) | `manage_scene`, `manage_gameobject` |
| Edit scripts | Prefer the `Edit` tool; `unity command create_script` for new `.cs` (Unity-side create avoids silent compile exclusion) | `manage_script` |
| Build player | `unity command build` / headless `unity build <path>` | `manage_build` |

**Agents: by default, drive your OWN headless editor.** Work in the user's open (visual) editor only when the user
asks for it; when it is unclear which editor to use, ask — do not guess. Launch a headless one per checkout with
`Unity.exe -batchmode -automated -projectPath <checkout> -logFile <checkout>/Logs/batch-editor.log` (background task)
and always pass `--project-path` to `unity command`. Batchmode auto-cancels every modal dialog, so nothing can block
the session and nothing shows on the user's screen. It cannot finish a game or take screenshots (the end of frame
never comes): complete games run in a windowed dev build via autoplay. Full recipe, CLI commands, traps and ports:
`tools/HEADLESS_UNITY.md`. Wrapper: `tools/autoplay/unityctl.sh` (`compile`, `editmode`, `playmode`, `build`,
`play-build`, `play-net`, `last-run`). In-game checks with bots: the `autoplay` skill.
The autoplay framework is the package `com.unpseudo.autoplay`, its own repository pinned by tag in
`Packages/manifest.json`: generic changes go there (never edit `Library/PackageCache`), see `tools/autoplay/REFERENCE.md`.

Playtest bug reports (Wouh on Discord…): read what the game recorded online first, with the `playtest-logs` skill
(`tools/logs/logs.py`, Firestore project `corruption-du-portail`).

After any code change: check `console_status` for compile errors before assuming anything works. After a feature completes: run the tests (filter when relevant). Add unit tests for new powers/roles, network flows, non-trivial logic, or bugs with subtle root causes.

## When a preventable problem slips through

A forgotten rule or an already-known trap is a harness gap, not a person's fault. Do not just fix the instance: find
what was missing (a hook that could check it, a rule absent from this file, a skill step, a doc too long to be read,
information never written down) and fix it at the strongest level that fits: hook > CLAUDE.md > skill >
project-context.md > memory. Example: playtest logs were skipped on features although the rule existed in
project-context.md → `log_guard` hook + Critical patterns line + skill steps.

## AI working time

Hooks journal every span of AI work (branch, prompt, tools, tags) into the main checkout's `.claude/timerecorder/`;
the Unity calendar shows it. Questions or corrections about worked time ("halve the autoplay time of the last 3 days")
go through `tools/timerecorder/tr.py` (dry run first, confirm with Poyo before `--yes`). See `tools/timerecorder/README.md`.

## Discord task board

The team's task list is a **Discord forum channel** (`liste-de-taches`, one thread = one task). No Discord MCP — talk to it via the **REST API** (`https://discord.com/api/v10`) using the bot token. Credentials live in `.env` at repo root (gitignored):

| Var | Meaning |
|---|---|
| `DISCORD_BOT_TOKEN` | Auth: header `Authorization: Bot <token>` |
| `DISCORD_GUILD_ID` | Server id |
| `DISCORD_TASK_CHANNEL_ID` | The forum channel |
| `DISCORD_DONE_TAG_ID` | "Résolu" tag id |

Forum tags (id → name): `1524509766972604517` Haute Priorité · `1524509938116726984` Moyenne Priorité · `1524509912019894373` Faible Priorité · `1524509841043882048` MODELE 3D · `1524511158445277268` Bug · `1524722157689638953` En Cours · `1524514526660263956` Résolu.

**Reading = `tools/discord/discord_tasks.py`** (read-only, stdlib, finds `.env` from a worktree too, UTF-8 output). Never hand-roll curl for reads: most tasks are *archived* (inactive forum threads auto-archive), threads can exceed one 50-message page, and bodies carry attachments/embeds/mentions.
- `check` — bot, Message Content intent ON/OFF, tag list, task count.
- `list [--open|--done|--all] [--json]` — active + archived (public/private, paginated); default open.
- `read <thread id | title substring> [--download DIR] [--json]` — every message: starter post, attachments (`--download` saves images so you can `Read` them), embeds, replies, reactions, polls, mentions resolved to names.
- `dump [--open|--done|--all] [--download DIR] [--json]` — `read` for every task.

Writes stay raw REST (all authed with the Bot header):
- **Mark in-progress / complete** — `PATCH /channels/{threadId}` with `{"applied_tags":[...]}` (full replacement — read current tags, add/swap, write back). Complete = add Résolu (+ archive via `"archived":true`); starting = add "En Cours".
- **Create a task** — `POST /channels/{channel}/threads` with `{"name":..., "applied_tags":[...], "message":{"content":...}}`.

**Always confirm with Poyo before any write** (PATCH/POST) to the board — same rule as commits.

Gotchas: bot token as a `python` argv gets mangled → 403; pass it via **env var** or use `curl`. Git Bash `curl -o /tmp/x` writes a path the native Windows `python` can't read → write to the scratchpad dir with an absolute path. Console is cp1252 → accents print as `�` but the JSON data is fine UTF-8. Bodies (`content`, `attachments`, `embeds`) need the privileged **Message Content intent** (Developer Portal → bot app → Bot → Privileged Gateway Intents); without it Discord returns them empty, even over REST. `discord_tasks.py` warns when it is OFF; then ask Poyo to enable it (or to paste the text).
