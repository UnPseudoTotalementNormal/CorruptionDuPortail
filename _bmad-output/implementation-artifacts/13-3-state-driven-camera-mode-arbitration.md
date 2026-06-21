# Story 13.3: State-driven camera-mode arbitration (preserve the fixed-camera game loop)

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want camera mode to switch automatically with game state — **free-roam in the Lobby, the existing fixed board cameras during the loop, embodied during the Vote** — driven by a single arbiter,
so that the new avatar camera modes never leak into the phases that must keep the current presentation.

> **Epic 13 — Player Embodiment & Physical Presence.** Builds on **Story 13.1** (spawned/persisted `PlayerAvatar` + `AvatarManager`) and **Story 13.2** (owner-auth movement + the **minimal** Lobby first-person camera). **This story generalizes 13.2's hard-coded `is LobbyState` gate into a proper state → camera-mode arbiter.** It owns the **mapping table**, the **coexistence with `BoardCameraManager`**, and the **movement-input gating per state**. It does **NOT** build the embodied Vote seating / look-clamp (that is **Story 13.4** — 13.3 only routes `VoteState` to the `Embodied` mode slot and locks movement; the seat-snap + clamped look are handed off). No voice (13.5/13.6).

## Acceptance Criteria

1. A **state → camera-mode mapping** is established with three modes — **`FreeRoam`** (Lobby), **`Board`** (every in-loop fixed state), **`Embodied`** (`VoteState`) — keyed by **state TYPE** (`LobbyState → FreeRoam`, `VoteState → Embodied`, everything else **and `null` → `Board`**). Type-keying (not index) so a state **reorder** is a no-op and a type **rename** breaks compilation rather than silently mis-mapping.
2. The arbiter **integrates with `BoardCameraManager`'s existing source/activation model** rather than bypassing it: a **new `BoardCameraInputActiveSource.Avatar`** source is toggled — `SetActiveSource(Avatar, false)` in `FreeRoam`/`Embodied` (cuts board-camera arrow neighbour-nav), `SetActiveSource(Avatar, true)` in `Board`. Board cameras are **never** removed/disabled directly; `GameState.forceBoardCamera` + arrow nav keep working **exactly as today** in the untouched states (the existing `GameState` source is untouched — the two sources AND together via `ControllerBase.IsActive()`).
3. Avatar **movement input is enabled ONLY in `FreeRoam` (Lobby)** and disabled in every other state, via the existing `AvatarMovementController.SetMovementEnabled(bool)` hook (`AvatarMovementController.cs:107-108`). Re-enabled on returning to the Lobby. (There is **no return-to-lobby mid-match and no mid-loop join** today — documented as such; the arbiter handles it generically anyway.)
4. The arbiter is the **single state→mode authority**: `AvatarFollowCamera` **no longer self-subscribes** to `currentGameStateIndex` / checks `is LobbyState` (its 13.2 minimal gate is removed); it exposes an activation entry point the arbiter drives. Lobby first-person behaviour is **unchanged** (still activates in the Lobby, still hides the local model, still pose-copies the eye each `LateUpdate`).
5. **NFR2 — transition ordering intact.** The switch is wired purely as a **reaction** to `currentGameStateIndex.OnValueChanged` (subscribe in `Start`/prime with current value, unsubscribe in `OnDestroy` — subscription symmetry). It **never writes** the index and **does not alter** the `OnEnd → write currentGameStateIndex.Value → OnStart` ordering. The refactor's **Story 2.11a sequence golden stays green unchanged** (`Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/GameLoopTransitionOrderingTests.cs` + `Assets/Scripts/Tests/Editor/GameLoopMachineTests.cs`).
6. An **EditMode test pins the state → camera-mode mapping table** (a golden over the pure policy), and a **PlayMode test** asserts the arbiter, on `currentGameStateIndex` change, drives: follow-cam activation (on iff `FreeRoam`), the `BoardCameraManager` `Avatar` source, and movement enable/disable. A future state reorder/rename breaks a test instead of silently dropping a player into the wrong camera.
7. **NFR1 — untouched phases preserved.** Full **EditMode + PlayMode suite is green** and the **console is clean**; a **manual smoke pass** confirms Awakening / recaps / chaining / victory-checks / portal / ending look **identical** to before this epic (golden-blind → wants a Poyo playtest). Baseline at story start: **EM 215 / PM 158**.

## Tasks / Subtasks

- [x] **Task 1 — The camera-mode policy (the golden table, EditMode-testable)** (AC: #1, #6)
  - [x] New `CameraMode` enum `{ FreeRoam, Board, Embodied }` in the **`Game` asmdef** (namespace `Avatars`, next to the avatar code).
  - [x] New **pure** `AvatarCameraModePolicy` (static class or stateless POCO, `Game` asmdef — **NOT** Domain: it references `GameState`, an engine `ScriptableObject` type forbidden by Domain's `noEngineReferences` purity guard). Single method `ResolveMode(GameState _state) → CameraMode`: `LobbyState → FreeRoam`, `VoteState → Embodied`, `null` **and** every other type `→ Board`. No `NetworkManager`, no scene refs — pure type switch so EditMode tests instantiate states with `ScriptableObject.CreateInstance<T>()` and assert the mapping with zero network setup.
  - [x] Comment why **type-keyed** (reorder-proof; rename = compile break) not index-keyed, and that `Embodied`'s concrete camera/seat realization is **Story 13.4** (13.3 only routes the mode + locks movement).
- [x] **Task 2 — Add the `Avatar` board-camera input source** (AC: #2)
  - [x] Append **`Avatar = 3`** to the `BoardCameraInputActiveSource` enum (`Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs:132-137`). **Append only** — do not renumber `GameState=0`/`Cutscene=1`/`Pause=2` (other call-sites + the inspector depend on the values). This is the source the arbiter toggles via `BoardCameraManager.SetActiveSource(BoardCameraInputActiveSource.Avatar, bool)` — the same `MonoController`/`ControllerBase` AND-gate `forceBoardCamera` already uses (`BoardCameraManager.cs:80,84`).
- [x] **Task 3 — `AvatarCameraArbiter` (the single reaction)** (AC: #1, #2, #3, #5)
  - [x] New `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` (`MonoBehaviour`, namespace `Avatars`). `[SerializeField] private GameManager gameManager;` + `private IGameStateQuery Query => gameManager;` (**lane A**, mirrors `BoardCameraManager.cs:21-25` and `AvatarFollowCamera`). `[SerializeField] private AvatarFollowCamera _followCamera;`. `Awake` asserts both are wired (like `AvatarFollowCamera.Awake`).
  - [x] **Reaction wiring (NFR2):** subscribe `Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged` in `Start`, **prime** with `OnGameStateChanged(Value, Value)` (exactly `BoardCameraManager.cs:65-67` / `AvatarFollowCamera.cs:56-61`), unsubscribe in `OnDestroy` (subscription symmetry, archi §5b). **Never write the index** — it is a pure reaction.
  - [x] On change: `CameraMode _mode = AvatarCameraModePolicy.ResolveMode(Query.GetGameState(_newValue));` then apply:
    - `_followCamera.SetActive(_mode == CameraMode.FreeRoam);`
    - `BoardCameraManager.instance?.SetActiveSource(BoardCameraInputActiveSource.Avatar, _mode == CameraMode.Board);` (`.instance` is a **recorded census survivor / opt-out** — sanctioned here, NOT a locator to remove; null-tolerant for headless/early-boot).
    - Local owned avatar's `AvatarMovementController.SetMovementEnabled(_mode == CameraMode.FreeRoam);`.
  - [x] **Local-avatar binding (late spawn):** the local avatar may spawn a frame or two **after** the Lobby state activates — mirror `AvatarFollowCamera.TryBindLocalAvatar` (`AvatarFollowCamera.cs:138-158`): resolve via `AvatarManager.For(NetworkManager.Singleton).GetAvatars()`, take the one where `IsOwner`, cache its `AvatarMovementController`. Cache the **last resolved mode** and **re-apply** movement-enable when the controller binds, so an avatar that appears after the Lobby gate still starts walkable. (`AvatarManager.For` + `NetworkManager.Singleton` are the avatar layer's own resolution — same as 13.2; this is why the avatar types are **NOT** in `DiSeamMigratedConsumers`, see Project Context Rules.)
- [x] **Task 4 — Refactor `AvatarFollowCamera` to be arbiter-driven** (AC: #4)
  - [x] **Remove** `AvatarFollowCamera`'s own state coupling: delete the `Start` subscription + `OnGameStateChanged` + `is LobbyState` branch (`AvatarFollowCamera.cs:56-83`) and the now-unused `_subscribed`/`OnDestroy` unsubscribe. The `gameManager` `[SerializeField]` + `Query` are no longer read by the follow camera → **remove them** (the arbiter owns all state reads now); update the `Awake` assert accordingly.
  - [x] Make the existing private `Activate()`/`Deactivate()` reachable by the arbiter — expose `public void SetActive(bool _active) { if (_active) Activate(); else Deactivate(); }` (keep `Activate`/`Deactivate`/`LateUpdate` pose-copy/`TryBindLocalAvatar`/`Hide`/`ShowBoundModel` **unchanged** — only the *who-decides-active* moves out).
  - [x] ⚠️ **Serialized-field rewiring rule** ([[reference_unity_mcp_serialized_field_wiring]] / [[feedback_serialized_field_rewiring]]): removing the `gameManager` field changes the component's serialized layout — after the script change, **re-verify** `AvatarFollowCamera`'s remaining `_camera` ref is still wired in `GameScene` (read back non-null) and that no console error fires on the dropped field.
- [x] **Task 5 — Scene wiring (Unity MCP)** (AC: #2, #3, #4)
  - [x] Add the `AvatarCameraArbiter` component (sibling on the **same `GameScene` GameObject as `AvatarFollowCamera`**, or a dedicated camera-rig object). Wire `gameManager` (the scene `GameManager`, like `BoardCameraManager.gameManager`) + `_followCamera` (the `AvatarFollowCamera` instance). **Read back to verify non-null**; null-guard. Watch the array-element `set_property` gotcha and create-time `component_properties` unreliability ([[reference_unity_mcp_serialized_field_wiring]]).
  - [x] Confirm `AvatarFollowCamera`'s `_camera` survived the Task-4 field removal (read-back). No other scene change expected (movement controller is resolved at runtime from the spawned avatar).
- [x] **Task 6 — Tests** (AC: #1, #5, #6)
  - [x] **EditMode** `Assets/Scripts/Tests/Editor/AvatarCameraModePolicyTests.cs` — the **golden table**: `ScriptableObject.CreateInstance<LobbyState>()` → `FreeRoam`; `<VoteState>()` → `Embodied`; a representative loop state (e.g. `<AwakeningState>()`, `<ChainingState>()`, `<VoteRecapState>()`) → `Board`; `null` → `Board`. One assert per row so a mis-map names the offending state. (`DestroyImmediate` the SOs in teardown.)
  - [x] **PlayMode** `Assets/Scripts/Tests/PlayMode/Avatars/AvatarCameraArbiterTests.cs` — assert the arbiter reacts to `currentGameStateIndex` changes: stepping the index across a `FreeRoam`/`Board`/`Embodied` state toggles `AvatarFollowCamera` active state + the `BoardCameraManager` `Avatar` source (read `GetActiveSource("Avatar")`) + (where the avatar substrate is wired) the movement-enabled flag. Reuse the established substrate/fixture; **poll with `WaitUntilOrTimeout`, never `WaitForSeconds`**. If wiring a full owned avatar into this test is disproportionate, assert the camera + board-source effects here and cover movement-enable via the policy + a direct `SetMovementEnabled` round-trip — **document the choice** (do not skip the arbiter reaction assertion).
  - [x] **Re-run the 2.11a golden unchanged** (`GameLoopTransitionOrderingTests` + `GameLoopMachineTests`) — must stay green with zero edits (NFR2 proof). Do not modify them.
- [x] **Task 7 — Verify** (AC: #5, #6, #7)
  - [x] `read_console` after **every** change (compile clean, 0 errors) before assuming anything works.
  - [x] `run_tests` — **EditMode** (the 3 DI/census guards — `DiSeamNoLocatorGuard` / `SceneWiringGuard` / `StaticSingletonCensusGuard` — stay green; the new arbiter must **not** trip them, see Project Context Rules) **and** **PlayMode** (new policy/arbiter tests + 13.1/13.2 avatar tests + the 2.11a golden + existing suite). Baseline: **EM 215 / PM 158** → expect +EditMode (policy) +PlayMode (arbiter).
  - [x] **GameScene boot smoke:** enter Play, console clean, no NRE; the Lobby still shows first-person and walking still works (13.2 unregressed).
  - [x] Record the **NFR1 manual smoke** as golden-blind → flag for a Poyo playtest (lobby→host through Awakening/Vote/recaps).

### Review Findings (gds-code-review, 2026-06-15)

Adversarial 3-layer review (Blind Hunter / Edge Case Hunter / Acceptance Auditor). **Auditor verdict: PASS — 7/7 ACs satisfied, tasks done as written, 2.11a golden untouched.** 1 patch applied, 1 deferred, 6 dismissed.

- [x] [Review][Patch] `OnDestroy` restores the board-camera `Avatar` source to `true` [Assets/Scripts/Avatars/AvatarCameraArbiter.cs:66] — the arbiter is the sole owner of the `Avatar` AND-source; if torn down while a non-Board mode left it `false`, a surviving `BoardCameraManager.instance` (sanctioned static survivor) would keep arrow neighbour-nav cut forever. Added idempotent, null-tolerant restore (cleanup symmetry, archi §5b). Not reachable in the current single-scene/no-return-to-lobby flow, but a correct latent-bug fix. **Applied.**
- [x] [Review][Defer] Arbiter's movement path (`TryBindLocalMovement` → `SetMovementEnabled(FreeRoam)`) has no integration test [Assets/Scripts/Tests/PlayMode/Avatars/AvatarCameraArbiterTests.cs] — deferred: documented scope decision (AC6 permits covering movement via the policy FreeRoam row + a direct `SetMovementEnabled` round-trip when full owned-avatar wiring is disproportionate). Recorded in deferred-work.md for a future test-hardening pass.

Dismissed as noise (rationale): board-source null-`instance` at prime (Awake precedes all Start → `instance` set; `?.` is intentional headless tolerance); per-frame `Update` rebind (real clients always own an avatar with the controller; `foreach List<T>` is alloc-free); rebind-after-despawn (avatars persist the whole match; the `Update` null-check rebinds anyway); `OnDestroy` NRE on null index (NetworkVariable is field-initialized, never null — mirrors `BoardCameraManager.OnDestroy`); `GetGameState` bounds (identical to existing `BoardCameraManager` exposure, misconfig-only); test stub lifecycle asymmetry (base bodies are CharacterManager-free and safe).

## Dev Notes

### Source-tree placement

- New code → existing **`Assets/Scripts/Avatars/`** (`Game` asmdef): `CameraMode.cs`, `AvatarCameraModePolicy.cs`, `AvatarCameraArbiter.cs`, next to `AvatarFollowCamera.cs` / `AvatarMovementController.cs`.
- Enum edit → `Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs` (append `Avatar = 3`).
- Tests: EditMode → `Assets/Scripts/Tests/Editor/`; PlayMode → `Assets/Scripts/Tests/PlayMode/Avatars/`.
- **Prefer `create_script`** for the 3 new `.cs` (Unity silent-compile-exclusion risk on raw file writes — [[reference_unity_silent_compile_exclusion]]).

### What 13.2 already built (read first — this story refactors part of it)

- **`AvatarFollowCamera.cs`** (`Assets/Scripts/Avatars/`) — FIRST-PERSON Lobby camera. Today it **self-subscribes** to `currentGameStateIndex.OnValueChanged` and activates iff `Query.GetGameState(idx) is LobbyState` (`:56-83`). **This story removes that self-gate** and lets the arbiter drive `SetActive`. The pose-copy (`LateUpdate :106-136`), local-avatar bind (`:138-158`), and model hide/show (`:160-188`) **stay as-is**.
- **`AvatarMovementController.cs`** — owner-gated `CharacterController` movement. Already exposes **`public void SetMovementEnabled(bool)`** (`:107-108`) and an internal `_movementEnabled` flag read in `Update` (`:112`) **explicitly as the 13.3 hook** (`:56-58`). This story just *calls* it from the arbiter. Don't re-architect the controller.
- **`AvatarManager.cs`** — `For(nm)`-only registry (no `static instance`); `GetAvatars()` returns the replicated list; the local avatar is the one with `IsOwner` (the bind pattern 13.2 already uses).
- **`PlayerAvatar.cs`** — thin `NetworkBehaviour`; exposes `EyePivot`. Movement controller + follow cam attach to / read from it.
- 13.2 tests: `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` (dual-NM UTP-loopback substrate). The arbiter PlayMode test can reuse this style.

### Existing systems to cooperate with (do NOT fight them)

- **`BoardCameraManager`** (`Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs`) — `MonoController` with a `static instance` (a **recorded census survivor / opt-out** — sanctioned, do **not** add a new locator or try to de-singletonise it). Reacts to `currentGameStateIndex.OnValueChanged` (`:65-86`); pins cameras via `GameState.forceBoardCamera` + `SetActiveSource(GameState, false)` (`:77-85`). The arbiter adds the **`Avatar`** source alongside `GameState` — they **AND** together (`ControllerBase.IsActive()` returns true only if every source is true, `Controller.cs:43-53`), and `TrySwitchCameraToNeighbour` early-returns when `!IsActive()` (`:88-93`). So `SetActiveSource(Avatar, false)` cleanly cuts arrow nav in free-roam/embodied **without** touching the `GameState` source or the board cameras themselves. Order of the two subscribers' invocation is irrelevant (the AND is order-independent — NFR2-safe).
- **`ControllerBase` / `MonoController`** (`Assets/Scripts/Inputs/Controller.cs`) — `SetActiveSource(string, bool)` keys off `enum.ToString()` (`BoardCameraManager.cs:126-129`); `GetActiveSource("Avatar")` is how the test reads it back.
- **`GameState`** (`Assets/Scripts/GameLogic/GameState.cs`) — `ScriptableObject`; `forceBoardCamera` is a **data field set on the SO assets in the Inspector** (no code assigns it — which board cam each state pins is content, untouched here). States are identified by **C# type** (`is LobbyState` / `is VoteState`), the basis of the type-keyed policy.
- **`IGameStateQuery`** (`Assets/Scripts/GameLogic/IGameStateQuery.cs`) — the narrow read slice (`GetGameState(int)`, `currentGameStateIndex`). Depend on it via the concrete `gameManager` field narrowed by a `Query` property (Unity can't serialize an interface) — identical to `BoardCameraManager`/`AvatarFollowCamera` (lane A).

### Load-bearing constraints (NFR1 / NFR2 — the spine of this story)

- **NFR2 — never disturb the loop ordering.** The arbiter is a **reaction** to `currentGameStateIndex.OnValueChanged`, never a driver. It must not call any `IGameLoop` mutator, never write the index, never reorder states. Proof = the **2.11a golden** (`GameLoopTransitionOrderingTests` / `GameLoopMachineTests`) stays green with **zero edits**. If those tests need editing to pass, the implementation is wrong.
- **NFR1 — untouched phases byte-identical.** Every non-Lobby, non-Vote state maps to `Board` → the `Avatar` source goes `true` → `BoardCameraManager` behaves exactly as today (arrow nav + `forceBoardCamera` intact). The follow cam stands down (`SetActive(false)` → priority `-100`, board cams win). Movement input is off. No observable change in Awakening/recaps/chaining/checks/portal/ending. This is **golden-blind** (no automated visual oracle) → a Poyo playtest is the acceptance gate.

### Scope boundaries (what is NOT this story)

- **Embodied Vote seating + clamped look (yaw ±75° / pitch ±40°, DO3/DO4) = Story 13.4.** 13.3 only routes `VoteState → Embodied` in the mapping and **locks movement**; it does **not** snap seats, place an embodied camera, or add look clamps. In 13.3, the `Embodied` mode falls back to the **board-camera presentation** for `VoteState` (its current `forceBoardCamera`, unchanged) with movement locked + arrow nav cut — the mode entry exists purely so 13.4 fills the camera/seat behaviour **without touching the arbiter**. Document this fall-back explicitly in the arbiter.
- **Movement feel / camera feel / eye offset** = Poyo-owned placeholder `[SerializeField]`s already on the 13.2 components — not re-tuned here.
- **Room wall colliders** (no NavMesh) = Poyo-authored content; orthogonal to this story.
- **Voice / proximity audio** = 13.5/13.6 (HELD on Steam).

### Design-open / Poyo-owned

- **`Embodied`-during-13.3 camera choice:** the only judgment call is "what does the Vote look like between 13.3 and 13.4?" — answer above: **unchanged board camera + movement locked** (safest, NFR1-consistent for the in-between state). If Poyo wants the Vote to *already* go first-person before 13.4 lands, that's a one-line `_mode == Embodied` branch on the follow cam — flagged, not assumed.
- **⚠️ Camera-mode transitions are golden-blind** → wants a Poyo playtest (lobby → host → step through Awakening/Vote). Automated tests cover the mapping + the arbiter reaction, not the visual feel.

### Previous-story intelligence (13.1 / 13.2 — both at `review`, uncommitted)

- 13.1 + 13.2 are **uncommitted**, awaiting Poyo's accept + playtest. 13.3 builds on their (working, tested) code. If Poyo's review reshapes the avatar/camera, re-baseline.
- Patterns to keep: born-clean avatar types (`For(nm)` + `NetworkManager.Singleton` resolution, **no `static instance`**, **not** registered in `DiSeamMigratedConsumers`); lane-A `[SerializeField] GameManager` narrowed by a `Query` property; subscription symmetry (subscribe in `Start`/prime, unsubscribe in `OnDestroy`); `Awake` asserts on wired refs.
- MCP gotchas hit before ([[reference_unity_mcp_serialized_field_wiring]]): create-time `component_properties` unreliable → `modify_contents`/`set_property` after + read back; `List<T>` object-ref arrays via `set_property` go null → set per-element `_field.Array.data[i]`. A `manage_prefabs`/`manage_gameobject` call is atomic — one bad property field rejects the whole call (13.2 hit this with `Vector3` serialized names; use C# property names).

### Project Structure Notes

- Aligns with the feature-first `Avatars/` folder, `Game` asmdef, `*Manager`/`*Controller`/`*Arbiter` naming, `[SerializeField] private`, `MonoBehaviour` for scene-placed non-networked logic (the arbiter is presentation-only — it reads replicated state and toggles local cameras/input; it is **not** a `NetworkBehaviour`).
- **Pure-policy split:** `AvatarCameraModePolicy` is the testable POCO seam (the per-system POCO + EditMode-test pattern from Epic 11). It stays in `Game` (not Domain) because it touches `GameState` — recorded, same reason `IGameStateQuery` lives in `Game` not Domain.
- **No variance** from the 13.2 networking deviation: this story adds **no** networking, no RPC, no `NetworkVariable`. Owner-auth movement (13.2) is untouched.

### Project Context Rules (from `_bmad-output/project-context.md`)

- **Server authority strict; clients propose via `ServerRpc`.** This story mutates **no** game state — it only reads the replicated `currentGameStateIndex` and toggles **local** cameras/input. No authority concern.
- **Networked init in `OnNetworkSpawn`, teardown in `OnNetworkDespawn`.** N/A for the arbiter (plain `MonoBehaviour`, `Start`/`OnDestroy` like `BoardCameraManager`). The avatar components it drives already follow the rule.
- **Input System only** (never legacy `Input.GetKey`); already satisfied by 13.2 — the arbiter only flips `SetMovementEnabled`, it reads no input itself.
- **`GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100`** — no RPCs added; bots have no avatar (DO2) so the local-avatar bind naturally skips them. NFR4 untouched.
- **DI seam:** the arbiter reads `gameManager` via `[SerializeField]` narrowed to `IGameStateQuery` (lane A) and uses `BoardCameraManager.instance` + `AvatarManager.For(nm)` + `NetworkManager.Singleton`. **Do NOT register `AvatarCameraArbiter` (or the other avatar types) into `DiSeamMigratedConsumers`** — they legitimately use `.For(` / `.instance` (recorded survivors), which the `DiSeamNoLocatorGuard` would flag *if* they were in the curated migrated set. They are intentionally outside it (13.2 precedent: `AvatarFollowCamera` uses `AvatarManager.For` and is not registered). The new `MonoBehaviour` has **no `static`** field → `StaticSingletonCensusGuard` needs no whitelist entry. Verify all 3 guards stay green.
- **Async = `UniTask`** (`.Forget()` chains `GetCancellationTokenOnDestroy()`); **Audio = FMOD** — neither is exercised here (no async, no audio until voice).
- **`[SerializeField] private`; rename only with `[FormerlySerializedAs]`.** Wire every scene instance via MCP, **read back to verify non-null**, null-guard. Removing `AvatarFollowCamera.gameManager` (Task 4) must be followed by a scene re-verify.
- **Unity MCP workflow:** `read_console` after every change; `run_tests` filtered before declaring done; `manage_scene`/`manage_gameobject` for wiring; `create_script` for new `.cs`.

### References

- [Source: _bmad-output/planning-artifacts/epics-player-embodiment.md#Story 13.3] — story + ACs; FR5, NFR1, NFR2.
- [Source: _bmad-output/implementation-artifacts/13-2-movement-controller-and-lobby-free-roam-follow-camera.md] — the 13.2 minimal Lobby gate this story generalizes; the `SetMovementEnabled` hook; first-person camera shape.
- [Source: Assets/Scripts/Avatars/AvatarFollowCamera.cs#56-83,85-104,138-158] — the self-subscription/`is LobbyState` gate to remove + the `Activate`/`Deactivate`/bind logic to keep + expose via `SetActive`.
- [Source: Assets/Scripts/Avatars/AvatarMovementController.cs#56-58,107-108,110-115] — the `_movementEnabled` flag + `SetMovementEnabled` hook the arbiter calls.
- [Source: Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs#65-86,126-137] — state reaction, `SetActiveSource`, `BoardCameraInputActiveSource` enum to extend with `Avatar`.
- [Source: Assets/Scripts/Inputs/Controller.cs#22-53,68-71] — `ControllerBase.SetActiveSource`/`GetActiveSource`/`IsActive` AND-gate semantics.
- [Source: Assets/Scripts/GameLogic/IGameStateQuery.cs] — the narrow read slice (`GetGameState`, `currentGameStateIndex`).
- [Source: Assets/Scripts/GameLogic/GameState.cs#19,59,123-126] — `gameManager`, `forceBoardCamera` (SO-data), state identity by type.
- [Source: Assets/Scripts/GameLogic/GameStates/LobbyState.cs#13, VoteState.cs#23] — the two mapped state types.
- [Source: Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/GameLoopTransitionOrderingTests.cs] + [Assets/Scripts/Tests/Editor/GameLoopMachineTests.cs] — the 2.11a sequence golden that must stay green unchanged (NFR2).
- [Source: Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs] — multi-client substrate to reuse for the arbiter PlayMode test.
- [Source: _bmad-output/project-context.md] — server authority, NGO lifecycle, Input System, DI seam / guards, MCP rules.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- PlayMode arbiter test first run FAILED with `NullReferenceException: routine is null`. Root cause: `GameManager.Update` (GameManager.cs:224-234) drives the CURRENT state's `StateUpdateServer/Client` every frame; the `VoteStubState` did not override `StateUpdate*`, so `VoteState.StateUpdateServer` ticked its (uninitialized → 0) vote timer and called `Loop.NextGameState()`, whose `VoteState.OnEndStateServer` ran `StopCoroutine(null)`. Fixed by overriding `StateUpdateServer/Client` to no-ops on the Lobby/Vote stub states so the index stays under the test's sole control.

### Completion Notes List

- **AC#1 (mapping)** — `CameraMode { FreeRoam, Board, Embodied }` enum + pure `AvatarCameraModePolicy.ResolveMode(GameState)` switch on state TYPE (`LobbyState → FreeRoam`, `VoteState → Embodied`, `null` + everything else → `Board`). Type-keyed so reorder is a no-op / rename breaks compile. In `Game` asmdef (touches `GameState`, forbidden in Domain).
- **AC#2 (board-camera coexistence)** — appended `BoardCameraInputActiveSource.Avatar = 3` (append-only, 0/1/2 untouched). Arbiter toggles it via `SetActiveSource(Avatar, mode == Board)`; ANDs with the untouched `GameState` source through `ControllerBase.IsActive()`, so FreeRoam/Embodied cut arrow neighbour-nav without removing/disabling any board camera or touching `forceBoardCamera`.
- **AC#3 (movement gating)** — arbiter calls `AvatarMovementController.SetMovementEnabled(mode == FreeRoam)` on the late-bound local owned avatar (`AvatarManager.For(NetworkManager.Singleton)` + `IsOwner`, re-applied in `Update` until the controller binds — handles late avatar spawn).
- **AC#4 (single authority)** — `AvatarFollowCamera` lost its `currentGameStateIndex` subscription + `is LobbyState` gate + `gameManager`/`Query`/`_subscribed` + `Start`/`OnDestroy`/`OnGameStateChanged`. Exposes `public void SetActive(bool)` (drives existing `Activate`/`Deactivate`) + `public bool IsActive`. Pose-copy / bind / hide-show unchanged. Field removal verified in scene: `_camera` still wired (read-back non-null), `gameManager` gone, no console error.
- **AC#5 (NFR2)** — arbiter is a pure reaction: subscribe + prime in `Start`, unsubscribe in `OnDestroy`, never writes the index. 2.11a golden (`GameLoopTransitionOrderingTests` PM + `GameLoopMachineTests` EM) stayed green with ZERO edits.
- **AC#6 (tests)** — EditMode `AvatarCameraModePolicyTests` (6 golden rows, one assert each). PlayMode `AvatarCameraArbiterTests`: `Arbiter_DrivesCameraAndBoardSource_AcrossModes` steps the index across Board(prime)/FreeRoam/Embodied/Board and asserts follow-cam active + `Avatar` source via `WaitUntilOrTimeout`; movement effect covered by `MovementController_SetMovementEnabled_RoundTrips` + the policy FreeRoam row (full owned-avatar wiring into the reaction test judged disproportionate — documented in the test).
- **AC#7 (NFR1)** — full suite green; console clean; GameScene boot smoke clean (Lobby still primes to FreeRoam, no NRE). The 3 DI/census guards (DiSeamNoLocator / SceneWiring / StaticSingletonCensus) stay green — the new `MonoBehaviour` arbiter has no `static`, uses sanctioned `AvatarManager.For` / `BoardCameraManager.instance`, and is intentionally NOT in `DiSeamMigratedConsumers`. **EM 215 → 221 (+6), PM 158 → 160 (+2).**
- **Scope/golden-blind** — Embodied is a routing slot only in 13.3 (Vote keeps its unchanged board-camera fall-back + locked movement; seated camera/seat/clamp = 13.4). Camera-mode visual transitions are golden-blind → wants a Poyo playtest (lobby → host → step through Awakening / Vote).

### File List

- `Assets/Scripts/Avatars/CameraMode.cs` (new)
- `Assets/Scripts/Avatars/AvatarCameraModePolicy.cs` (new)
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` (new)
- `Assets/Scripts/Avatars/AvatarFollowCamera.cs` (modified — removed state coupling + `gameManager`; added `SetActive`/`IsActive`)
- `Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs` (modified — appended `BoardCameraInputActiveSource.Avatar = 3`)
- `Assets/Scripts/Tests/Editor/AvatarCameraModePolicyTests.cs` (new — EditMode golden table)
- `Assets/Scripts/Tests/PlayMode/Avatars/AvatarCameraArbiterTests.cs` (new — PlayMode arbiter reaction + movement round-trip)
- `Assets/Scenes/GameScene.unity` (modified — `AvatarCameraArbiter` added as sibling on the `AvatarFollowCamera` GameObject, `gameManager` + `_followCamera` wired)

### Change Log

| Date | Change |
|---|---|
| 2026-06-13 | Story 13.3 drafted via gds-create-story — state→camera-mode arbiter (generalizes 13.2's Lobby gate), `CameraMode` policy golden table, `BoardCameraInputActiveSource.Avatar` source, movement gating, NFR1/NFR2 preservation. Status → ready-for-dev. |
| 2026-06-15 | Implemented via gds-dev-story — `CameraMode` enum + pure `AvatarCameraModePolicy` (type-keyed golden), `BoardCameraInputActiveSource.Avatar = 3` (append-only), `AvatarCameraArbiter` (single reaction: follow-cam + board Avatar source + movement gating; late-binds local avatar), `AvatarFollowCamera` refactored arbiter-driven (lost self-subscription + `gameManager`, gained `SetActive`/`IsActive`). Scene wired + read-back verified. EM 221/221, PM 160/160; 2.11a golden + 3 guards green; boot smoke clean. Status → review. |
