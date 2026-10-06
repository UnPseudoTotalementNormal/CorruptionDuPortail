---
project_name: 'Corruption Du Portail'
last_updated: '2026-10-04'
maintenance: 'hand-maintained — add a rule only if breaking it compiles clean and fails silently; delete rules the code makes obvious'
---

# Project Context for AI Agents

Project-specific rules whose violation **compiles clean and breaks silently** (multiplayer, player build, or scene wiring). Generic Unity/C# best practice is deliberately left out. `CLAUDE.md` carries commit conventions and tooling; this file carries the code rules.

Previous 300-rule version: `archive/generated-docs/project-context-2026-06-13.md`.

## Stack (versions = `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`)

Unity **6000.5.0f1** · URP 17.5 · NGO 2.12 (+ Multiplayer Tools 2.2.8, bump together) · UTP + Unity Relay (dtls → wss fallback, `RelayConnector`) and vendored Facepunch/Steam transport (`Assets/Scripts/Facepunch/`, own asmdef, never via UPM) · UniTask · DOTween · FMOD · Input System · Cinemachine 3 · uGUI + UI Toolkit (RoleCard, InfoTable, lobby tablet) · NSubstitute for EditMode tests.

- **C# 9 only.** No `record struct`, file-scoped namespaces, global usings (CS8773). Use `struct` + `IEquatable<T>`.
- `ProjectVersion.txt` is the Editor-version source of truth; never edit or bump it without the team.

## The silent killers

1. **`GetSafeRpcTarget(clientId)` on every RPC target.** `clientId >= 100` = simulated bot; the Host intercepts. Relocate this code verbatim, never "simplify" it. Every new RPC ships with a `MultiClientGameFixture` case.
2. **`IsLocalOrSimulated(clientId)`, never `IsLocalClient`.**
3. **Server authority.** Game state mutated server-side only; clients propose via `ServerRpc`. `NetworkVariable` write perm stays `Server`. Sole sanctioned exception: cosmetic gaze `SeatedYaw/Pitch` (owner-write).
4. **UniTask only** (never `Task`, never new coroutines). Every `.Forget()` in a MonoBehaviour chains `this.GetCancellationTokenOnDestroy()`. Tweens: `AwaitForComplete(ct)`, never `AsyncWaitForCompletion()`.
5. **FMOD via `AudioSystem/GameAudioManager`**, never `AudioSource`. `GameAudioManager` must stay no-op-safe without FMOD (EditMode tests). Event refs must exist in `Assets/FMODBanks/` (else runtime `EVENT_NOTFOUND`).
6. **Depend on injected slices, never locators** (see DI below).

## Architecture & DI (post-refactor, PR #53)

Read `refactor-architecture-despaghetti.md` before touching a manager, DI, or adding a singleton.

### Layering

- **Domain core** `Assets/Scripts/Domain/`, asmdef `CorruptionDuPortail.Domain` (`noEngineReferences`, no refs): pure decision logic. Not even `Vector3`/`Mathf`. `Game → Domain`, never the reverse; never add a Domain reference to fix a compile error.
- Moving a type into Domain → the **test asmdefs need an explicit Domain reference** (`autoReferenced` doesn't reach them → CS0012).
- **A POCO returns a decision, never applies it.** A POCO that writes network state or triggers a transition freezes clients with no exception.
- Powers: one `XDecision` POCO (Domain) + a thin `Power : NetworkBehaviour` adapter. See `implementation-artifacts/spec-powers-poco-v2-architecture.md` ("shipped reality" section). Owner-local effects (reveal/card) run via `RunClientDecisionEffects`, not on the server.

### Three injection lanes (lane = how the object is created; one lane per type)

| Lane | When | Field type | Mechanism |
|---|---|---|---|
| **A** | Scene/prefab-placed MonoBehaviour | concrete | `[SerializeField]`, wired in the asset |
| **B** | Plain class / object our code creates | interface | `Initialize(deps)` by the creator |
| **C** | NGO-spawned replica (`Character`, powers…) | interface slice | resolve once in `OnNetworkSpawn` from `CompositionRoot.For(NetworkManager)` |

None of these fits → stop and ask a human.

- Depend on the **narrow slice** (`IGameLoop`, `IGameStateQuery`, `ICharacterQuery`, `ICharacterCommand`, `IRevealService`), never the manager.
- `CompositionRoot.For(nm)` is the **only** sanctioned static. Call it only in `OnNetworkSpawn`, against `base.NetworkManager`, **never `NetworkManager.Singleton`** (wrong graph in the 2-NM test fixture).
- **No NGO spawn order.** Resolving a *peer manager* via `For(nm)` inside `OnNetworkSpawn` races → scene-wire it as a lane-A `[SerializeField]` instead. Never read another replica's resolved fields during your own spawn.
- Fail loud: `Assert.IsNotNull(dep, "<dep> not wired")` after each resolution. **No `?? X.instance` fallback.**
- Never add a new `static instance`. Grandfathered façades (`GameManager.instance`, `CharacterManager.instance` for static win-rule machinery; `GameAudioManager`, `LobbyManager`, `InputManager`) are not an invitation.
- `GameManager` owns `currentGameStateIndex` + `OnEnd → write NV → OnStart` sequencing by design. Don't "finish removing" it.

### CI guards (run by category before pushing)

`DiSeamGuard` (locator in a migrated type) · `SceneWiringGuard` (null injected `[SerializeField]` in GameScene/prefabs) · `StaticAbsenceGuard` (new static instance). A type joins `DiSeamMigratedConsumers` **last**, after zero locators + every instance wired. Never whitelist your own type in the census.

### Serialized fields (lane A)

Unity serializes by **name**. Append new fields; never rename/reorder/retype (rename unavoidable → `[FormerlySerializedAs]`). A new field is null in every existing instance: wire each one (`find_gameobjects` → `set_serialized_field`), **read it back**, run `SceneWiringGuard`. Tests green ≠ wired.

### Subscriptions

Every subscribe has a mirrored unsubscribe on the **cached** target: spawned replicas in `OnNetworkDespawn`; instantiated-not-spawned objects (`StateUI` subclasses) in `OnDestroy`.

## NGO gotchas found the hard way

- **`ConnectionApproval` must be set identically on host and client**, else NGO rejects every join. Host-only playtests hide it (BootScene serialized `true` + guard test).
- **Join handshake:** after `WaitForConnectedOrTimeout`, NGO has already loaded GameScene and unloaded the menu. Never raw `LoadScene` (destroys NetworkObjects), no UI in the continuation, no `Shutdown` in a generic `catch`.
- **`NetworkList` late-joiner duplicate:** a same-tick entry can arrive twice, permanently. `CharacterManager` dedups by reference (`[CHARLIST]`). Never dedup by `ownerClientId`.
- **A long GameScene sync loses NetworkVariable deltas** unless `NetworkConfig.SpawnTimeout` covers it: NGO defers a
  synchronizing client's object creations (no limit) but purges the deltas / parent syncs for those objects after
  SpawnTimeout (10 s in BootScene) → ghost Character (`ownerClientId = FAKE_CLIENT_ID`), missing player, for the whole
  game. `ClientConnectionPayload.Apply` (every client start path) raises it to `JoinHandshake.DeferredMessageWindowSeconds`;
  a new client start path must call it. Repro: autoplay `join-spawn-during-load`.
- **`DisconnectReason` non-empty ≠ server reason:** every client transport drop yields a `"[Disconnect Event]…"` placeholder. Use `HasServerReason`.
- **`CharacterManager.GetCharacters()` defaults `triggerUpdate: true`** and re-raises `onCharactersListUpdated` at end of frame. Calling it from that event's handler = per-frame loop. Passive readers: `GetCharacters(false)`.
- `Character.isEliminated` is quasi-deprecated: build "player out" on chaining.
- Quit mid-game = seat RESERVED for `GameValues.REJOIN_GRACE_SECONDS` (shown left, skipped at night, no vote; never wait on him), then chained instantly when the delay expires; last anomaly chained → élus win.
- **Rejoin = seat alias.** A rejoined player keeps his ORIGINAL clientId (the seat) everywhere in game state; only the
  transport id is new. Server: `CharacterManager.SeatOfTransport(id)` for every inbound sender id and every
  `NetworkManager.ConnectedClientsIds` entry,
  `TransportOfSeat(seat)` / `GetSafeRpcTarget` for every outbound target (a raw `RpcTarget.Single(seat)` misses him).
  Client: `GetLocalClientId()` (the seat), never `NetworkManager.LocalClientId`. Per-player data pushed once
  (chat channels, icon slices, knowledge) must also be re-sent on `GameManager.onPlayerRejoinedServer`.
- **Unity Transport is embedded and patched** (`Packages/com.unity.transport`, `[CdP patch]` in `UDPNetworkInterface`):
  on Windows each ICMP "port unreachable" (a peer whose game died) failed a UDP receive request whose buffer was never
  released, so the host went deaf within seconds and every client dropped. Proof: PlayMode `UdpDeadPeerTests`. Re-apply
  the patch when upgrading the package (or drop it once Unity fixes it upstream).
- `GameSnapshotBuilder.FromLiveState` runs synchronously before any `await` (NV tearing moves goldens). A moved `[Category("GoldenMaster")]` = real behaviour change: find the cause, never re-bless.

## UI Toolkit gotchas

- Tablet = screen-space UITK → RenderTexture → uGUI `RawImage`. RT panels need `PanelInputConfiguration` on the EventSystem; presenters **clone** `PanelSettings`, never mutate the shared asset.
- Screen-space overlay over an RT tablet loses input at equal `sortingOrder` → raise the overlay's sorting order.
- State-screen roots: `pickingMode = Ignore`; block the world with a scrim child (mapping uGUI `blocksRaycasts` to root `Position` blocks the whole screen).
- USS transitions don't fire on elements recreated each rebuild → `experimental.animation.Start`.
- A `Label` used as a cell keeps `.unity-label` margin (~3px "box in a box") → `margin: 0`. Diagnose via resolved styles, not USS theory.
- Faction colours come from `FactionDatabase` SO, never new USS tokens. UI visuals are placeholder and design-owned: don't invent palettes.

## 3D interaction

- Board objects are driven entirely through `IPointer*` (no `OnMouse*`): cursor mode = `InputSystemUIInputModule` + `PhysicsRaycaster` on CameraBrain; embodied mode = `ReticleInteractor`. Mutually exclusive.
- With a `PhysicsRaycaster` present, `IsPointerOverGameObject()` is true over 3D objects → useless as an "over UI" test.
- Animating the scale of a non-convex `MeshCollider` re-cooks it every frame → raycast dropouts. Put the collider on an unscaled parent or use a primitive.

## Code organization

| Adding… | Where |
|---|---|
| Game phase | `GameLogic/GameStates/<Name>State.cs` |
| Power | `Characters/Powers/P<Name>.cs` (prefix `P`) + `XDecision` in Domain |
| Role | `RoleID` entry + `RoleDataObject` SO under `Assets/ScriptableObjects/` |
| Victory condition | `Characters/WinningConditions/` |
| Tunable constant | `GameValues.cs` |
| URP feature | `Rendering/` (`Game.Rendering` asmdef, never `Game`) |
| Spike / harness scene | `Assets/Scenes/Spikes/` |

- `Game` never references `Game.Editor` / `Game.Rendering` (breaks player build only in CI). Editor-only packages never referenced from runtime asmdefs.
- `UNITY_INCLUDE_TESTS` ≠ `UNITY_EDITOR`; expose internals via `InternalsVisibleTo`, never by widening to `public`.
- ScriptableObjects are read-only at runtime (Editor persists mutations to disk) → `Instantiate(so)` for mutable copies.
- Domain reload may be disabled: mutable statics reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- Legacy scenes `(old)MenuScene`, `GameScene_backup`: not loaded, don't extend.

## Testing

- Start in EditMode (Domain POCO + NSubstitute). PlayMode only for NGO/frame loop. Bug with a subtle root cause → red-first regression test.
- **New `.cs` files (tests especially): create via Unity** (`unity command create_script`). A file written from disk can sit in the AssetDatabase yet be excluded from compilation: 0 errors, tests in no suite.
- 2-NM fixture (`MultiClientGameFixture`): network tests always run on **UTP loopback**, never Steam. `IsOwner` is false on client replicas, so drive owner assertions from the host. `ClientCm.GetCharacter` returns the **host** object; the real client replica is `ClientNm.SpawnManager.SpawnedObjects[netId].GetComponent<Character>()`. The real prod `NetworkBehaviour` can be spawned in both NMs via `BuildExtraNetworkPrefabs`.
- `TestStaticReset.ResetAll` is opt-in, not automatic (auto-reset broke leak-dependent tests; resetting only at Desingleton teardown gives a false green).
- Never `WaitForSeconds` / real-time waits in tests. PlayMode flake: port 7777 bind.
- CI test runner is off (`unity-tests.yml` `if: false`) → run tests locally before a PR.
- Claude never enters Play mode or builds a player: verify via tests + console; playtests are Poyo's.

## Gameplay truths (design-owned, don't "fix")

- Wake-up is sequential (`awakeningOrder`), so heal/corrupt never race; there is no victory tie-break.
- Mage auto-skip after its 2 powers is by design.
- In-game skip = sleep only; the real vote-skip path is dead code (deferred, design-owned).
