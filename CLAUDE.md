# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Corruption du Portail** — Asymmetric multiplayer social deduction game (Werewolf/Mafia style), Unity **6000.2.6f2**.

Stack: Unity + Netcode for GameObjects (NGO) + FMOD + UniTask + DOTween + Facepunch (Steam) transport. Main branch: `Dev` (target for PRs).

## Documentation source of truth

`_bmad-output/` holds the generated project documentation maintained by the BMad / GDS workflows. Entry point: `_bmad-output/index.md`. Read the relevant doc before non-trivial work on a system. Refresh with `/gds-document-project`.

**For AI agents implementing code**: also read `_bmad-output/project-context.md` — 270 load-bearing rules (Unity / NGO / FMOD / UniTask / asmdef / testing / performance / anti-patterns). Refresh with `/gds-generate-project-context`.

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
- **Audio = FMOD** via `AudioSystem/GameAudioManager`. Never `AudioSource` for gameplay sounds.

## Build / Test / Run — use Unity MCP

| Action | Tool |
|---|---|
| Run tests (EditMode + PlayMode) | `mcp__UnityMCP__run_tests` |
| Read compile errors after a change | `mcp__UnityMCP__read_console` |
| Inspect / mutate scene | `mcp__UnityMCP__manage_scene`, `manage_gameobject` |
| Edit scripts | Prefer `Edit` tool; `mcp__UnityMCP__manage_script` for create/delete |
| Build player | `mcp__UnityMCP__manage_build` |
| Enter play mode | `mcp__UnityMCP__manage_editor` |

After any code change: poll `read_console` for compile errors before assuming anything works. After a feature completes: run `mcp__UnityMCP__run_tests` (filter by category/assembly when relevant). Add unit tests for new powers/roles, network flows, non-trivial logic, or bugs with subtle root causes.

## Discord task board

The team's task list is a **Discord forum channel** (`liste-de-taches`, one thread = one task). No Discord MCP — talk to it via the **REST API** (`https://discord.com/api/v10`) using the bot token. Credentials live in `.env` at repo root (gitignored):

| Var | Meaning |
|---|---|
| `DISCORD_BOT_TOKEN` | Auth: header `Authorization: Bot <token>` |
| `DISCORD_GUILD_ID` | Server id |
| `DISCORD_TASK_CHANNEL_ID` | The forum channel |
| `DISCORD_DONE_TAG_ID` | "Résolu" tag id |

Forum tags (id → name): `1524509766972604517` Haute Priorité · `1524509938116726984` Moyenne Priorité · `1524509912019894373` Faible Priorité · `1524509841043882048` MODELE 3D · `1524511158445277268` Bug · `1524722157689638953` En Cours · `1524514526660263956` Résolu.

Common ops (all authed with the Bot header):
- **List tasks** — `GET /guilds/{guild}/threads/active` (filter `parent_id == channel`) + `GET /channels/{channel}/threads/archived/public`. Open = tag not in `applied_tags`; done = `DISCORD_DONE_TAG_ID` present.
- **Read a task** — `GET /channels/{threadId}` (metadata + `applied_tags`) then `GET /channels/{threadId}/messages` (thread body = first message).
- **Mark in-progress / complete** — `PATCH /channels/{threadId}` with `{"applied_tags":[...]}` (full replacement — read current tags, add/swap, write back). Complete = add Résolu (+ archive via `"archived":true`); starting = add "En Cours".
- **Create a task** — `POST /channels/{channel}/threads` with `{"name":..., "applied_tags":[...], "message":{"content":...}}`.

**Always confirm with Poyo before any write** (PATCH/POST) to the board — same rule as commits.

Gotchas: bot token as a `python` argv gets mangled → 403; pass it via **env var** or use `curl`. Git Bash `curl -o /tmp/x` writes a path the native Windows `python` can't read → write to the scratchpad dir with an absolute path. Console is cp1252 → accents print as `�` but the JSON data is fine UTF-8.
