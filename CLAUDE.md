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
