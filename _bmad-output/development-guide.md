# Development Guide

## Prerequisites

| Tool | Version |
|---|---|
| Unity Editor | **6000.2.6f2** (exact) |
| Render pipeline | URP 17.2.0 (installed via package) |
| FMOD Studio | Required to author audio; banks live under `Assets/FMODBanks/` |
| Steam client | Required for Facepunch transport runtime / Steam lobby debug |
| Git | LFS optional; large binaries handled by Unity directly |
| Python | 3.x — used by `_bmad/scripts/resolve_customization.py` |

## Setup

1. Clone the repository.
2. Open the project root in Unity Hub → it must auto-select `6000.2.6f2`.
3. Allow Unity to import / regenerate `Library/` (Unity-managed, not committed).
4. (Optional) Install **MCP for Unity** server — package is already declared in `Packages/manifest.json` (`com.coplaydev.unity-mcp`). Required for `mcp__UnityMCP__*` tools used by AI workflows.

> Do **not** edit the auto-generated `.csproj` files at the repo root. Add references via `.asmdef` files instead.

## Branches

- Main: `Dev` (PR target).
- Build CI runs on push (see `.github/workflows/Build.yml`).
- Commit / PR discord notifications via `DevPush.yml`, `DevPROpened.yml`, `DevMergeNotif.yml`.

## Build / run / test (preferred: Unity MCP)

| Action | Tool | Notes |
|---|---|---|
| Run all tests (EditMode + PlayMode) | `mcp__UnityMCP__run_tests` | Filter by category or assembly when relevant |
| Read compile errors after change | `mcp__UnityMCP__read_console` | Poll before assuming a change worked |
| Inspect / mutate scene | `mcp__UnityMCP__manage_scene` + `manage_gameobject` | |
| Create / delete script | `mcp__UnityMCP__manage_script` | For pure edits, prefer the `Edit` tool |
| Build player | `mcp__UnityMCP__manage_build` | CLI Unity builds are **not** the primary workflow |
| Enter Play Mode | `mcp__UnityMCP__manage_editor` | |

### Test workflow

1. After **any** code change → `mcp__UnityMCP__read_console` to catch compile errors before further work.
2. After completing a feature → `mcp__UnityMCP__run_tests` (filter as needed).
3. Add unit tests when: new powers, new roles, new network flows, non-trivial logic, or bug fixes with subtle root causes. Skip for pure Unity boilerplate.

### Test assemblies

| Assembly | Path | Purpose | Stack |
|---|---|---|---|
| `Tests.Editor` | `Assets/Scripts/Tests/Editor/` | Pure C# logic (extensions, validators, parsers) | NSubstitute |
| `Tests.PlayMode` | `Assets/Scripts/Tests/PlayMode/` | Networked flows, multi-client | `NetworkTestHelper` |

Both carry `defineConstraints: ["UNITY_INCLUDE_TESTS"]` → they only compile inside the Editor test runner. Expected behavior.

## CI status

- `Build.yml` — runs game-ci builds on push. Active.
- `unity-tests.yml` — exists but currently gated with `if: false` (disabled).
- `DevPush.yml`, `DevPROpened.yml`, `DevMergeNotif.yml` — discord notification workflows.

> Commit bodies are surfaced to Discord by `Build.yml`. See commit conventions below.

## Commit conventions (load-bearing — CLAUDE.md)

- **Never** add Claude / AI as commit author or co-author. No `Co-Authored-By: Claude ...` trailers.
- Commits in **English**, subject **and** body.
- **Always include a body** explaining *why* — even for `ci`/`chore`/`docs`.
- Subject = conventional commits. Body adds a `UX:` line when player experience is affected.
- Skip `UX:` for pure infra/CI/tooling, but still write a descriptive body.

Format:
```
feat(ui): animate role picker entrance/exit

Tween the role card in and out on a dedicated layer so the reveal is progressive.

UX: the player sees their role appear gradually — reduces confusion during role distribution.
```

These bodies appear in Discord build notifications via `Build.yml` — the UX line doubles as a real-time team briefing.

## Coding conventions

- **Async:** `UniTask` / `UniTaskVoid`. Never `System.Threading.Tasks.Task`.
- **Tweens:** DOTween + UniTask integration (`AwaitForComplete`).
- **Cancellable long flows:** `CancellableTaskHandler` (see `Board/CardComponents/`).
- **Audio:** FMOD only via `AudioSystem/GameAudioManager`. Never `AudioSource` for gameplay.
- **Networking:** wrap RPC targets with `GetSafeRpcTarget(clientId)`; use `IsLocalOrSimulated(clientId)` instead of `IsLocalClient`.
- **Server authority:** mutate state on server only; clients propose via `ServerRpc`.
- **Language:** code identifiers in English; commits and some comments in French — match surrounding style.

## Local debug tips

- Use the simulated-player gateway (`clientId >= 100`) instead of launching multiple Steam clients.
- `NetworkTransportDetector` chooses transport at runtime.
- `IsServerTest.cs` is a quick toggle helper for server-side branches during dev.
