# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Corruption du Portail** — Asymmetric multiplayer social deduction game (Werewolf/Mafia style), Unity **6000.2.6f2**.

Stack: Unity + Netcode for GameObjects (NGO) + FMOD + UniTask + DOTween + Facepunch Steam transport.

Main branch: `Dev` (target for PRs).

## Commits

- **Never** add Claude / AI as a commit author or co-author. Do not add `Co-Authored-By: Claude ...` trailers or any AI attribution to commits or PRs.

## Build / Test / Run

**Use Unity MCP whenever possible** — direct CLI Unity builds are not the primary workflow.

| Action | Tool |
|---|---|
| Run all tests (EditMode + PlayMode) | `mcp__UnityMCP__run_tests` |
| Read compile errors after code change | `mcp__UnityMCP__read_console` |
| Inspect / mutate scene | `mcp__UnityMCP__manage_scene`, `manage_gameobject` |
| Edit scripts | Prefer `Edit` tool; `mcp__UnityMCP__manage_script` for create/delete |
| Build player | `mcp__UnityMCP__manage_build` |
| Enter play mode | `mcp__UnityMCP__manage_editor` |

**Test workflow**:
1. After any code change, poll `read_console` for compile errors before assuming anything works.
2. After feature completion, run `mcp__UnityMCP__run_tests` (filter by category or assembly when relevant).
3. Add unit tests when adding non-trivial logic, new powers/roles, network flows, or fixing bugs with subtle root causes. Skip for pure Unity boilerplate.

Test assemblies:
- `Assets/Scripts/Tests/Editor/` — pure C# logic (extensions, validators, parsers). Uses NSubstitute.
- `Assets/Scripts/Tests/PlayMode/` — networked tests via `NetworkTestHelper.cs` (multi-client simulation), powers, roles, board, smartphone, lobby.

CI: `.github/workflows/unity-tests.yml` exists but is currently gated with `if: false` (disabled). `Build.yml` runs game-ci builds on push.

## Architecture

### Big picture
Server-authoritative NGO architecture. The "superpower" is the **Gateway RPC** system that lets the Host simulate additional bot/player identities locally for debugging full lobbies solo.

```
GameManager → GameState Router → NetworkGatewaySystem → CharacterManager → PowerManager
```

### Game loop (`GameState`)
`Lobby` → `Introduction` → `Awakening` → `Chaining` → `Vote` → `Recap` → `GameEnding`

Server dictates state transitions; cinematics/powers run async via **UniTask**.

### Critical patterns (must follow)

- **`GetSafeRpcTarget(clientId)`** — Wrap every RPC target. If `clientId >= 100` it is a simulated bot; the Host intercepts the RPC instead of sending it over the wire. Direct `ServerRpc/ClientRpc` calls without this wrapper will silently break the bot-debug flow.
- **`IsLocalOrSimulated(clientId)`** — Use instead of `IsLocalClient` checks anywhere the Host may act on behalf of a simulated identity. Plain `IsLocalClient` is wrong in this codebase.
- **Server authority strict** — Mutate game state on server only. Clients propose via ServerRpcs.

### Feature-based layout (`Assets/Scripts/`)
Each system is self-contained under its own folder. Notable systems:

- `Network/` — Gateway RPC, simulated player registry, NGO bootstrap.
- `Characters/` — Identity, factions, role distribution, powers (`Characters/Powers/`).
- `GameLogic/` — `GameManager`, state router, chaining, vote, victory.
- `Board/` — 3D card rendering, animations (DOTween), board cameras, character/power bars.
- `Smartphone/` — In-game 2D smartphone OS hub (chat, notes, info tables).
- `ChatSystem/`, `NoteSystem/`, `MessageSystem/`, `TooltipSystem/`, `FocusSystem/`, `RoleTargetSystem/`, `AudioSystem/` (FMOD wrapper), `FX/`, `ArrowSystem/`.
- `Extensions/` — Pure utility extensions (heavily unit-tested under `Tests/Editor/`).

### Documentation source of truth
`.ai-context/wiki/` contains the **Antigravity Wiki**: one markdown per feature under `features/`, plus `architecture.md`. Read the relevant feature wiki before non-trivial work on that system. Rules for maintaining it: `.ai-context/rules/WIKI_COMPILER.md`. Raw notes pending compilation live in `.ai-context/raw/`.

`AI_WIKI_GUIDE.md` at repo root is the entry pointer to this system.

### Async / animation conventions
- Async code uses **UniTask** (`UniTask`, `UniTaskVoid`), not `Task`.
- Tweens use **DOTween** with UniTask integration (`AwaitForComplete`).
- Long-running cancellable flows use `CancellableTaskHandler` (see `Board/CardComponents/`).

### Audio
**FMOD** only, never `AudioSource` for gameplay sounds. `AudioSystem/GameAudioManager` is the entry point.

### Networking transport
Production transport is **Facepunch (Steam)**. Local debug can use the simulated-player gateway instead of spinning up multiple Steam clients.

## Project-specific gotchas

- Repo is partially French — code identifiers are English, but commit messages, wiki, and some comments are French. Match the surrounding style.
- `Library/`, `Temp/`, `Logs/`, `TestResults/`, `obj/` are Unity-generated — never edit, never commit.
- The numerous `.csproj` files at root are auto-generated by Unity from `.asmdef` files. Edit the `.asmdef`, not the csproj.
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]` on test assemblies means tests only compile in Editor with test runner — expected.
