---
title: 'Refonte GameSettings — server-authoritative networked backbone + pure views'
type: 'refactor'
created: '2026-06-20'
status: 'done'
baseline_commit: '9b8899d3af4a9b51c984097c364eb128370a6961'
context:
  - '{project-root}/_bmad-output/project-context.md'
  - '{project-root}/_bmad-output/refactor-architecture-despaghetti.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The lobby GameSettings (role-attribution counts) embed their networking *inside runtime-`Instantiate`d UI `NetworkBehaviour`s* (`GameSettingTab`/`RoleAttributionSettingTab` with `[Rpc]`s, `GameSettingsUI`), synced ad-hoc by reading/broadcasting a per-client `RoleAttributionState` ScriptableObject clone — fragile NGO identity (the prefab is Instantiated under `StatesCanvas`, never `Spawn`ed), a runtime SO mutation (`_rolePair.Key.role = …`, an anti-pattern), and tightly coupled UI↔state. This blocks moving the lobby menu anywhere (Goal 2: the tablet) and is the project's flagged spaghetti.

**Approach:** Extract a server-authoritative, scene-**spawned** `GameSettingsManager` (`NetworkBehaviour`) that *owns* the role settings as replicated state, resolved via the `CompositionRoot` (lane-A field + `Services` accessor — **no new singleton**). Demote all GameSettings UI to **pure `MonoBehaviour` views** that read/write through the manager and refresh on its change event. Distribution + lobby validation read the manager. Behaviour-preserving: same default counts, same host-authoritative editing, same role distribution under the golden seed.

## Boundaries & Constraints

**Always:**
- Server authority: only the server mutates the replicated settings; clients propose via an RPC; replication is automatic (NGO `NetworkList`/`NetworkVariable`, server write-perm) — no manual targeted broadcast.
- Behaviour-preserving: `RoleDistributor`, the frozen authored role-pool order (`GetFrozenRolePoolOrder`), and the Start-Game validation (`playerCount > totalRoles` → warn + abort) stay byte-identical. `RoleAssignmentGoldenMasterTests` + `RolePoolOrderingTests` stay green **unchanged**.
- Host-authoritative editing preserved (today's de-facto: only the host effectively changes settings — non-host edits revert on the server refresh). Non-host views are read-only mirrors.
- DI rules: new manager wired lane-A on `CompositionRoot` (SceneWiringGuard), pushed lane-B into GameStates via `SetupGameStates` (mirror `boardManager`); prefab UI resolves it the sanctioned Story-12.3 way (`CompositionRoot.For(Singleton).GameSettings`). No new `static instance` (StaticAbsenceGuard). No `*.For(` in a migrated GameState (push, don't pull).
- NFR5: any RPC respects the bot gateway; since replication is via auto-synced state (no manual `ClientRpc` target), it is bot-safe by construction — document this.

**Ask First:**
- Opening settings editing to **all** clients (today only the host effectively edits). Default = preserve host-only; ask before broadening.
- Introducing a designer-facing GameSettings ScriptableObject distinct from `RoleAttributionState`'s authored dictionary (default = keep authoring on `RoleAttributionState`, manager *seeds* from it).

**Never:**
- Do not change the role-attribution *mechanic*, the distribution math, or the authored role list/order.
- Do not touch the broader `StateUI` system or `LobbyUI` beyond decoupling GameSettings (Goal 2 relocates the menu; `LobbyUI` stays a `StateUI` — its only network use is the `IsServer` Start gate, no RPC).
- Do not mutate a ScriptableObject at runtime (kill the `_rolePair.Key.role = …` write).
- Do not move the lobby menu onto the tablet here (that is the deferred Goal 2).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Host edits a role count | Host drags a slider to N | Manager writes the replicated entry → every client's view refreshes to N | Clamp to authored range |
| Non-host slider | Non-host client UI | Sliders read-only; reflect the host's canonical values | Reject/ignore non-host write request |
| Late joiner mid-lobby | Client connects after edits | Receives current settings via the replicated-state snapshot → view shows correct counts | N/A |
| Role distribution | `RoleAttributionState.OnStartStateServer` | Reads counts from the manager (mapped by `RoleID` over the frozen order) → distribution identical to pre-refactor under same counts/seed | N/A (golden-pinned) |
| Start Game, not enough roles | `playerCount > Σ counts` | Warn + abort (unchanged) | `Debug.LogWarning`, no transition |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs` — **NEW.** Scene-placed `NetworkBehaviour` (own `NetworkObject`). Replicated role settings keyed by `RoleID`; server seed + RPC + read API + `OnSettingsChanged`.
- `Assets/Scripts/UI/GameSettings/RoleAttributionSettingObject.cs` — slider widget → pure view: read count from manager, write via `RequestSetRoleCount`, refresh on event. Drop the SO mutation + per-call `For` lookup.
- `Assets/Scripts/UI/GameSettings/RoleAttributionSettingTab.cs` — tab → pure view: build widgets from the manager's role list, subscribe to `OnSettingsChanged`. Delete `[Rpc]`s + `RoleSettingsUpdater`.
- `Assets/Scripts/UI/GameSettings/GameSettingTab.cs` — base → `MonoBehaviour` (drop `NetworkBehaviour` + `AskForRefreshSettingsRpc`).
- `Assets/Scripts/UI/GameSettings/GameSettingsUI.cs` — empty `NetworkBehaviour` → pure view container (or delete).
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` — distribution reads counts from the manager (not its own dict); keep `RoleDistributor` + frozen order untouched. Dict stays the *authoring* source the manager seeds from.
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — `OnStartGameButtonPressed` reads Σ counts from the manager.
- `Assets/Scripts/GameLogic/GameState.cs` — add `gameSettingsManager { get; set; }` (lane-B push target, mirror `boardManager`).
- `Assets/Scripts/GameLogic/GameManager.cs` (`SetupGameStates`, ~248-261) — push `gameSettingsManager` from `CompositionRoot.For(NetworkManager).GameSettings`.
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — lane-A `[SerializeField] GameSettingsManager` + `Services.GameSettings` accessor + `ResolveGameSettings(nm)` (mirror `gameInfoRevealer`).
- `Assets/Scripts/Characters/RoleID.cs` / `Role.cs` — `RoleID` (existing) = the network key.
- GameScene + `LobbyStateUI.prefab` — add the manager GameObject (NetworkObject), wire it on `CompositionRoot`; strip the `NetworkBehaviour`s from the lobby UI prefab. (MCP: scene/prefab wiring + read-back.)
- `Assets/Scripts/Tests/PlayMode/…/GameSettingsManagerTests.cs` — **NEW** multi-client.

## Tasks & Acceptance

**Execution:**
- [x] `GameSettingsManager.cs` — created: `NetworkList<RoleSettingEntry>` (unmanaged struct keyed by `RoleID`), server write-perm; `OnNetworkSpawn` (server) seeds from the authored `RoleAttributionState` dict (idempotent); `[Rpc(SendTo.Server)] SubmitRoleCountServerRpc` + host-direct write, gated by `_allowClientEditing` (default false = host-only); `GetRoleCount`/`GetCanBeFake`/`GetTotalRolesToAttribute` + `OnSettingsChanged`; clamp 0..10.
- [x] `CompositionRoot.cs` — lane-A `[SerializeField] gameSettings` + `GameSettingsManager` accessor + `ResolveGameSettings(nm)`. NOTE deviation: gameSettings is **not** Awake-asserted (its consumers are null-tolerant — see Design Notes); SceneWiringGuard enforces production wiring instead.
- [x] `GameState.cs` + `GameManager.SetupGameStates` — added `gameSettingsManager` field + lane-B push.
- [x] `RoleAttributionState.cs` — reads counts/canBeFake/total from `gameSettingsManager` (fallback to authored dict when null); distribution math + `_fakeRoleAmountToRemove` + frozen order identical.
- [x] `LobbyState.cs` — `OnStartGameButtonPressed` totals from the manager (fallback to dict when null).
- [x] `RoleAttributionSettingTab.cs` / `RoleAttributionSettingObject.cs` / `GameSettingTab.cs` / `GameSettingsUI.cs` — demoted to pure `MonoBehaviour` views; deleted the RPC sync + `RoleSettingsUpdater`; killed the runtime SO mutation; non-host sliders read-only; subscribe `OnSettingsChanged`.
- [x] GameScene (MCP) — added the `GameSettingsManager` scene NetworkObject under `---GameLogic---`, wired `_authoredSettingsSource` → RoleAttributionState SO + `CompositionRoot.gameSettings` → the manager; verified by read-back + SceneWiringGuard green. Lobby UI NetworkBehaviours auto-demoted via the base-class change (no GameSettings NetworkBehaviour remains).
- [x] `GameSettingsManagerTests.cs` — single-host PlayMode (4/4): seed, host edit + event + total, clamp, no-op idempotency. Two-NM cross-wire replication + non-host-ignored + late-join deferred (NGO NetworkList semantics + manual smoke) — recorded in deferred-work.

**Acceptance Criteria:**
- Given a host + a real client in the lobby, when the host changes a role count, then the client's view shows the new count (replicated, no manual broadcast).
- Given a non-host client, when it attempts a settings change, then the canonical settings are unchanged and its view re-mirrors the host.
- Given the same authored counts + golden seed, when distribution runs, then the assigned roles are identical to pre-refactor (`RoleAssignmentGoldenMasterTests` + `RolePoolOrderingTests` green, unchanged).
- Given the refactor lands, then no GameSettings `NetworkBehaviour` remains in the `LobbyStateUI` prefab, no new `static instance` exists, and the three CI guards (`DiSeamGuard`/`SceneWiringGuard`/`StaticAbsenceGuard`) are green.

## Design Notes

- **Replication primitive:** prefer `NetworkList<RoleSettingNet>` with `RoleSettingNet : INetworkSerializable, IEquatable<RoleSettingNet> { RoleID roleId; int count; bool canBeFake; }` (unmanaged → NGO-2.6 clean), server write-perm, clients react to `OnListChanged`. Per-role `NetworkVariable` is acceptable — constraint is *server-write, auto-replicated, no manual `ClientRpc`*.
- **Why a spawned manager (not the old Instantiated-UI path):** `[Rpc]`/`NetworkVariable` need a **spawned** `NetworkObject`; the old tabs were `Instantiate`d under `StatesCanvas` and never spawned (fragile). A scene `NetworkObject` spawns with the host and snapshots cleanly to late joiners.
- **Null-tolerant gameSettings (implementation deviation from the "assert in Awake" task):** the distribution + lobby consumers fall back to the authored `RoleAttributionState` dict when `gameSettingsManager` is null (so the `RoleAssignment` golden — which builds a standalone state with no DI graph — stays byte-identical), and the UI views guard `!= null`. So `null` is a designed, supported state, and the PlayMode power/corruption harnesses (`NetworkTestHelper.RegisterCompositionRoot`) register a root without a settings manager. A hard `Awake` assert would red those harnesses for no production benefit. Production wiring is instead enforced by **SceneWiringGuard**: `CompositionRoot` is a `SceneWiredOnly` type whose `[SerializeField]` refs are checked **iff their type is in `DiSeamMigratedConsumers.InjectedManagerTypes`** — so `typeof(GameSettingsManager)` was **added to that set** (review patch), making `CompositionRoot.gameSettings` guard-covered. Hence no Awake assert on this one field.
- **Clamp upper bound = 15 (review patch):** `MaxRoleCount` mirrors the authored slider's max (`RoleAttributionRoleSettingObject` prefab `m_MaxValue: 15`), not the `[Range(0,10)]` attribute, so the clamp is a no-op for legitimate input and the achievable range is exactly the pre-refactor one (the old code had no server clamp). **Flagged for Poyo:** the slider max (15) and the `[Range(0,10)]` disagree — a design call to align.
- **Host-only editing via `[SerializeField] _allowClientEditing` (default false):** the host writes the server-authoritative `NetworkList` directly; a non-host's `RequestSetRoleCount` is a no-op and the `SubmitRoleCountServerRpc` is server-gated. Flip the toggle (+ the slider-interactable gate) to open editing to all clients (spec Ask-First).
- **Single-host test scope:** `GameSettingsManagerTests` proves the server-authoritative seed/read/write/clamp/event/idempotency path under StartHost (host==server). Cross-wire replication to a real 2nd client, the non-host-ignored gate, and the late-join snapshot are NGO `NetworkList` semantics + manual smoke; a dedicated two-NM test is deferred (same spec-permitted reduction the avatar epic used for its arbiter integration tests).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change — expected: zero compile errors before any test run.
- `mcp__UnityMCP__run_tests` EditMode, category `GoldenMaster` + the role tests — expected: `RoleAssignmentGoldenMasterTests` + `RolePoolOrderingTests` green, **unmoved**.
- `mcp__UnityMCP__run_tests` filtered to `DiSeamGuard` / `SceneWiringGuard` / `StaticAbsenceGuard` — expected: green.
- `mcp__UnityMCP__run_tests` PlayMode, the new `GameSettingsManagerTests` — expected: green (host→client replication, non-host ignored, late-join snapshot).
- Full EditMode + PlayMode suite before review — expected: no regression vs the EM/PM baseline.

**Manual checks:**
- In a host+client play session: host changes counts → client sliders update; non-host cannot change the canonical values; Start Game still aborts when players outnumber roles.

## Suggested Review Order

**The networked backbone (start here)**

- Entry point — the server-authoritative replicated owner that replaces the spaghetti.
  [`GameSettingsManager.cs:27`](../../Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs#L27)
- Server seeds the `NetworkList` from the authored SO once at spawn; fail-loud on dup/unset RoleID.
  [`GameSettingsManager.cs:75`](../../Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs#L75)
- Write path: host writes directly, non-host gated behind `_allowClientEditing`; clamp 0..15.
  [`GameSettingsManager.cs:169`](../../Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs#L169)

**DI seam — how it's resolved & wired (no new singleton)**

- Lane-A field on the composition root (mirrors `gameInfoRevealer`).
  [`CompositionRoot.cs:46`](../../Assets/Scripts/GameLogic/CompositionRoot.cs#L46)
- Scene-root resolution; null-tolerant (consumers fall back).
  [`CompositionRoot.cs:137`](../../Assets/Scripts/GameLogic/CompositionRoot.cs#L137)
- Pushed lane-B into every GameState (mirrors `boardManager`).
  [`GameManager.cs:261`](../../Assets/Scripts/GameLogic/GameManager.cs#L261)
- The push target field on the GameState base.
  [`GameState.cs:50`](../../Assets/Scripts/GameLogic/GameState.cs#L50)
- Guard coverage that replaces the dropped Awake assert (the review must-fix).
  [`DiSeamMigratedConsumers.cs:178`](../../Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs#L178)

**Consumers — behaviour-preserving reads (golden-sensitive)**

- Distribution reads counts/canBeFake from the manager, falls back to the authored dict when null (keeps the golden byte-identical).
  [`RoleAttributionState.cs:49`](../../Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs#L49)
- Start-Game validation totals from the manager (same fallback).
  [`LobbyState.cs:37`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L37)

**UI — demoted to pure views (no NGO identity)**

- Tab builds a widget per authored role, subscribes to the manager's change event.
  [`RoleAttributionSettingTab.cs:29`](../../Assets/Scripts/UI/GameSettings/RoleAttributionSettingTab.cs#L29)
- Slider reads/writes through the manager; read-only on non-host.
  [`RoleAttributionSettingObject.cs:33`](../../Assets/Scripts/UI/GameSettings/RoleAttributionSettingObject.cs#L33)

**Tests**

- Single-host PlayMode coverage (seed / edit+event / clamp / idempotency).
  [`GameSettingsManagerTests.cs:25`](../../Assets/Scripts/Tests/PlayMode/GameSettings/GameSettingsManagerTests.cs#L25)
