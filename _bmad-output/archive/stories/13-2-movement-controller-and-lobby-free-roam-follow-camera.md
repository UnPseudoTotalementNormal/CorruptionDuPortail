# Story 13.2: Movement controller + Lobby free-roam follow camera

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want the local player to **walk their avatar freely around the Lobby room** with a **third-person follow camera**,
so that the Lobby becomes a navigable social space instead of a fixed view.

> **Epic 13 — Player Embodiment & Physical Presence.** Builds directly on **Story 13.1** (the spawned/persisted `PlayerAvatar` + `AvatarManager` + seat/spawn registry). This story adds **owner-authoritative movement** + a **Lobby follow camera** + the **input-driven tablet open** + **arrow-key reconciliation**. **No state-driven camera arbitration** (that is 13.3) and **no embodied Vote seating** (13.4) — keep this story to *Lobby free-roam + the movement/camera/input plumbing*.

## Acceptance Criteria

1. An **owner-only** movement controller reads a **new Input System action map** (move + look) and drives the **local** avatar; **non-owners receive interpolated `NetworkTransform`** updates. The avatar's `NetworkTransform` authority is flipped from **server (13.1)** to **owner** (NFR3) — position is cosmetic only, **no game state** is mutated.
2. Movement is bounded by the **room's physical wall colliders** (authored by Poyo — **no NavMesh**); the avatar cannot leave the room. (The controller respects whatever wall colliders exist in `GameScene`; if Poyo's walls are not yet authored, the controller is collider-ready and bounding "just works" once they exist — flagged below.)
3. `NetworkTransform` send rate is tuned so movement does **not** fire a per-frame RPC (NFR6) and position is **never** a custom per-frame `NetworkVariable<Vector3>`.
4. A **first-person Lobby camera** is active for the owning client **during the Lobby only** (⚠️ **Poyo's design call 2026-06-13 — overrides the epic's original "third-person follow camera"**; FR4/AC#4 updated), introduced as a new mode that **coexists** with `BoardCameraManager` (the board cameras are **not** removed or bypassed). Outside the Lobby the existing board cameras present exactly as today.
5. The action map is authored so a **mobile touch joystick + touch look** can be added later **without rework** (NFR5), though only **desktop bindings ship now**.
6. The **smartphone/tablet UI stays reachable while walking** (DO1): an **input-driven open/close** of `SmartphoneController` is added that works in free-roam **without** the board-camera trigger (`openOnCamera`), while the existing **camera-coupled open path is unchanged** in the untouched states; the **text-chat app** is reachable from it. The lobby **Start button** and **role-attribution settings** are **out of scope** (Poyo's responsibility).
7. The **arrow-key input conflict is reconciled** for free-roam: arrows are currently claimed by `BoardCameraManager` (camera neighbour nav, `BoardCameraManager.cs:60-63`) **and** `SmartphoneController` (app swipe, `SmartphoneController.cs:85-88`). Movement/look use a **separate binding** (WASD + mouse) so they do **not** collide with the arrow consumers, and the untouched states keep their current arrow behaviour. (Disabling board-camera arrow-nav *in Lobby* is **Story 13.3**'s arbiter job — 13.2 must not regress the untouched states.)
8. The full **EditMode + PlayMode suite is green** and the **console is clean** after the change; the **13.1 avatar spawn test still passes** with the owner-authority `NetworkTransform`.

## Tasks / Subtasks

- [x] **Task 1 — Flip the avatar `NetworkTransform` to owner authority** (AC: #1, #3, #8)
  - [x] On `Assets/Prefabs/Avatars/PlayerAvatar.prefab`, set the `NetworkTransform` `AuthorityMode` to **Owner** (13.1 left it `Server` = `0`; owner = `1`). Tune the send/interpolation so it is **event/threshold driven, not per-frame** (`PositionThreshold`/`RotAngleThreshold` already non-zero — confirm they suppress idle traffic).
  - [x] Avatars are server-spawned (13.1) and **not owned by the connecting client by default** — `InstantiateAndSpawn` gives ownership to the server. For owner-authoritative movement the avatar must be **owned by its `clientId`**: in `AvatarManager.SpawnAvatar` spawn with ownership to the target client (`InstantiateAndSpawn(prefab, ownerClientId: clientId, ...)` or `ChangeOwnership`/`SpawnWithOwnership`). ⚠️ Re-verify the 13.1 multi-client test still passes (ownership change must not break the replicated list / `ownerClientId` NV).
  - [x] Confirm the bot-skip (clientId >= 100) path is unaffected (no avatar = no ownership).
- [x] **Task 2 — Input: add Move + Look (+ tablet toggle) to the action map** (AC: #1, #5, #6, #7)
  - [x] Add to the **`Player`** map in `Assets/InputSystem_Actions.inputactions`: a **`Move`** action (`Vector2`, WASD 2D-composite, Keyboard&Mouse group) and a **`Look`** action (`Vector2`, `<Mouse>/delta`). Author them so a **Touch** joystick/look binding can be appended later (NFR5) — leave the control schemes intact. Add a **`ToggleTablet`** button (a free key, e.g. `<Keyboard>/tab` — **Poyo-tunable**).
  - [x] Extend `InputID` (`Assets/Scripts/Inputs/InputID.cs`) with `Move`, `Look`, `ToggleTablet` entries, and wire them through `InputManager` (`Assets/Scripts/Inputs/InputManager.cs`): the arrows use `InputActions` event objects; **continuous Move/Look are best read by value** each frame by the owner controller (poll the generated `InputActions` asset directly, or extend `InputManager.GetInputActions<Vector2>` — currently returns null, `InputManager.cs:195-203`). Pick **one** approach and document it. `ToggleTablet` is event-based like the arrows.
  - [x] Wire the new actions' `PlayerInput` callbacks the same way the arrows are wired (`InputManager.OnArrowUpPressed`-style, `InputManager.cs:208-234`) — find the scene `PlayerInput` component that dispatches to those methods and add the new ones.
- [x] **Task 3 — Owner-authoritative movement controller** (AC: #1, #2, #3)
  - [x] New `Assets/Scripts/Avatars/AvatarMovementController.cs` (`NetworkBehaviour`, namespace `Avatars`) on the avatar prefab. **Owner-only**: gate all input + movement on `IsOwner` (and, per the project, prefer `IsLocalOrSimulated`-style checks only where the Host acts for a simulated id — but bots have no avatar, so plain `IsOwner` is correct here; document).
  - [x] Move via a **`CharacterController`** (or `Rigidbody`) on the avatar root so the **room wall colliders** stop it (no NavMesh). Replace/augment the basic `CapsuleCollider` from 13.1 as needed. Read `Move` (planar movement relative to camera/avatar facing) + `Look` (yaw the avatar / pitch the camera). Movement **speed, acceleration, look sensitivity = `[SerializeField]` tunables with placeholder defaults — Poyo tunes the feel** (see "Design-open / Poyo-owned").
  - [x] **Lifecycle:** resolve nothing networked in `Awake`; gate on `IsOwner` in `OnNetworkSpawn`; read input only when free-roam is active (Lobby). No per-frame RPC, no custom position NV — `NetworkTransform` (owner) replicates (NFR3/NFR6). `UniTask` for any async; `.Forget()` chains `GetCancellationTokenOnDestroy()` (NFR8).
- [x] **Task 4 — Lobby first-person camera (coexists with BoardCameraManager)** (AC: #4) — ⚠️ first-person per Poyo's 2026-06-13 call (was third-person)
  - [x] Add a new **`CinemachineCamera`** whose pose is driven to the **local** avatar's eye each frame (first-person). It must **coexist** with `BoardCameraManager`'s board cameras (`BoardCamera` = `CinemachineCamera` toggled by `enabled`, `BoardCamera.cs:23-33`) — drive it by **priority**, never delete/bypass the board cams. Only the **owning** client binds it to its own avatar; the local model is **hidden** while first-person is active.
  - [x] Activate the camera **only in the Lobby**; restore board-camera presentation otherwise. 13.2 uses a **minimal Lobby check** mirroring `BoardCameraManager`'s `currentGameStateIndex.OnValueChanged` subscription (`BoardCameraManager.cs:65-86`) — activate iff `GetGameState(idx) is LobbyState`. ⚠️ The **general state→camera-mode arbiter is Story 13.3** — 13.2's activation is intentionally minimal and superseded by 13.3.
  - [x] Eye offset = `[SerializeField]` tunable with placeholder default (Poyo-owned feel). Pitch / clamped look is **Story 13.4** (yaw-only first-person here — the body yaw drives the view).
- [x] **Task 5 — Input-driven tablet open/close (decouple from `openOnCamera`)** (AC: #6, #7)
  - [x] Register the new `ToggleTablet` input to call `SmartphoneController.TryOpenPanel()` / `TryClosePanel()` (already **public**, `SmartphoneController.cs:144-164`) — an open path that works in free-roam **without** the `openOnCamera` board-camera trigger. **Do not touch** the `openOnCamera.onCameraActivated += TryOpenPanel` path (`SmartphoneController.cs:68-72`) — it stays for the untouched states.
  - [x] Keep the text-chat app reachable (the tablet already hosts it via `SmartphoneApp`s). Swipe stays on arrows (`SmartphoneController.cs:85-88`) — unaffected because Move/Look use WASD+mouse.
- [x] **Task 6 — Scene wiring (Unity MCP)** (AC: #1, #4, #6)
  - [x] Wire the avatar prefab's new movement controller fields; place + wire the follow `CinemachineCamera` in `GameScene`; wire the `ToggleTablet` registration. ⚠️ Per the serialized-field rewiring rule: wire every instance, **read back to verify non-null**, null-guard. Watch the array-element `set_property` gotcha ([[reference_unity_mcp_serialized_field_wiring]]).
  - [x] Confirm/flag the **room wall colliders** in `GameScene` (Poyo-authored). If absent, record it — movement bounding is content-blocked until they exist (the controller is still correct).
- [x] **Task 7 — Tests** (AC: #1, #3, #8)
  - [x] PlayMode (extend the 13.1 dual-NM substrate): assert the avatar is **owned by its client** after spawn; an **owner-driven** position change replicates to the remote via `NetworkTransform`; a **non-owner cannot drive** it. Reuse `WaitUntilOrTimeout` polling (never `WaitForSeconds`).
  - [x] Confirm the **13.1 `AvatarSpawnTests` still pass** with owner authority + ownership assignment.
  - [x] Pure input-mapping / clamp logic (e.g. look-angle clamp) → EditMode if extractable to a POCO.
- [x] **Task 8 — Verify** (AC: #8)
  - [x] `read_console` after each change (compile clean) before assuming anything works.
  - [x] `run_tests` — EditMode (3 DI/census guards stay green) **and** PlayMode (new movement test + 13.1 avatar tests + existing suite). Baseline at story start: **EM 215 / PM 157**.
  - [x] GameScene boot smoke: enter Play, console clean, no NRE.

## Dev Notes

### Source-tree placement

- New code → existing **`Assets/Scripts/Avatars/`** (created in 13.1), `Game` asmdef. `AvatarMovementController.cs` lives next to `PlayerAvatar.cs` / `AvatarManager.cs`.
- Input asset: `Assets/InputSystem_Actions.inputactions` (the project's single action asset). `InputID` enum + `InputManager` under `Assets/Scripts/Inputs/`.
- Tests mirror source: `Assets/Scripts/Tests/PlayMode/Avatars/`.

### What 13.1 already built (the substrate — read first)

- `Assets/Scripts/Avatars/PlayerAvatar.cs` — `NetworkBehaviour`, `NetworkVariable<ulong> ownerClientId`, `ApplyAppearance()`. **Thin today** — this is where the movement controller attaches (or a sibling component on the prefab).
- `Assets/Scripts/Avatars/AvatarManager.cs` — scene-placed server spawner, `For(nm)`-only registry (no `static instance`), replicated `NetworkList` + cache, `SpawnAvatar`/`DespawnAvatar`, seat/spawn registry (`GetSeat`/`GetSpawnPoint`). **`SpawnAvatar` is where ownership is assigned** (Task 1).
- `Assets/Prefabs/Avatars/PlayerAvatar.prefab` — `NetworkObject` + `NetworkTransform` (**`AuthorityMode: 0` = Server today** → flip to Owner) + capsule `Model` child + `CapsuleCollider` + appearance SO.
- 13.1 test: `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` — the dual-NM UTP-loopback substrate to **extend** for the ownership/movement assertions.

### Existing systems to cooperate with (do NOT fight them)

- **`BoardCameraManager`** (`Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs`) — `static instance` (a recorded census survivor, opt-out — do **not** add a new locator). Owns board cameras, reacts to `currentGameStateIndex.OnValueChanged` (`:65-86`), pins cameras via `GameState.forceBoardCamera` + `SetActiveSource(BoardCameraInputActiveSource, bool)` (`:77-85`, `:126-129`). The enum is `GameState/Cutscene/Pause` (`:132-137`) — **Story 13.3** will add an avatar/free-roam source; **13.2 must not** repurpose it. Arrow nav registered in `Start` (`:60-63`).
- **`BoardCamera`** (`BoardCamera.cs`) — `[RequireComponent(CinemachineCamera)]`; `ActivateCamera`/`DeactivateCamera` toggle `cinemachineCamera.enabled` and fire `onCameraActivated`/`onCameraDeactivated` (`:23-33`). The follow cam is a **new** `CinemachineCamera` alongside these; the project already uses **Cinemachine 3.1.5** with one `CinemachineBrain` on the main camera.
- **`SmartphoneController`** (`Assets/Scripts/Smartphone/SmartphoneController.cs`) — `NetworkController`. Opens via `openOnCamera.onCameraActivated += TryOpenPanel` in `Awake` (`:68-72`); `TryOpenPanel()`/`TryClosePanel()` are **public** (`:144-164`); swipe on arrows in `Start` (`:85-88`). 13.2 adds an input toggle calling the public methods — **leave `openOnCamera` wiring intact**.
- **`InputManager`** (`Assets/Scripts/Inputs/InputManager.cs`) — `static instance` (recorded census survivor / opt-out global façade — keep using `InputManager.instance`, it is the sanctioned pattern here, NOT a locator to remove). `RegisterAction(InputID, InputState, Action)` (`:40`) for buttons; a generic `RegisterAction<T>` exists but `GetInputActions<T>` returns null today (`:195-203`) — extend it if you route `Move`/`Look` through the manager, or read the generated asset directly in the owner controller.
- **`InputID`** (`Assets/Scripts/Inputs/InputID.cs`) — only `ArrowUp/Down/Left/Right` + `EscapePressedStack`. Append `Move`/`Look`/`ToggleTablet`.

### Networking discipline (NFR3 / NFR6 — the load-bearing rule for this story)

- **Owner authority is a SCOPED, DOCUMENTED deviation** from the project's strict server-authority rule (project-context.md §NGO ownership), justified because the avatar position is **cosmetic presence only** and mutates **no** game state. All game state (votes/roles/corruption/chaining) stays server-authoritative; clients still propose via `ServerRpc`. Put this justification in a comment on `AvatarMovementController` + the `NetworkTransform` flip.
- **Position rides `NetworkTransform` only** — interpolated, owner-authoritative. **Never** a custom per-frame `NetworkVariable<Vector3>`; **never** a per-frame movement RPC. Tune `NetworkTransform` thresholds so an idle avatar costs ~zero (NFR6, ~30 Hz tick).
- Ownership: 13.1 spawns server-owned. For owner movement the avatar must be owned by its client — assign at spawn (`SpawnWithOwnership`/`InstantiateAndSpawn(..., ownerClientId)`) or `NetworkObject.ChangeOwnership(clientId)` server-side. Re-gate the 13.1 test.

### Design-open / Poyo-owned (do NOT invent the feel — placeholder defaults + Inspector tunables)

- **Movement feel:** walk speed, acceleration/damping, gravity, look sensitivity, invert-Y → `[SerializeField]` with neutral placeholders; Poyo tunes in-editor + playtest.
- **Follow-camera feel:** distance, height, shoulder offset, damping, FOV, pitch/yaw limits for free-roam → `[SerializeField]` placeholders.
- **Bindings:** Move = WASD, Look = mouse delta (placeholders); `ToggleTablet` key (e.g. Tab) — Poyo confirms. Touch bindings deferred (NFR5, structure only).
- **Room wall colliders:** authored by Poyo, **no NavMesh** (epic `scope_decisions.movement_bounds`). The controller is collider-agnostic (a `CharacterController` collides with whatever exists). If `GameScene` has no room walls yet, **flag it** — movement bounding (AC #2) is content-blocked until Poyo authors them, but the code ships correct.
- **⚠️ Movement + camera feel is golden-blind** → wants a Poyo playtest (lobby→host flow). Automated tests cover ownership/replication, not feel.

### Scope boundaries (what is NOT this story)

- **State→camera-mode arbitration across all states = Story 13.3.** 13.2 only needs the follow cam live in the Lobby; keep the activation minimal and clearly marked as superseded by 13.3 (don't build the general arbiter here).
- **Embodied Vote seating / look clamp = Story 13.4.** No seat-snapping here.
- **Disabling board-camera arrow-nav in Lobby = 13.3.** 13.2 avoids the conflict by using WASD+mouse (separate bindings), and must not regress the untouched states' arrow behaviour.

### Previous-story intelligence (13.1 — just completed, at `review`)

- 13.1 is **uncommitted** and **awaiting Poyo's accept + playtest**. 13.2 builds on its (working, tested) code. If Poyo's review changes the 13.1 avatar shape, re-baseline.
- Patterns established: born-clean managers (`For(nm)`-only, no `static instance`); appearance SO on the **prefab** (network-correct); thin `PlayerAvatar` (manager owns the authoritative list). Keep the same shape.
- MCP gotchas hit in 13.1 ([[reference_unity_mcp_serialized_field_wiring]]): create-time `component_properties` unreliable → use `modify_contents`/`set_property` after + read back; `List<T>` object-ref arrays via `set_property` go null → set per-element `_field.Array.data[i]`.

### Project Structure Notes

- Aligns with feature-first `Avatars/` folder, `Game` asmdef, suffix-`Manager`/`Controller` naming, `[SerializeField] private`, `OnNetworkSpawn`/`OnNetworkDespawn` lifecycle — all standard here.
- **Variance:** owner-authoritative `NetworkTransform` is a *deliberate documented deviation* from strict server-authority (NFR3) — scoped to cosmetic avatar position only. Record it in code.

### Project Context Rules (from `_bmad-output/project-context.md`)

- **`IsOwner` ≠ write authority on game state.** Owner authority here applies ONLY to the avatar's `NetworkTransform` (cosmetic). Never mutate game `NetworkVariable`s from the client.
- **Position → `NetworkTransform`** (interpolated), never custom per-frame `NetworkVariable<Vector3>`; `NetworkVariable` mutated by event, never per frame; no per-frame RPC (~30 Hz tick).
- **Input System only** (1.14.2) — all bindings in `Assets/InputSystem_Actions.inputactions`; never legacy `Input.GetKey`. Author touch/gamepad/keyboard so the mobile port isn't structurally blocked (NFR5).
- **Networked init in `OnNetworkSpawn`, teardown in `OnNetworkDespawn`**; gate owner logic on `IsOwner`. `Awake` only for non-networked init.
- **`GetSafeRpcTarget`/`IsLocalOrSimulated`/`clientId >= 100`** semantics preserved; bots get no avatar (so no movement/ownership) — bot-debug flow unbroken (NFR4).
- **Async = `UniTask`**; `.Forget()` chains `GetCancellationTokenOnDestroy()`. **Audio = FMOD** (not relevant until voice, 13.5/13.6).
- **`[SerializeField] private`**; rename only with `[FormerlySerializedAs]`. Wire every scene/prefab instance via MCP, verify by read-back, null-guard.
- **DI seam:** depend on injected slices / `For(nm)` in `OnNetworkSpawn`; never `GameManager.instance`/`CharacterManager.instance`/`*.For(` in migrated types. `BoardCameraManager.instance`/`InputManager.instance` are **recorded census survivors (opt-out)** — using them is the sanctioned pattern, not a violation; do **not** register the new avatar types into `DiSeamMigratedConsumers`.
- **Unity MCP workflow:** `read_console` after every change; `run_tests` filtered before declaring done; `manage_scene`/`manage_gameobject`/`manage_prefabs` for wiring; prefer `create_script` for new `.cs` ([[reference_unity_silent_compile_exclusion]]).

### References

- [Source: _bmad-output/planning-artifacts/epics-player-embodiment.md#Story 13.2] — story + ACs; FR3/FR4, NFR3/NFR5/NFR6/NFR8, DO1.
- [Source: _bmad-output/implementation-artifacts/13-1-foundation-networked-avatar-seat-registry-and-appearance-hook.md] — the avatar/manager/prefab substrate this story extends.
- [Source: Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs#60-86,126-137] — board-camera nav, state reaction, `SetActiveSource`/`BoardCameraInputActiveSource` (the follow cam must coexist; 13.3 owns the arbiter).
- [Source: Assets/Scripts/Board/BoardCameraSystem/BoardCamera.cs#8-33] — `CinemachineCamera` enable + `onCameraActivated`/`onCameraDeactivated` (the follow cam pattern + the tablet open trigger).
- [Source: Assets/Scripts/Smartphone/SmartphoneController.cs#37,68-72,85-88,144-164] — `openOnCamera` trigger, public `TryOpenPanel`/`TryClosePanel`, arrow swipe (decouple the open; leave `openOnCamera` intact).
- [Source: Assets/Scripts/Inputs/InputManager.cs#40,105-135,169-234] — `RegisterAction`, the generic `RegisterAction<T>`/`GetInputActions<T>` (returns null today — extend), `PlayerInput` dispatch methods.
- [Source: Assets/Scripts/Inputs/InputID.cs] — enum to extend with `Move`/`Look`/`ToggleTablet`.
- [Source: Assets/InputSystem_Actions.inputactions] — single action asset; add `Move`/`Look`/`ToggleTablet` to the `Player` map (WASD 2D-composite + mouse delta).
- [Source: Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs] — dual-NM substrate to extend for ownership/replication assertions.
- [Source: _bmad-output/project-context.md] — NGO ownership/authority, NetworkTransform, Input System, lifecycle, DI seam, MCP rules.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — gds-dev-story

### Debug Log References

- `manage_prefabs modify_contents` rejected `CharacterController.m_Center` (serialized name) as "Unsupported SerializedPropertyType: Vector3" — the whole call was atomic (no changes saved). Fixed by using the C# property names `center`/`height`/`radius` (same as the 13.1 CapsuleCollider wiring). See [[reference_unity_mcp_serialized_field_wiring]].

### Completion Notes List

- **⚠️ FIRST-PERSON camera (Poyo's design call, 2026-06-13) — overrides the epic's third-person follow.** FR4 / AC#4 / `scope_decisions.lobby_camera` in `epics-player-embodiment.md` + this story updated. `AvatarFollowCamera` (kept the name to avoid a scene-component re-wire) now copies the local avatar's eye-height pose onto a bare `CinemachineCamera` each `LateUpdate` (the brain reads the vcam transform → 1:1 first person), yaw-only (body yaw drives the view; pitch/clamped look deferred to 13.4). The local avatar's own model is hidden while first-person is active (so the player doesn't see inside their own capsule); remote clients still see it.
- **Owner authority (NFR3, scoped deviation):** `PlayerAvatar.prefab` `NetworkTransform` flipped Server→Owner (`AuthorityMode: 1`); `AvatarManager.SpawnAvatar` now spawns the avatar OWNED by its client (`InstantiateAndSpawn(ownerClientId: clientId, position/rotation)` — position rides the spawn payload so it lands on every replica regardless of authority). Documented in code as cosmetic-position-only; game state stays server-authoritative.
- **Input (deviation, documented):** Move/Look/ToggleTablet added to the `Player` map of `InputSystem_Actions.inputactions` (WASD 2D-composite + `<Mouse>/delta` + `<Keyboard>/tab`). The continuous Move/Look are read by the owner controller via a **runtime CLONE** of the InputActionAsset (`Instantiate` + `FindAction` + `ReadValue`), NOT routed through the discrete-event `InputManager` wrapper — because (a) continuous values fit value-reads not button events, (b) `InputManager.GetInputActions<Vector2>` is stubbed, and (c) the scene `PlayerInput` is `InvokeUnityEvents` (per-action UnityEvent wiring is fragile headless). Still the Input System (not legacy `Input.GetKey`). The clone isolates enable/disable from the scene PlayerInput's asset instance.
- **`AvatarMovementController`** (on the prefab, owner-gated): CharacterController planar move + body yaw from Look.x; gravity grounds it against the room floor/wall colliders (no NavMesh). Replaced the 13.1 `CapsuleCollider` with a `CharacterController` (its own collider). Feel (`_moveSpeed`/`_lookYawSpeed`/`_gravity`) = placeholder `[SerializeField]` defaults — **Poyo tunes**.
- **Tablet decouple (DO1):** `TabletToggleInput` on the Tablet GO reads ToggleTablet (asset clone) and calls the already-public `SmartphoneController.TryOpenPanel`/`TryClosePanel`. The `openOnCamera` camera-coupled path is **untouched**.
- **Camera lobby-gating (NFR1):** `AvatarFollowCamera` mirrors `BoardCameraManager`'s `currentGameStateIndex.OnValueChanged` subscription and activates ONLY when `GetGameState(idx) is LobbyState`, by priority (board cams not removed). The general state→mode arbiter is **Story 13.3**.
- **Verification:** compile clean (0 errors); **EditMode 215/215** (DiSeam/SceneWiring/StaticAbsence guards green); **PlayMode 158/158** — the 13.1 avatar tests still pass under owner authority, plus a new `Avatar_IsOwnedByItsClient_AndOwnerDrivenPositionReplicates` (client drives its OWN avatar on the client side, host replica receives — distinguishes owner from server authority). GameScene boot smoke clean (no NRE). All scene `[SerializeField]` wiring read back non-null.
- **⚠️ Poyo-owned, NOT done here (authorized as scaffolding-with-placeholders):** movement + camera FEEL (speed/sensitivity/eye-offset) tuning; **room wall colliders** (no NavMesh) — movement bounding "just works" once they exist in GameScene; visual playtest of first-person + walking via the real lobby→host flow (golden-blind). NOT `# REVIEW-REQUIRED` (mergeable direct) but a new owner-auth networked controller + input changes → Poyo may want a cheap `/gds-code-review`.

### File List

- `Assets/Scripts/Avatars/AvatarMovementController.cs` — NEW. Owner-only CharacterController movement; reads a runtime InputActionAsset clone (Move/Look).
- `Assets/Scripts/Avatars/AvatarFollowCamera.cs` — NEW. First-person Lobby camera (pose-copy to the local avatar's eye; lobby-gated by priority; hides the local model).
- `Assets/Scripts/Avatars/TabletToggleInput.cs` — NEW. Input-driven tablet open/close decoupled from `openOnCamera`.
- `Assets/Scripts/Avatars/AvatarManager.cs` — UPDATE. `SpawnAvatar` spawns owned-by-client + position via spawn payload.
- `Assets/Prefabs/Avatars/PlayerAvatar.prefab` — UPDATE. NetworkTransform Server→Owner; CapsuleCollider → CharacterController; +AvatarMovementController (`_inputActions` wired).
- `Assets/InputSystem_Actions.inputactions` — UPDATE. `Player` map +Move (WASD), +Look (mouse delta), +ToggleTablet (Tab).
- `Assets/Scenes/GameScene.unity` — UPDATE. +`AvatarFollowCamera` (CinemachineCamera + component, wired `_camera`/`gameManager`); +`TabletToggleInput` on the Tablet (wired `_smartphone`/`_inputActions`).
- `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` — UPDATE. Test prefab NetworkTransform forced Owner; +ownership/owner-driven-replication test.
- `_bmad-output/planning-artifacts/epics-player-embodiment.md` — UPDATE. FR4/AC#4/scope_decisions → first-person (Poyo's call).

### Change Log

| Date | Change |
|---|---|
| 2026-06-13 | Story 13.2 implemented — owner-auth movement controller, **first-person** Lobby camera (Poyo's design call, overrides epic third-person), Move/Look/ToggleTablet input, tablet open decoupled from openOnCamera. EM 215/215, PM 158/158. Status → review. |
| 2026-06-13 | Playtest follow-up (Poyo: "can't look up, only sides"): added first-person look **pitch** (up/down). `Look.y` pitches a local `Eye` pivot child on the avatar (clamped, default ±80°, tunable); `AvatarFollowCamera` now copies the eye-pivot world pose (body yaw + local pitch). Body yaw stays networked; pitch is local view only. EM 215/215, PM 158/158 (avatar 4/4). |
