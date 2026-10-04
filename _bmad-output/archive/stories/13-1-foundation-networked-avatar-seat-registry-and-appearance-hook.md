# Story 13.1: Foundation — networked avatar, seat registry & appearance hook

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want a networked avatar spawned and persisted per **real** player, mapped to seats/spawn points, with a data-driven appearance hook,
so that every later movement/camera/voice story (13.2–13.6) has a replicated, identity-bound avatar to attach to.

> **Epic 13 — Player Embodiment & Physical Presence.** New gameplay feature, independent of the refactor (Epics 1–12). This is the **foundation** story: spawn + persist + despawn + seat registry + appearance seam. **No movement, no camera, no embodiment, no voice** — those are 13.2–13.6. Keep this story tight.

## Acceptance Criteria

1. An avatar prefab with a `NetworkObject` + `NetworkTransform` is **spawned server-side** for each connected **real** client (`InstantiateAndSpawn`, server-only) and is **despawned via `Despawn(destroy: true)`** on disconnect and at match end (no client-side `Destroy`).
2. The avatar **persists across all game states** for the lifetime of the match — it is spawned once (at/after lobby join) and is **not** re-spawned per phase.
3. A **seat/spawn registry** maps `clientId ↔ Character ↔ seat transform` and exposes lobby spawn points; seats are defined in a **stable order** so a later story can place the local client at the front seat (DO3). *(The actual front-seat rotation is Story 13.4 — here only the stable mapping + spawn points are required.)*
4. Appearance is driven by a **data-driven hook** that loads a **single shared 3D model** today, with the seam shaped so per-player customization can be added later without re-architecting (FR8 / DO6). Customization itself is **not** implemented.
5. The avatar lifecycle uses `OnNetworkSpawn`/`OnNetworkDespawn` (not `Awake`/`OnDestroy`) for networked init/teardown, and any mutable `static` resets via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` under `#if UNITY_EDITOR` (domain-reload-disabled rule).
6. **Simulated `clientId >= 100` bots get no avatar** (DO2): avatar spawn is gated to real connected clients (`clientId < 100`), while `GetSafeRpcTarget` / `IsLocalOrSimulated` and the bot-debug flow stay intact (NFR4).
7. A **PlayMode multi-client test** (host + at least one real in-process client, built on the `MultiClientGameFixture` substrate) proves an avatar spawns for each **real** client, is visible on the remote replica, despawns cleanly on disconnect, and **no avatar is spawned for a simulated bot**.

## Tasks / Subtasks

- [x] **Task 1 — Avatar prefab + NGO registration** (AC: #1, #4, #6)
  - [x] Create `Assets/Prefabs/Avatars/PlayerAvatar.prefab` with: root `NetworkObject`, `NetworkTransform`, the 3D model (placeholder capsule per Poyo's decision — no humanoid asset in repo yet) as a child `Model`, a new `PlayerAvatar` component, and a basic `CapsuleCollider` on the root.
  - [x] Set `NetworkTransform` authority to **server** for now (`AuthorityMode: 0`). No custom `NetworkVariable<Vector3>` — position rides `NetworkTransform` (NFR3).
  - [x] Prefab **auto-registered** in `DefaultNetworkPrefabs.asset` by NGO's prefab post-processor on creation (verified: last entry, guid `88efd967…`).
- [x] **Task 2 — `PlayerAvatar` component** (AC: #4, #5)
  - [x] New file `Assets/Scripts/Avatars/PlayerAvatar.cs` (`NetworkBehaviour`, namespace `Avatars`).
  - [x] `NetworkVariable<ulong> ownerClientId` (mirror `Character.ownerClientId`, `Character.cs:18`).
  - [x] `OnNetworkSpawn` applies appearance. **No manager resolution needed in 13.1** (PlayerAvatar is thin; tracking is done BY `AvatarManager` via its authoritative list — see Completion Notes). Lane-C resolution slot documented for later.
  - [x] `ApplyAppearance()` reads the **data-driven appearance hook** (single shared SO) and applies the shared body material to child renderers (null-tolerant).
  - [x] No subscriptions added → no teardown needed (kept thin); `OnNetworkSpawn`/`OnNetworkDespawn` lifecycle used.
- [x] **Task 3 — `AvatarManager` (scene-placed, server-authoritative spawner)** (AC: #1, #2, #5, #6)
  - [x] New file `Assets/Scripts/Avatars/AvatarManager.cs` (`NetworkBehaviour`, namespace `Avatars`). Mirrors `CharacterManager` lifecycle: per-`NetworkManager` registry `s_byNetworkManager` + `For(nm)`, `OnNetworkSpawn` duplicate-destroy + registry claim, `OnNetworkDespawn`/`OnDestroy` unregister, `#if UNITY_EDITOR [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` statics reset. **Born clean — NO `static instance`** (For(nm)-only), so the census guard needs no whitelist entry.
  - [x] Server-only spawn driver mirroring `LobbyState`: subscribe `OnClientConnectedCallback`/`OnClientDisconnectCallback` + loop already-connected clients in `OnNetworkSpawn` (host + early joiners).
  - [x] `SpawnAvatar(ulong clientId)`: `if (clientId >= 100) return;` (AC #6), `InstantiateAndSpawn(_avatarPrefab, destroyWithScene: true)`, deferred reparent via `WaitForParentToSpawnAndSet`, set `ownerClientId.Value`, position at spawn point, track.
  - [x] Authoritative replicated `NetworkList<NetworkBehaviourReference>` + derived cache (mirror `networkedCharacters`). Double-spawn guard. (No separate server dict — the cache scan by `ownerClientId` mirrors `CharacterManager`.)
  - [x] `DespawnAvatar(ulong clientId)`: remove from the list **before** `NetworkObject.Despawn(true)` (mirror `RemoveCharacter`).
  - [x] **Persistence (AC #2):** spawn once on connect; never on state transitions; `destroyWithScene: true` cleans up at match end.
- [x] **Task 4 — Seat / spawn registry** (AC: #3)
  - [x] `[SerializeField] private List<Transform> _seats;` + `_spawnPoints;` on `AvatarManager` (4 + 4 scene transforms wired in `GameScene`).
  - [x] Stable assignment `clientId → seat index` = the avatar's position in the replicated list (identical on every client). `GetSeat(ulong)` / `GetSpawnPoint(ulong)` deterministic + resolvable everywhere. (Disconnect-reindex caveat documented; real seating is 13.4.)
  - [x] `GetCharacterForClient(ulong)` resolves via `CompositionRoot.For(NetworkManager).CharacterQuery` (lane-C, resolved once in `OnNetworkSpawn`).
- [x] **Task 5 — Scene + CompositionRoot wiring (Unity MCP)** (AC: #1, #3)
  - [x] Scene-placed `AvatarManager` under `---GameLogic---` in `GameScene`; wired `_avatarPrefab`, `_avatarsParent` (= its own Transform, mirroring `CharacterManager._charactersParent`), `_seats`, `_spawnPoints` via Unity MCP — **all verified by reading back** (no null). `_defaultAppearance` lives on the **prefab** (network-correct seam), not the manager — see Completion Notes.
  - [x] Optional CompositionRoot accessor **NOT taken** (not required for 13.1 — `AvatarManager` reached directly via `For(nm)`). No `DiSeamMigratedConsumers` edits.
- [x] **Task 6 — PlayMode multi-client test** (AC: #7)
  - [x] New `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` — self-contained dual-NM UTP-loopback substrate (trimmed `MultiClientGameFixture` technique), registers the avatar prefab + spawns `AvatarManager` on the host.
  - [x] Asserts: a real client sees a replicated avatar for each real `clientId`; `DespawnAvatar` removes it on the remote; `SpawnAvatar(100)` spawns nothing. `WaitUntilOrTimeout` polling only. **3/3 green.**
- [x] **Task 7 — Verify** (AC: all)
  - [x] `read_console` after each change — compile clean (0 errors).
  - [x] `run_tests` — **EditMode 215/215** (DiSeamGuard + SceneWiringGuard + StaticAbsenceGuard green) and **PlayMode 157/157** (3 new avatar + existing suite, no regressions).
  - [x] GameScene boot smoke: entered Play, no NRE, console clean (host start is lobby-driven so no avatars spawn on direct Play — real spawn proven by the multi-client test).

## Dev Notes

### Source-tree placement (feature-first)

- New system → **new folder** `Assets/Scripts/Avatars/` (project rule, `project-context.md` §Code Organization). Stays in the **`Game` asmdef** — no new asmdef for the foundation.
- Prefab under `Assets/Prefabs/Avatars/`. Appearance SO (if used) under `Assets/ScriptableObjects/Avatars/`.
- Tests mirror source path: `Assets/Scripts/Tests/PlayMode/Avatars/`.

### The spawn pattern to imitate (READ THIS — it is the whole story)

`CharacterManager.AddNewCharacter` (`CharacterManager.cs:400–432`) is the canonical server-side per-client spawn in this codebase. **Imitate it for avatars:**

- `NetworkObject _no = NetworkManager.SpawnManager.InstantiateAndSpawn(_avatarPrefab, destroyWithScene: true);` — server-only.
- Reparent under a scene parent NetworkObject; if the parent isn't spawned yet, defer with a coroutine (`WaitForParentToSpawnAndSet`, `CharacterManager.cs:513–534`).
- Set `ownerClientId.Value` after spawn.
- Add to the authoritative replicated `NetworkList<NetworkBehaviourReference>` (`CharacterManager.cs:171`, `:424`) — this is how late joiners get the full set; the list arrives pre-populated **without** raising `OnListChanged`, so force a rebuild in `OnNetworkSpawn` (`CharacterManager.cs:232–235`).
- **Known spawn-timing caveat** recorded in the code: `CharacterManager.cs:400` carries `//todo: create all characters on start … to not have spawn issues`. Cross-object `OnNetworkSpawn` order is **not guaranteed** (`project-context.md` §NGO lifecycle). Resolve dependencies (CharacterManager, parent) lazily / via `OnClientConnectedCallback`, never assume another object spawned first.

### Lifecycle & registry (mirror CharacterManager exactly)

- `Awake`: claim `instance`/registry only if free (`CharacterManager.cs:178–192`) — NGO assigns `NetworkManagerOwner` **after** `Object.Instantiate` returns, so `Awake` cannot tell a foreign-NM replica from a duplicate (Design B). Do the authoritative duplicate-destroy + registry claim in `OnNetworkSpawn` (`CharacterManager.cs:194–236`).
- Per-`NetworkManager` registry `Dictionary<NetworkManager, AvatarManager> s_byNetworkManager` + `static AvatarManager For(NetworkManager)` (`CharacterManager.cs:44–64`). This is what lets the `MultiClientGameFixture`'s second in-process NM resolve its own avatar manager.
- `OnNetworkDespawn` + `OnDestroy`: unsubscribe lists and `UnregisterFromRegistry()` by value-scan (`CharacterManager.cs:238–287`) — `NetworkManager.Singleton` may be null during shutdown, never key off it.
- **Domain reload is disabled** → statics survive Play sessions. Add the `#if UNITY_EDITOR [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset (`CharacterManager.cs:75–81`).

### Resolving dependencies = lane C via CompositionRoot (NOT the locator)

- `PlayerAvatar`/`AvatarManager` resolve `CharacterManager`/`CharacterQuery` via `CompositionRoot.For(NetworkManager).CharacterQuery` **once in `OnNetworkSpawn`** (lane C) — exactly like `Character.cs:49`. `CompositionRoot.For(nm)` returns a lightweight `Services` value resolver (`CompositionRoot.cs:57, 181`).
- **Never** call `CharacterManager.instance` / `CharacterManager.For(` / `GameManager.instance` / `.For(` in your source — guard #1 (`DiSeamNoLocatorGuardTests.cs:39`) forbids those substrings. `CompositionRoot.For(` is the sanctioned form (not forbidden).
- **Guard impact for a NEW manager:** the DI-seam guards only scan *registered migrated consumers* (`DiSeamMigratedConsumers.All` / `NoLocatorOnly`). A brand-new type that never used the locator does **not** need to be registered. Only register `AvatarManager` (and add to `InjectedManagerTypes`) if you choose Task 5's optional CompositionRoot accessor and want its `[SerializeField]` wiring guard-checked. Don't register it into `All` (that list is for *migrated* consumers).
- The **endgame static census** (`StaticSingletonCensusGuardTests`) fails on any non-whitelisted `static instance`/`Instance`. `AvatarManager` mirrors CharacterManager's pattern (a `static For` backbone + optional `instance` façade). If you add a `static instance`, expect the census guard to flag it → either avoid a `static instance` (prefer `For(nm)`-only resolution like `CompositionRoot`) **or** add a whitelist entry with a reason. **Preferred: `For(nm)`-only, no `static instance`** — this is a fresh manager with no legacy callers, so it can be born clean (the whole refactor's end-state goal).

### Spawn driver: where the per-client spawn is triggered

- `LobbyState` already drives per-client `Character` creation on the server: `OnStartStateServer` subscribes `OnClientConnectedCallback` (`LobbyState.cs:65–69`), `OnStateCreated` loops already-connected clients (`LobbyState.cs:57–62`), `OnEndStateServer` unsubscribes (`LobbyState.cs:71–75`), `OnClientDisconnected` removes (`LobbyState.cs:26–29`).
- **Recommended:** give `AvatarManager` its **own** server-side connect/disconnect subscription (cohesive, keeps `LobbyState` lean) rather than calling into it from `LobbyState`. Mirror the same already-connected-loop so the host's own avatar spawns. If ordering against `Character` creation matters for the seat mapping, resolve the `Character` lazily (it may spawn a frame later) — use `ICharacterQuery.GetCharacter` which tolerates in-flight refs, or the `GetCharacterAsync` promise pattern (`CharacterManager.cs:85`).

### Appearance seam (data-driven, single model now — FR8/DO6)

- Minimal seam that won't need re-architecting: a `[SerializeField] private AvatarAppearanceData _defaultAppearance;` (a new ScriptableObject under `Assets/ScriptableObjects/Avatars/`) read by `PlayerAvatar.ApplyAppearance()`. Today it points at the one shared model/material; later a `NetworkVariable<int> appearanceId` (or a per-player profile) selects among entries — the **call site doesn't change**.
- **SOs are read-only at runtime** (`project-context.md` §ScriptableObject) — never mutate the appearance SO; if per-player runtime state is needed later, clone via `Instantiate(so)`. For 13.1, read-only is enough.
- Do **not** over-build: no customization UI, no per-player data, no networked appearance var yet. Just the hook + the single model.

### NetworkTransform / networking discipline (NFR3)

- Position replicates via `NetworkTransform` only — interpolated, **no** custom per-frame `NetworkVariable<Vector3>`, **no** per-frame RPC (`project-context.md` §Networking bandwidth). Server authority for the transform in 13.1; 13.2 flips to **owner** authority for responsive local movement (cosmetic position only — game state stays server-authoritative).
- All game-state mutation stays server-authoritative; the avatar layer mutates **no** game state.

### Files this story TOUCHES

| File | NEW/UPDATE | What |
|---|---|---|
| `Assets/Scripts/Avatars/AvatarManager.cs` | NEW | Scene-placed server spawner + per-NM registry + seat registry. |
| `Assets/Scripts/Avatars/PlayerAvatar.cs` | NEW | Per-avatar `NetworkBehaviour`: identity, appearance hook. |
| `Assets/Scripts/Avatars/AvatarAppearanceData.cs` | NEW (optional) | Appearance SO seam. |
| `Assets/Prefabs/Avatars/PlayerAvatar.prefab` | NEW | NetworkObject + NetworkTransform + model. Register in NetworkPrefabs. |
| `GameScene` | UPDATE | Place + wire `AvatarManager` (MCP); add seat/spawn-point transforms. |
| NGO NetworkPrefabs list (`DefaultNetworkPrefabs`) | UPDATE | Register `PlayerAvatar.prefab`. |
| `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` | NEW | Multi-client fixture-based test. |
| `CompositionRoot.cs` + `DiSeamMigratedConsumers.cs` | UPDATE (optional) | Only if Task 5's optional accessor is taken. |

No existing gameplay file is modified destructively. `LobbyState` is **not** required to change if `AvatarManager` owns its own connect subscription (recommended).

### Testing standards

- **PlayMode** for anything touching `NetworkBehaviour`/spawn/RPC (`project-context.md` §EditMode vs PlayMode). The avatar spawn is multi-client → use the `MultiClientGameFixture` substrate (host + real client), not StartHost-only (StartHost can't prove "visible on the remote").
- Never `WaitForSeconds` / `Thread.Sleep` — poll with `WaitUntilOrTimeout` / yield one frame (`MultiClientGameFixture.cs:176, 293`).
- The bot-skip (AC #6) is asserted by calling the spawn path with `clientId >= 100` and proving **no** NetworkObject appears (and the bot-flow `GetSafeRpcTarget` interception still works — see `AssertSimulatedBotIsIntercepted`, `MultiClientGameFixture.cs:311`).
- `[TearDown]` despawns spawned NetworkObjects and resets statics (fixture already does this for GM/CM — extend for the avatar manager).

### Project Structure Notes

- Aligns with feature-first folders, `Game` asmdef, suffix-`Manager` naming, `[SerializeField] private` fields, `NetworkBehaviour` lifecycle in spawn/despawn — all standard here.
- **Variance / decision:** prefer **no `static instance`** on `AvatarManager` (resolve via `For(nm)` and/or a CompositionRoot accessor). This deviates from older managers (which carry a façade) but matches the refactor's end-state and avoids a new census-guard whitelist entry. Born clean.

### Project Context Rules (from `_bmad-output/project-context.md`)

- **Server-only spawn:** `NetworkObject.Spawn`/`InstantiateAndSpawn` server-side only; clients never spawn/`Destroy` a NetworkObject (`§NGO lifecycle`). Despawn via `Despawn(destroy:true)`.
- **Networked init in `OnNetworkSpawn`, teardown in `OnNetworkDespawn`** (unsubscribe `NetworkVariable.OnValueChanged` there); `Awake` only for non-networked init.
- **Cross-object `OnNetworkSpawn` order not guaranteed** — defer via `OnClientConnectedCallback` / lazy resolve.
- **Domain reload disabled** — reset mutable statics via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- **Position → `NetworkTransform`** (interpolated), never custom per-frame `NetworkVariable<Vector3>`; `NetworkVariable` mutated by event, never per frame.
- **`GetSafeRpcTarget(clientId)` / `IsLocalOrSimulated(clientId)`** semantics preserved; `clientId >= 100` = simulated bot (gets no avatar here).
- **Async = `UniTask`**; `.Forget()` chains `GetCancellationTokenOnDestroy()`. **Audio = FMOD** (not relevant until voice, 13.6).
- **`[SerializeField] private`** fields; rename only with `[FormerlySerializedAs]`. Wire every scene/prefab instance via MCP, verify, null-guard.
- **SOs read-only at runtime**; clone via `Instantiate(so)` for mutable state.
- **Unity MCP workflow:** `read_console` after every change; `run_tests` filtered before declaring done; `manage_scene`/`manage_gameobject` for scene wiring; prefer `manage_script` create for brand-new `.cs` (avoids the silent compile-exclusion gotcha — `reference_unity_silent_compile_exclusion`).

### References

- [Source: _bmad-output/planning-artifacts/epics-player-embodiment.md#Story 13.1] — story + ACs + DO1–DO6 decisions.
- [Source: Assets/Scripts/Characters/CharacterManager.cs#400-432] — `AddNewCharacter` spawn pattern (imitate).
- [Source: Assets/Scripts/Characters/CharacterManager.cs#434-462] — `RemoveCharacter` despawn pattern.
- [Source: Assets/Scripts/Characters/CharacterManager.cs#44-81,178-287] — per-NM registry + lifecycle + statics reset (mirror).
- [Source: Assets/Scripts/Characters/CharacterManager.cs#513-534] — `WaitForParentToSpawnAndSet` reparent coroutine.
- [Source: Assets/Scripts/GameLogic/GameStates/LobbyState.cs#26-75] — server connect/disconnect subscription + already-connected loop.
- [Source: Assets/Scripts/GameLogic/CompositionRoot.cs#38-106,181-219] — lane-A fields + `For(nm)`/`Services` resolution (lane C consumption).
- [Source: Assets/Scripts/Characters/Character.cs#18,45-66] — `ownerClientId` NV + `OnNetworkSpawn` lane-C resolve precedent.
- [Source: Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs] — host + real-client substrate to extend for the avatar test.
- [Source: Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs + DiSeamNoLocatorGuardTests.cs] — DI-seam guards (forbidden locators; only migrated consumers scanned).
- [Source: _bmad-output/project-context.md] — NGO lifecycle, networking, statics, folders, testing, MCP rules.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Opus 4.8) — gds-dev-story

### Debug Log References

- `manage_gameobject create` `component_properties` did **not** apply for `CapsuleCollider` / `PlayerAvatar._defaultAppearance` at creation time — corrected via `manage_prefabs modify_contents` (headless), verified in the prefab YAML.
- Wiring a `List<Transform>` via `set_property` with an array of GameObject instance IDs produced **null** elements (single object refs resolve GameObject→Transform; arrays do not). Worked around with per-element `SerializedProperty` paths (`_seats.Array.data[i]` / `_spawnPoints.Array.data[i]`), then re-verified by reading the component back.

### Completion Notes List

- **Born clean (refactor end-state):** `AvatarManager` and `PlayerAvatar` carry **no `static instance`** — `AvatarManager` resolution is `For(nm)`-only (mirrors `CompositionRoot`). Confirmed by `StaticSingletonCensusGuardTests` staying green with **no new whitelist entry**. Neither type uses the locator, so neither is registered in `DiSeamMigratedConsumers` (the DI-seam guards only scan registered migrated consumers).
- **Appearance seam lives on the PREFAB, not the manager (deviation from Task 5's literal "wire `_defaultAppearance` on AvatarManager"):** a `[SerializeField] AvatarAppearanceData` reference on the avatar prefab is identical on every client replica with zero network traffic — the network-correct data-driven seam. A manager-held SO reference would **not** reach client avatar replicas (NGO replicates no SO refs). `PlayerAvatar.ApplyAppearance()` reads its own prefab-baked SO; the future `appearanceId` selector changes only the appearance SOURCE, not the call site (FR8 / DO6).
- **`PlayerAvatar` is thin (deviation from Task 2's literal "register the avatar with AvatarManager"):** registration is done **by** the manager via its authoritative replicated `NetworkList` (server adds on spawn; clients rebuild a cache from the pre-populated list) — exactly the `CharacterManager.AddNewCharacter`/`networkedCharacters` precedent, where the spawned `Character` does **not** add itself. This avoids the unguaranteed cross-object `OnNetworkSpawn` order (a client-side avatar→manager callback could fire before the manager replica spawns). `PlayerAvatar.OnNetworkSpawn` therefore only applies appearance; it needs no manager dependency in 13.1.
- **Serialized-field null-guard placement:** `Assert.IsNotNull(_avatarPrefab)` lives in the server branch of `OnNetworkSpawn` (the boundary where it is first needed), **not in Awake** — a runtime-built test prefab sets `[SerializeField]` fields after `AddComponent` (so an Awake assert would fire on the template), and `CharacterManager` likewise asserts nothing on serialized fields in Awake (Design B).
- **Placeholder model:** Poyo chose a capsule placeholder (no humanoid asset in repo). `Assets/Prefabs/Avatars/AvatarBodyPlaceholder.mat` (URP/Lit, teal) is wired into the appearance SO so the hook is exercised; swap-in later is a one-asset change behind the SO seam.
- **Verification:** compile clean (0 errors); **EditMode 215/215** (DiSeamGuard / SceneWiringGuard / StaticAbsenceGuard all green); **PlayMode 157/157** including 3 new `AvatarSpawnTests`; GameScene boot smoke clean (no NRE). All scene `[SerializeField]` wiring read back and confirmed non-null.
- **⚠️ Visual / playtest gap (golden-blind):** the multi-client test proves spawn/replication/despawn/bot-skip, but the *visual* appearance of the avatar in-game (capsule placement at seats/spawns, material) wants a Poyo playtest via the real lobby→host flow.
- **Network-spawn touch — review suggestion:** not tagged `# REVIEW-REQUIRED` in sprint-status, so per the quota-aware policy it can merge direct. Given it introduces a new server-authoritative networked spawner (NetworkList replication + connect/disconnect driver), Poyo may still want a cheap `/gds-code-review` before merge.

### File List

- `Assets/Scripts/Avatars/PlayerAvatar.cs` — NEW. Per-avatar `NetworkBehaviour`: `ownerClientId` identity + data-driven `ApplyAppearance()`.
- `Assets/Scripts/Avatars/AvatarManager.cs` — NEW. Scene-placed server-authoritative spawner; per-NM `For(nm)` registry; replicated `NetworkList` + cache; connect/disconnect driver; seat/spawn registry; bot-skip; statics reset.
- `Assets/Scripts/Avatars/AvatarAppearanceData.cs` — NEW. Appearance SO seam (shared body material + reserved model prefab).
- `Assets/Prefabs/Avatars/PlayerAvatar.prefab` — NEW. NetworkObject + NetworkTransform (server auth) + capsule `Model` child + PlayerAvatar + CapsuleCollider; `_defaultAppearance` wired.
- `Assets/Prefabs/Avatars/AvatarBodyPlaceholder.mat` — NEW. URP/Lit placeholder body material.
- `Assets/ScriptableObjects/Avatars/DefaultAvatarAppearance.asset` — NEW. `AvatarAppearanceData` instance (body material wired).
- `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` — NEW. Multi-client dual-NM PlayMode test (3 cases).
- `Assets/Scenes/GameScene.unity` — UPDATE. Scene-placed `AvatarManager` (+ 4 `Spawn_*` / 4 `Seat_*` transforms) under `---GameLogic---`, fully wired.
- `Assets/DefaultNetworkPrefabs.asset` — UPDATE. `PlayerAvatar.prefab` registered (auto-added by NGO).

### Change Log

| Date | Change |
|---|---|
| 2026-06-13 | Story 13.1 implemented — networked avatar foundation: `PlayerAvatar` + `AvatarManager` (born-clean, For(nm)-only) + appearance SO seam + scene wiring + multi-client PlayMode test. EM 215/215, PM 157/157. Status → review. |
