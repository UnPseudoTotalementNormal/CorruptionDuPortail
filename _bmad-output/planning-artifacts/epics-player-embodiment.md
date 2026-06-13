---
stepsCompleted: ['step-01-validate-prerequisites', 'step-02-design-epics', 'step-03-create-stories']
inputDocuments:
  - '_bmad-output/planning-artifacts/gdds/gdd-Corruption Du Portail-2026-05-29/gdd.md'
  - '_bmad-output/architecture.md'
  - '_bmad-output/project-context.md'
  - 'Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs (code investigation)'
  - 'Assets/Scripts/GameLogic/GameStates/LobbyState.cs (code investigation)'
epic_number: 13
epic_numbering_note: 'Numbered Epic 13 to share the single project sprint-status.yaml namespace with the refactor Epics 1–12 (epics.md). This is a standalone GAMEPLAY FEATURE epic, kept in its own file but globally numbered to avoid story-key collisions.'
scope_decisions:
  embodied_states: 'Vote only'
  movement_authority: 'Owner-authoritative (NetworkTransform owner)'
  discussion: 'In-game proximity voice — DEFERRED until Steam connectivity exists (Facepunch/Steamworks)'
  platforms: 'Desktop/Steam first, mobile deferred'
  voice_active_phases: 'Lobby (free-roam) + Vote (embodied); OFF during Awakening/night'
  movement_bounds: 'Physical room walls (colliders) authored by Poyo — no NavMesh'
  appearance: 'Single shared 3D model now; per-player customization architected as a data-driven hook, NOT implemented in this epic'
  embodied_look: 'Clamped — yaw ±75°, pitch ±40° (approx)'
  bot_avatars: 'No — simulated clientId >= 100 identities get NO avatar'
  seating: 'Local embodied player sits at the FRONT seat from their own POV; others arranged/rotated around the table relative to the local seat'
---

# Corruption Du Portail — Epic 13: Player Embodiment & Physical Presence

## Overview

This document specifies a **new gameplay feature epic** — a persistent, walkable 3D avatar per player layered on top of the existing fixed-camera board game. It is **independent of the POCO/despaghetti refactor** tracked in `epics.md` (Epics 1–12) and is deliberately kept in a separate file so the refactor breakdown stays single-concern. It is numbered **Epic 13** so it shares the single project `sprint-status.yaml` numbering namespace with the refactor without story-key collisions.

**Feature in one sentence:** each player gets a 3D avatar that they can **walk freely** in the room during the **Lobby**, keeps that avatar for the whole match, sees the board through the **existing fixed cameras during Awakening and the rest of the loop (unchanged)**, and **embodies** their avatar — seated around the table, able to look around slightly and talk via **proximity voice**, but unable to move — during the **Vote**.

> **Source of the spec.** This feature is **not** captured in the GDD (its "Development Epics" section is intentionally empty — design-owned). The GDD describes the current build as a *single shared 3D board of cards viewed through Cinemachine board cameras, with a 2D smartphone OS UI overlaid* (GDD §Level Design, §Art). This epic is authored from Poyo's explicit feature request plus the scope decisions captured during planning (see frontmatter `scope_decisions`). Genuine design choices that Poyo has **not** yet made are flagged `[DESIGN-OPEN]` and are **not** invented here.

### Grounding in the current code (verified)

- **No player movement/avatar/controller exists today.** A repository search for `CharacterController` / `PlayerController` / movement / avatar found only UI/card/input code — there is **no walking-character system**. Poyo has the 3D model asset but no spawn/movement script. This epic is therefore **near-greenfield** for the avatar layer.
- **Cameras are fixed.** `BoardCameraManager` (`Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs`) owns a set of fixed `BoardCamera`s around the table. Players navigate between neighbouring cameras with the arrow keys; each `GameState` can pin a camera via `GameState.forceBoardCamera`, which calls `SetActiveSource(BoardCameraInputActiveSource.GameState, false)` to **cut player camera input**. The manager reacts to `currentGameStateIndex.OnValueChanged`. → This epic adds **two new camera modes** that must cooperate with this manager, not fight it.
- **No "Discussion" state.** The 13-state loop (GDD §Core Gameplay Loop) has no dedicated discussion phase; the closest are `VoteState` and `VoteRecapState`. Per Poyo's decision, **only `VoteState`** gets the embodied mode.
- **State-transition ordering is load-bearing.** Client listeners (`BoardCameraManager`, `RoomFog`, `LightManager`) react to `currentGameStateIndex.OnValueChanged`, and the `OnEndStateClient → write currentGameStateIndex.Value → OnStartStateClient` ordering is pinned by the refactor's Story 2.11a golden. Any camera-mode switch wired here must preserve that order.

---

## Requirements Inventory

### Functional Requirements

FR1: A networked avatar prefab (`NetworkObject` + `NetworkTransform`) is spawned **server-side** for each connected player, **persists for the entire match** (the player keeps the same avatar across all states), and is despawned cleanly on disconnect and at match end.
FR2: A **seat/spawn registry** maps `clientId ↔ Character (card) ↔ seat transform` and provides lobby spawn points, so the avatar layer aligns with the existing per-client `Character` model added in `LobbyState`.
FR3: An **owner-authoritative movement controller** (Unity Input System: move + look) drives the local player's avatar; position replicates via `NetworkTransform` (owner authority, interpolated). Movement is bounded by the **room's physical wall colliders** (authored by Poyo — no NavMesh).
FR4: During the **Lobby**, the owning client views the room through a **third-person follow camera** — a new camera mode that **coexists** with `BoardCameraManager` (does not delete or bypass it).
FR5: Camera mode is **arbitrated by game state**: **Lobby → free-roam follow**, **in-loop fixed states (Awakening, recaps, chaining, checks, etc.) → existing board cameras (behavior preserved)**, **Vote → embodied**. Avatar movement input is **disabled outside the Lobby**.
FR6: During **`VoteState`**, each avatar enters **embodied mode**: snapped to its assigned seat around the table, **movement locked**, with **slight clamped look control**, while the **existing per-card vote UI still works**. On leaving `VoteState`, the prior fixed-camera presentation is restored (VoteRecap and onward unchanged).
FR7: **In-game proximity voice chat**: microphone capture + transport + **distance-attenuated spatial playback**, active during **Lobby (free-roam) and Vote (embodied)**, **off during Awakening/night** and other states. Includes push-to-talk and mute. Preferred transport path: **Steam Voice via Facepunch** (already the project's transport) — confirmed in the Story 13.5 spike.
FR8: The avatar ships with a **single shared 3D model** for all players, but its appearance is wired through a **data-driven hook** so **per-player customization can be added later without re-architecting** (customization itself is out of scope for this epic).

### Non-Functional Requirements

NFR1: **Behavior preservation for untouched phases.** All non-Lobby, non-Vote states keep their current fixed-camera presentation exactly (Awakening order, recaps, chaining, victory checks, portal endgame, ending) — no observable change.
NFR2: **Transition ordering intact.** Camera-mode switching is wired via `currentGameStateIndex.OnValueChanged` (like `BoardCameraManager`/`RoomFog`/`LightManager`) and must **not** break the `OnEnd → write currentGameStateIndex.Value → OnStart` ordering pinned by refactor Story 2.11a. A camera-mode switch is a *reaction* to the index, never a driver of it.
NFR3: **Networking discipline.** Avatar position uses `NetworkTransform` (interpolated), never a per-frame custom `NetworkVariable<Vector3>`. Movement is **owner-authoritative** — a **documented, scoped deviation** from the project's strict server-authority rule, justified because the avatar position is **cosmetic presence only** and mutates **no game state**. All game state (votes, roles, corruption, chaining) stays **server-authoritative**; clients still propose via `ServerRpc`.
NFR4: **Simulated-identity discipline.** Any avatar-related RPC respects `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` gateway verbatim. **Simulated bots get NO avatar** (DO2 resolved) — the avatar layer applies to real connected clients only; the bot-debug flow must remain unbroken.
NFR5: **Platform path.** Desktop/Steam first. The Input System action maps are authored so a **mobile touch joystick + touch look** can be added later **without rework**; no Lobby/Vote flow is built in a way that structurally blocks the mobile port.
NFR6: **Performance budgets.** Avatars + voice must hold the 60 fps desktop budget (16.6 ms) and the ~30 Hz network tick. `NetworkTransform` send rate is tuned (not per-frame RPC); voice bandwidth is bounded; off-phase voice and idle avatars cost near-zero.
NFR7: **Audio path.** FMOD remains the gameplay audio system. Voice spatialization integrates with the FMOD mixer **or** documents an explicit exception if Steam Voice bypasses FMOD — decided in the Story 13.5 spike. No `AudioSource` for gameplay sound.
NFR8: **Lifecycle/async hygiene.** `UniTask` only; avatar/voice teardown in `OnNetworkDespawn` (not `OnDestroy`); any mutable `static` resets via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (domain-reload rule). `.Forget()` chains `GetCancellationTokenOnDestroy()`.

### Open Design Decisions (`[DESIGN-OPEN]` — owned by Poyo / GD)

- **DO1 — Lobby UI access while walking.** `[RESOLVED]` The **only** common in-game UI visible during free-roam is the **existing smartphone/tablet** (`SmartphoneController`) — the one that surfaces today when navigating to its camera with the down arrow. It hosts the **text chat** (load-bearing — must stay reachable while walking). The lobby **Start Game** button and the **role-attribution settings** are **out of this epic's scope** — Poyo will handle those himself if needed. **Implementation note (Story 13.2):** today the tablet opens via a **board camera** (`openOnCamera.onCameraActivated += TryOpenPanel`), reached by arrow navigation. In free-roam the board cameras are inactive, so Story 13.2 must add an **input-driven open/close of the smartphone overlay decoupled from `openOnCamera`**, without breaking the camera-coupled open in the untouched states. Arrow keys are currently triple-claimed (BoardCameraManager neighbour nav + SmartphoneController swipe + new movement/look) → input ownership in free-roam must be reconciled.
- **DO2 — Simulated bot avatars.** `[RESOLVED — No]` Simulated `clientId >= 100` identities get **no avatar**. The avatar layer applies to real connected clients only; the bot-debug flow is unaffected.
- **DO3 — Seat assignment rule.** `[RESOLVED]` The **local embodied player must sit at the FRONT seat facing the table from their own viewpoint**; the remaining players are arranged/rotated around the table relative to that local seat. **Implementation route (decided in Story 13.4):** prefer **(A)** fixed global seats + the embodied camera placed at the local client's own seat (avatars keep one shared world position — network-clean), over **(B)** per-client rotated avatar presentation (heavier, desync risk). Intent is fixed; A vs B is a Story-13.4 implementation call.
- **DO4 — Embodied look bounds.** `[RESOLVED]` Clamped look: **yaw ±75°, pitch ±40°** (approx, tunable). Binding (free mouse vs hold-to-look) still a minor Story-13.4 detail.
- **DO5 — Voice technology + routing.** `[DEFERRED]` Postponed until **Steam connectivity exists** (not yet wired). When tackled, it will use **Facepunch (Steam Voice) or Steamworks directly** — no Vivox/Dissonance comparison needed. Stories 13.5–13.6 are **HELD** on this prerequisite.
- **DO6 — Appearance customization scope.** `[RESOLVED]` Out of scope; only the data-driven hook ships now (Story 13.1 / FR8), single shared model.

### FR Coverage Map

- FR1, FR2, FR8 → Story 13.1 (Foundation — networked avatar + seat registry + appearance hook)
- FR3, FR4 → Story 13.2 (Movement + Lobby follow camera)
- FR5 (+ NFR1, NFR2) → Story 13.3 (State-driven camera-mode arbitration)
- FR6 → Story 13.4 (Embodied seated Vote mode)
- FR7 → Story 13.5 (Voice spike — decision) + Story 13.6 (Voice implementation)

8/8 FRs mapped, no orphan. Stories are ordered by dependency: foundation → movement → camera arbitration → embodiment → voice. Story 13.5 (spike) gates Story 13.6.

---

## Epic 13: Player Embodiment & Physical Presence

Give each player a persistent 3D avatar that is **free-roaming in the Lobby**, **invisible to the existing fixed-camera flow during the game loop** (presentation unchanged), and **embodied with proximity voice during the Vote**. Built greenfield on top of the current card-board + `BoardCameraManager` presentation, cooperating with the state-driven camera switch rather than replacing it. Networking is owner-authoritative for the **cosmetic** avatar position only; all game state stays server-authoritative.

**Stories:** 13.1 (foundation) → 13.2 (movement + lobby cam) → 13.3 (camera arbitration) → 13.4 (embodied Vote) → **13.5 `[HELD]`** (voice spike, REVIEW-REQUIRED) → **13.6 `[HELD]`** (voice impl). Stories 13.5–13.6 are **gated on Steam connectivity** (not yet wired); stories 13.1–13.4 are the **shippable near-term slice**.

---

### Story 13.1: Foundation — networked avatar, seat registry & appearance hook

As a developer (Poyo),
I want a networked avatar spawned and persisted per player, mapped to seats/spawn points, with a data-driven appearance hook,
So that every later movement/camera/voice story has a replicated, identity-bound avatar to attach to.

**Acceptance Criteria:**

**Given** `LobbyState` already creates one `Character` (card) per connected client server-side, and no avatar exists today
**When** the avatar foundation lands
**Then** an avatar prefab with a `NetworkObject` + `NetworkTransform` is **spawned server-side** for each connected client (`NetworkObject.Spawn`, server-only) and is **despawned via `Despawn(destroy: true)`** on disconnect and at match end (no client-side `Destroy`)
**And** the avatar **persists across all game states** for the lifetime of the match (it is not re-spawned per phase)
**And** a **seat/spawn registry** maps `clientId ↔ Character ↔ seat transform` and exposes lobby spawn points; seats are defined so that, in embodied mode, the **local client occupies the front seat facing the table** and the other players fill the remaining seats around it in a stable order (DO3 — intent fixed; the front-seat-via-camera vs rotated-presentation route is settled in Story 13.4)
**And** appearance is driven by a **data-driven hook** loading a **single shared 3D model** today, with the seam shaped so per-player customization can be added later without re-architecting (FR8 / `DO6`) — customization itself is **not** implemented
**And** the avatar lifecycle respects `OnNetworkSpawn`/`OnNetworkDespawn` (not `Awake`/`OnDestroy`) for networked init/teardown, and any mutable `static` resets via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`
**And** **simulated `clientId >= 100` bots get no avatar** (DO2 resolved): avatar spawn is gated to real connected clients, while `GetSafeRpcTarget` / `IsLocalOrSimulated` and the bot-debug flow stay intact
**And** a **PlayMode multi-client test** (host + at least one real in-process client via the refactor's `MultiClientGameFixture`, Epic 5 / Story 5.0) proves an avatar spawns for each **real** client, is visible on the remote, despawns cleanly on disconnect, and **no avatar is spawned for a simulated bot**

---

### Story 13.2: Movement controller + Lobby free-roam follow camera

As a developer (Poyo),
I want the local player to walk their avatar freely around the Lobby room with a third-person follow camera,
So that the Lobby becomes a navigable social space instead of a fixed view.

**Acceptance Criteria:**

**Given** the foundation avatar from Story 13.1 and the existing arrow-key `BoardCameraManager`
**When** the movement + lobby camera lands
**Then** an **owner-only** movement controller reads a **new Input System action map** (move + look) and drives the local avatar; **non-owners receive interpolated `NetworkTransform`** updates (owner authority, NFR3)
**And** movement is bounded by the **room's physical wall colliders** (authored by Poyo — no NavMesh); the avatar cannot leave the room
**And** `NetworkTransform` send rate is tuned so movement does **not** fire a per-frame RPC (NFR6) and position is never a custom per-frame `NetworkVariable<Vector3>`
**And** a **third-person follow camera** is active for the owning client **during the Lobby only**, introduced as a new mode that **coexists** with `BoardCameraManager` (the board cameras are not removed)
**And** the action map is authored so a mobile touch joystick + touch look can be added later without rework (NFR5), though only desktop bindings ship now
**And** the **smartphone/tablet UI stays reachable while walking** (DO1 resolved): an **input-driven open/close** of `SmartphoneController` is added that works in free-roam **without** the board-camera trigger (`openOnCamera`), while the existing camera-coupled open path is **unchanged** in the untouched states; the **text chat app** is reachable from it. The lobby **Start button** and **role-attribution settings** are **out of scope** (Poyo's responsibility)
**And** the **arrow-key input conflict is reconciled** for free-roam: arrows are currently claimed by `BoardCameraManager` (camera neighbour nav) and `SmartphoneController` (app swipe); in free-roam, board-camera nav is inactive (Story 13.3), so input ownership routes to movement/look + tablet without regressing the untouched states
**And** the full EditMode + PlayMode suite is green and the console is clean after the change

---

### Story 13.3: State-driven camera-mode arbitration (preserve the fixed-camera game loop)

As a developer (Poyo),
I want camera mode to switch automatically with game state — free-roam in Lobby, fixed board cameras during the loop, embodied during Vote,
So that the new avatar camera modes never leak into the phases that must keep the current presentation.

**Acceptance Criteria:**

**Given** `BoardCameraManager` already reacts to `currentGameStateIndex.OnValueChanged` and honors `GameState.forceBoardCamera` + `SetActiveSource(...)`
**When** the camera-mode arbiter lands
**Then** camera mode maps from state as: **Lobby → free-roam follow (Story 13.2)**, **all in-loop fixed states → existing board-camera behavior unchanged (NFR1)**, **`VoteState` → embodied (Story 13.4)**
**And** the arbiter integrates with the existing manager's source/activation model (e.g. a new `BoardCameraInputActiveSource`/avatar source) rather than bypassing it — board-camera arrow navigation and `forceBoardCamera` keep working exactly as today in the untouched states
**And** avatar **movement input is disabled in every state except Lobby**, and re-enabled on returning to Lobby (there is no return-to-lobby mid-match today; documented as such)
**And** the switch is wired as a **reaction** to `currentGameStateIndex.OnValueChanged` and **does not alter** the `OnEnd → write currentGameStateIndex.Value → OnStart` ordering (NFR2); the refactor's Story 2.11a sequence golden stays **green unchanged**
**And** an EditMode/PlayMode test pins the **state → camera-mode mapping** (a golden table), so a future state reorder or rename breaks a test instead of silently dropping a player into the wrong camera
**And** a manual smoke pass confirms Awakening/recaps/chaining/checks look **identical** to before this epic (NFR1)

---

### Story 13.4: Embodied seated mode during the Vote

As a developer (Poyo),
I want each player snapped into their seat at the table during the Vote — able to look around slightly and stay present, but unable to move — with the existing vote UI intact,
So that the Vote becomes a face-to-face discussion moment without losing the current voting mechanics.

**Acceptance Criteria:**

**Given** the camera arbiter (Story 13.3) routes `VoteState` to embodied mode, and the seat registry (Story 13.1) gives each avatar a seat
**When** `VoteState` is entered
**Then** each player is seated facing the table with their **own avatar at the front seat from their viewpoint** (DO3) and **movement is locked** (the Story 13.2 controller is suppressed for the duration)
**And** the **seating route is settled here**: prefer **(A)** fixed global seats + the embodied camera placed at the local client's own seat (avatars keep one shared `NetworkTransform` world position — network-clean) over **(B)** per-client rotated avatar presentation; the chosen route is documented in the story
**And** the player has **clamped look control: yaw ±75°, pitch ±40°** (approx, tunable per DO4); binding (free mouse vs hold-to-look) documented
**And** the **existing per-card vote UI continues to function** (cast one vote / skip, the early-finish-when-all-voted and tie/skip resolution rules from `VoteState` are **unchanged**) — embodiment is presentation-only and mutates **no** vote state
**And** on **leaving `VoteState`**, the prior fixed-camera presentation is restored and `VoteRecapState` (and everything after) is **visually unchanged** (NFR1)
**And** the embodiment transition reuses the camera arbiter from Story 13.3 and does not introduce its own dependence on the state index ordering (NFR2)
**And** a PlayMode test asserts: on `VoteState` entry avatars are seated + movement locked; the vote can still be cast; on exit movement-lock and camera are restored

---

### Story 13.5 `[HELD]`: Proximity voice — design spike on Steam Voice `# REVIEW-REQUIRED`

> **HELD — blocked on a prerequisite:** Steam connectivity is **not yet wired** in the project. This story (and Story 13.6) cannot start until the Steam connection exists. Voice will be built on **Facepunch (Steam Voice) or Steamworks directly** — the stack is already chosen, so this is a *design* spike (spatialization + routing + UX), not a stack comparison.

As a developer (Poyo),
I want the proximity-voice design (spatialization, FMOD routing, PTT/mute, per-phase gating) settled on the Steam Voice stack before implementation,
So that Story 13.6 is built on a de-risked design once Steam is connected.

**Acceptance Criteria:**

**Given** Steam connectivity is in place (prerequisite) and the audio system is **FMOD**, with **Facepunch/Steamworks** as the chosen voice transport
**When** the spike concludes
**Then** a written decision (`_bmad-output/implementation-artifacts/` or appended here) settles the **Steam Voice (Facepunch/Steamworks) integration approach** — capture, encode, transport, per-peer playback (no Vivox/Dissonance comparison — the stack is decided)
**And** it resolves: **spatialization model** (distance attenuation around the table/room), **FMOD mixer routing vs documented bypass** (NFR7), **push-to-talk binding + default mute state**, and **which phases are voice-on** (Lobby + Vote confirmed; Awakening/night off)
**And** it states how voice behaves under the **simulated gateway** / `clientId >= 100` (real Steam voice needs real peers — solo-debug fallback documented; bots have no avatar/voice per DO2)
**And** it estimates bandwidth/perf against NFR6 and flags whether proximity voice should **spin out into its own epic** if the chosen stack proves heavier than a single story
**And** **no production voice code ships in this story** — it is decision-only (REVIEW-REQUIRED before Story 13.6 starts)

---

### Story 13.6 `[HELD]`: Proximity voice implementation (Lobby + Vote)

> **HELD — blocked on Steam connectivity + Story 13.5.** Same prerequisite as Story 13.5.

As a developer (Poyo),
I want proximity voice working in the Lobby and the Vote, attenuated by distance, with push-to-talk and mute,
So that players can talk face-to-face when embodied and while milling around the Lobby.

**Acceptance Criteria:**

**Given** Steam connectivity is in place and the design decision from Story 13.5
**When** voice is implemented
**Then** microphone **capture + transport + spatial playback** work end-to-end between real Steam peers, with **distance attenuation** keyed off avatar positions (Story 13.1/13.2)
**And** voice is **active only in Lobby and `VoteState`** and **muted in all other states** (Awakening/night silence), driven off the same state signal as the camera arbiter (Story 13.3)
**And** **push-to-talk** and **per-player mute** work as decided in Story 13.5; default mute state matches the decision
**And** audio routing honors NFR7 (FMOD mixer integration or the documented exception) and bandwidth holds NFR6
**And** all networked teardown is in `OnNetworkDespawn`, `UniTask` is used for async capture loops, and statics reset per the domain-reload rule (NFR8)
**And** a manual multi-peer playtest (real Steam clients) confirms intelligible, distance-attenuated voice in both phases and silence elsewhere

---

## Epic summary

6 stories covering FR1–FR8. Foundation-first ordering: **13.1** (networked avatar + seats + appearance hook) → **13.2** (owner-auth movement + lobby follow camera) → **13.3** (state-driven camera arbitration, preserving the untouched loop) → **13.4** (embodied seated Vote) → **13.5 `[HELD]`** (voice spike, REVIEW-REQUIRED) → **13.6 `[HELD]`** (voice implementation). Load-bearing constraints: untouched phases preserved bit-for-bit (NFR1), state-transition ordering never disturbed (NFR2), avatar position owner-authoritative but game state still server-authoritative (NFR3), simulated-gateway semantics intact (NFR4), **no avatar for simulated bots** (DO2).

**Decision status:** DO1 (tablet = the only free-roam UI, input-decoupled from the board camera), DO2 (no bot avatars), DO3 (local-at-front seating), DO4 (yaw ±75°/pitch ±40°), DO6 (single model + hook) are **resolved** — **no open code-blocking decision remains**. **DO5 (voice) is deferred**: Stories 13.5–13.6 are **HELD on Steam connectivity** (not yet wired) and will use Facepunch/Steamworks directly. **Near-term shippable slice = Stories 13.1–13.4** (all design-unblocked).
