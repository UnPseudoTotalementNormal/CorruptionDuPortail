# Refactor Architecture — Despaghettification (decoupling pass)

Branch: `refactor-despaghetti` (from `dev-refactor`, after the Epic 5 squash-merge `28639d6`). Never `Dev` directly; merge to `Dev` once, at the very end. Companion of `refactor-architecture-poco.md` and `refactor-architecture-desingleton.md` — same NFRs, same safety net.

Driver: Poyo's goal restated (2026-06-11) — *de-spaghettify the whole game so it is clean, simple to modify, and maintainable with real unit tests*, across the most important systems, ideally everything. The POCO + de-singleton work was **enabling scaffolding**, not the decoupling itself.

> **Status: VALIDATED — spec arrêtée (2026-06-11).** The **three-lane / one-surviving-static / two-guards** architecture below is the decided shape. Story 6.1 (lane A worked example on `LightManager`) is **delivered**. Execution source of truth: `planning-artifacts/epics.md` (Epics 6–12 / D0–D6) + `implementation-artifacts/sprint-status.yaml`.

---

## 1. What "spaghetti" means here (measured, not guessed)

Reconnaissance on `Assets/Scripts` (2026-06-11):

- **24 static singletons** (`static instance`/`Instance`), **29 `NetworkBehaviour`s**.
- **Service Locator everywhere**: a consumer grabs `GameManager.instance` / `CharacterManager.instance` (now `For(nm)`) to reach what it needs. The dependency is **implicit** — nothing in a class's signature says what it actually needs.
- **Two God Objects**, by caller fan-in (number of non-test files referencing the type):

| Type | Fan-in | LOC | Role today |
|---|---|---|---|
| **GameManager** | 72 | 555 | game loop + state dict + RPC dispatch + **pass-through to characterManager/gameInfoRevealer/chainingManager/bars** + day count |
| **CharacterManager** | 37 | 509 | character registry/query + spawn + `GetSafeRpcTarget`/bot flow + reveal helpers |
| ChatManager | 17 | 216 | NetworkBehaviour singleton |
| SelectionFlowService | 16 | 253 | UI selection (plain Mono singleton) |
| RoleTargetSystem | 16 | — | targeting |
| BoardManager | 13 | 257 | board/despawn |

- Largest systems by file count: `Characters/` (52), `UI/` (48), `Board/` (33), `GameLogic/` (22), `Domain/` (15, the clean POCO core already built).

**The single biggest finding** — what callers actually use on `GameManager`:

| Member used via `GameManager.instance`/`For()` | Hits |
|---|---|
| `.characterManager` | **78** |
| `.gameInfoRevealer` | **31** |
| `.onGameStarted` | 10 |
| `.GetGameStates` / `.GetGameState` / `.GetGameStateIndex` | ~13 |
| `.currentGameStateIndex` | 7 |
| `.charactersBar` / `.powersBar` | 4 |
| `.currentDay` / `.hasGameStarted` | 3 |

→ **GameManager is mostly a locator hub.** ~109 of its uses are just "give me the *other* manager." Inject those directly and the God Object's fan-in collapses to its real surface: the game-loop/state machine.

**The second structural fact** — a large consumer population is **NGO-spawned**: `Character`, the ~16 `P*` powers, `LobbyPlayerInfoHolder`, board-info UIs all have `OnNetworkSpawn`. On a client, **NGO instantiates these prefabs — no project code calls their creation**, so creator-push injection cannot reach the client-side replicas. Any honest plan needs a dedicated lane for them (lane C below); pretending pure push-DI covers them is how this refactor would stall mid-Epic-7.

## 2. Target architecture — three lanes, one surviving static, two guards

The end state: each system **declares** its dependencies and depends on **narrow interfaces**, not on whole managers or a global lookup. No DI framework. The injection *mechanism* is chosen by **how the object is created** — decided once per type:

| Lane | Who | Mechanism | Failure if unwired |
|---|---|---|---|
| **A — scene/prefab-placed** | LightManager, RoomFog, cameras, timers, scene UI | `[SerializeField]` (concrete type), wired in scene/prefab | `Assert` at init + **SceneWiringGuard** (CI) |
| **B — created by our code** | powers at their spawn site, GameStates, StateUI | `Initialize(deps)` / property-push by the creator | `Assert` inside `Initialize` |
| **C — NGO-spawned (client replicas)** | `Character`, `P*` powers, `LobbyPlayerInfoHolder` | `OnNetworkSpawn` resolves once from the **CompositionRoot** | `Assert` in `OnNetworkSpawn` |

Plus:

1. **One surviving project static: `CompositionRoot`** (§4). 24 singletons collapse to 1, confined to lane C glue, whitelisted explicitly by the guard. Everything else is injected.
2. **Two permanent guards** (§5): `DiSeamNoLocatorGuardTests` (no locator in migrated types — exists, story 6.1) and `SceneWiringGuardTests` (every injected `[SerializeField]` actually wired — story 6.2).
3. **Narrow role interfaces are the real decoupling** — the lanes are just plumbing. Split each God Object's surface into intent-named interfaces a caller depends on à la carte: `IGameLoop`, `IGameStateQuery`, `ICharacterQuery`, `ICharacterCommand`, `IRevealService` (more per system: `IChatService`, `IBoardService`, …). A class implementing several is fine; **callers see only the slice they use**.
4. **Thin adapters, fat POCOs.** `MonoBehaviour`/`NetworkBehaviour` shrink to adapters: lifecycle, RPC plumbing (`GetSafeRpcTarget`, `NetworkVariable`), Unity glue. Decision logic moves into `Domain`/POCOs with EditMode tests — continuing the Wave 1–4 pattern.
5. **Existing `OnValueChanged` subscriptions stay.** The observer pattern is already decoupled — the spaghetti was `GameManager.instance` used to *find* the source. Inject the source; the subscription code does not move.

### Why not the alternatives (recorded decisions)

- **VContainer / Zenject:** reflection-driven resolution changes init order/timing — a direct risk to "behaviour identical"; and the hard problem here (injecting NGO-spawned replicas) is exactly what frameworks don't solve without their own network extensions + massive scene/prefab churn. Solo-dev cost > gain. **No.**
- **ScriptableObject service channels:** moves the binding into assets — worse greppability than the scene, same null risk, extra indirection; the eventing need is already covered by `NetworkVariable.OnValueChanged`. **No.**
- **Code-push everything from the root (zero `[SerializeField]`):** buys greppability but re-invents half a framework + strict init-order discipline (root must inject before every `Start`). With SceneWiringGuard, lane A is simpler, Unity-native, and equally safe. **No.**
- **Blanket `[SerializeField]` on all 50+ consumers:** physically impossible — **a prefab cannot serialize a reference to a scene object**, and nobody instantiates client replicas. Lanes B/C are mandatory anyway; the three-lane split is not a style choice.
- **Why lane A is not "more spaghetti than the singleton":** the singleton hides the dependency (nothing in the class says it needs CharacterManager; the graph is reconstructed by grepping 78 hub-hops). An injected field **declares** it — `grep "ICharacterQuery"` lists every consumer in one command. Only the *binding* lives in the scene, and the two guards + scripted MCP wiring make that binding mechanically verified, not opaque.

## 3. Injection seam convention — the three lanes

Established by story 6.1 (lane A proven on `LightManager`), completed by 6.2/6.3. Every later despaghetti story (Epic 7+) cites this section instead of re-deriving the wiring. The rule: a migrated consumer **declares** its dependency and **holds no static lookup** afterward.

### Lane A — scene/prefab-placed → `[SerializeField]`

A `MonoBehaviour`/`NetworkBehaviour` that lives in a scene or on a prefab declares `[SerializeField] private GameManager gameManager;` (etc.) and is wired at its **composition root** — the GameScene object, or the prefab asset for prefab-placed types. The scene/prefab *is* the composition root: the place the object graph is assembled.

- **Concrete type in the field** — Unity does not serialize interface references. The consumer may narrow internally (`private IGameStateQuery Query => gameManager;`) so its *code* depends on the slice only.
- **Serialized-field safety (silent-breakage rule):** Unity serializes by **field name**. **Append** the new injected field; never rename / reorder / retype existing serialized fields — that orphans their already-wired references silently. A new `[SerializeField]` field is **null in every existing instance until explicitly wired**. Enumerate every instance (`find_gameobjects`) and wire each via MCP (scene: `manage_components`; prefab: `manage_prefabs`), then **verify by reading the reference back**. If a rename is ever unavoidable: `[FormerlySerializedAs("old")]`.
- Covered by **both guards**: no-locator (the type) + scene-wiring (the binding).

### Lane B — created by our code → `Initialize(deps)` / property-push

An object our code instantiates is injected by its creator. This pattern **already exists** — the convention codifies it:
- `GameManager.SetupGameStates()` clones each `GameState` and pushes `clonedGameState.gameManager = this;` — composition-root push injection.
- `StateUI.SetupStateUI(gameManager, this)` — explicit `Initialize`-style method injection one level down.
- Epic 7 applies the same to powers at their server-side spawn site: `CharacterManager` (their creator) injects what they need.

**Lane B is the testability lane:** pass **interfaces** here (`Initialize(ICharacterQuery query, …)`) — an EditMode test injects a fake trivially. Rule of thumb: logic worth unit-testing takes its deps through lane B interfaces; a pure presentation leaf may hold a lane A concrete field (verify-don't-force).

### Lane C — NGO-spawned → `OnNetworkSpawn` glue via the CompositionRoot

Client-side, NGO instantiates the replica — push injection cannot reach it. The consumer resolves **once**, in `OnNetworkSpawn`, from the **one allowed static**:

```csharp
public override void OnNetworkSpawn()
{
    var services = CompositionRoot.For(NetworkManager); // the ONE allowed static lookup
    _characterQuery = services.CharacterQuery;
    // ... rest of the class uses the fields, never the root
}
```

Rules:
- Resolution happens **only inside `OnNetworkSpawn`** (guard-checked, §5); resolved refs are stored in fields; the rest of the class reads the fields.
- The root must be resolvable **before any consumer spawns**: it is scene-placed and registers per-`NetworkManager` at `Awake` — never spawn-registered (cross-object `OnNetworkSpawn` order is not guaranteed).
- Server-side, the creator may *also* push (lane B) — lane C is the floor that guarantees client replicas are wired.

### (d) NFR5 relocation rule

When a migrated consumer holds network-authority code — `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` / `RpcParams` — that code is **moved verbatim** into the network adapter, **never edited**. It is relocated, not rewritten. (`LightManager` had none; this rule binds the network-sensitive consumers, gated by the multi-client fixture.)

### (e) No static lookup remains — fail loud, never fall back

After migration the consumer contains **no** `GameManager.instance` / `CharacterManager.instance` / `For(nm)` call (lane C's root access excepted, in `OnNetworkSpawn` only). A missed wiring must fail **loud and early**: `Assert.IsNotNull(dep, "... not wired ...")` in `Awake`/`Start`/`Initialize`/`OnNetworkSpawn`, with **no locator fallback** — a fallback re-introduces the locator and defeats the migration. DI in Unity moves the failure mode from "locator always resolves" to "reference might be unwired"; the guard-rail is a deterministic init-time assert + the two CI guards, not a mid-game NRE.

## 4. The one surviving static — `CompositionRoot`

- **Scene-placed** (GameScene). Its own references to the scene managers are lane A `[SerializeField]` fields — so the root itself is covered by SceneWiringGuard.
- **Typed accessors**, no `Dictionary<Type, object>` service bag: `services.CharacterQuery`, `services.GameLoop`, … The compiler, not a string/type key, is the registry.
- **Per-`NetworkManager` aware:** `CompositionRoot.For(NetworkManager)` resolves the graph for that NM. Production has exactly one NM, so `For` returns the scene root. The `MultiClientGameFixture`'s second in-process NM resolves its own graph — implemented by **delegating to the per-NM registries already built in 5.0c/5.0d** (`GameManager.For(nm)`, `CharacterManager.For(nm)`) behind one typed surface, then absorbing them as the track kills the per-manager statics.
- **Endgame census:** 24 project statics → **1** (the root), plus engine-owned `NetworkManager.Singleton` (5.0a rule: prefer the inherited `NetworkBehaviour.NetworkManager`), plus any **recorded** verify-don't-force exceptions (candidate: `GameAudioManager` as a global audio façade — decided in Epic 10, recorded either way).

**Shipped shape (story 6.3, recorded per AC5).** `CompositionRoot.For(NetworkManager)` returns a lightweight **`CompositionRoot.Services` value resolver** (a `readonly struct`), **not** the MonoBehaviour. The resolver holds only the NM and delegates each typed accessor to the per-NM manager registries (`CharacterManager.For(nm)` / `GameManager.For(nm)`), so **a scene-placed root *instance* is not required for resolution** — the static answers for any NM those registries know. This is what lets the `MultiClientGameFixture`'s second in-process NM (which has no scene root of its own) resolve its own graph with **zero fixture changes** (proven by `CompositionRootResolutionTests`). The scene-placed `CompositionRoot` MonoBehaviour is the production **lane-A wiring carrier** (its `[SerializeField] gameManager` / `characterManager` are SceneWiringGuard-checked) and is Awake-registered per NM (`s_byNetworkManager`, value-scan `OnDestroy` unregister, `SubsystemRegistration` reset — mirroring `GameManager.For`). First slice = **concrete** accessors `Services.CharacterManager` / `Services.GameManager`; the narrow interfaces in the §3 lane C sketch (`CharacterQuery`, `GameLoop`, …) arrive with Epics 8/9 and the accessors narrow then. Guard coverage split: `CompositionRoot` is in `DiSeamMigratedConsumers.SceneWiredOnly` (guard #2 only — it legitimately calls `For(`), never in `All`; the migrated lane C consumer `PTruthChains` is in `All` (both guards). Guard #1 gained `GameManager.For(` / `CharacterManager.For(` to the forbidden set (the For() hub-hop blind spot) plus the "`CompositionRoot` only inside `OnNetworkSpawn`" lane C whitelist.

### 4a. `CharacterManager` static census — recorded leftovers (story 9.3)

After Epic 9 (9.1 `ICharacterQuery` / 9.2 `ICharacterCommand`), every **gameplay** read/command consumer resolves the CharacterManager via injection (lane A/B/C → `CompositionRoot`). `CharacterManager.For(nm)` stays as the **per-NM backbone** the root delegates to; its only direct callers are now the root + the test fixtures (9.3 rerouted the last gameplay one, `GameSnapshotBuilder`, onto `CompositionRoot.For(nm).CharacterQuery`). `CharacterManager.instance` is **narrowed to a recorded-callers-only façade** — no gameplay path reaches it; every survivor below is a verify-don't-force exception with a death date. The field stays `public` until 12.3 deletes it.

| Caller(s) | Member(s) | Why on the façade | Planned death |
|---|---|---|---|
| `ChatManager` | `GetLocalClientId`, `GetSafeRpcTarget` | replicated singleton. **Opposite axis:** 10.1 injected ChatManager INTO its consumers (§4b); it did NOT inject CharacterManager into ChatManager. `GetSafeRpcTarget` stays concrete per NFR5 regardless | **Epic 12.3** (final sweep — ChatManager as a CharacterManager consumer) |
| ~~`LobbyPlayerInfoHolder`~~ | ~~`GetSafeRpcTarget`~~ | **RESOLVED (story 10.4)** — rerouted onto a lane-C `characterManager` field resolved through the composition root in `OnNetworkSpawn` (null-tolerant; the GetSafeRpcTarget path is server-only). No longer on the façade. | ✅ done |
| `TargetUtils` (static class) | `GetCharacters`, `GetLocalClientId` | static utility, **no injection context** (verify-don't-force) | **Epic 10.5** (mono-statics census) / recorded |
| `PowerEffectDispatcher` (static) | `GetCharacter`, `AskForUpdateAllCharactersRpc`, `GetSafeRpcTarget` | static dispatcher + NFR5 internal, no injection context | **Epic 10.5 / 12** |
| `WOmniscienceHackedCharacter`, `WMarginalIsChainedWin`, `WChosenChainedAllAnomaly`, `WAnomalyCorruption` | `GetCharacter(s)` | `W*` winning-condition rules evaluated outside any Unity lifecycle — façade (recorded since 7.4) | **Epic 11** (POCO pass) / recorded |
| `DevIdentityController` | `SpawnSimulatedPlayer`, `SetPossessedIdentity`, `GetCharacters`, `GetLocalClientId` | debug-only F-key tool, not shipped-path | **Epic 10/12** |
| `RoomFog`, `PowersBar`, `AnonymeMessageButton`, `InfoTableSystem` | reads + `onLocalIdentityChanged` | **entangled** — also hold `GameManager.instance`; reroute both managers in one touch | **Epic 12** (both-managers batch) |
| `NoteRibbon`, `NoteChoosePanel`, `AwakeningRecapCorruption`, `SelectPanelPlayer`, `TooltipLinkParser`, `VoteStateUI`, `ChatWindow`, `CardPickerManager`, `MeIconCard` | reads | UI leaves (several already annotated "deferred to Epic 12") | **Epic 12.2** |
| `CharacterAwakenTimer`, `TakeDownThePortalTextTitle` | reads | **prefab-only** (lane A impossible — prefab can't ref a scene object) | **Epic 12.3** (prefab lane) |

Symmetry note: this mirrors 7.5's GameManager static kill, but D3 has an **explicitly allowed remainder** (UI + static utils + POCO win-rules) — D1's hub-hops were all mechanical so none survived. The `instance`/`For` Awake duplicate-guard semantics (same-NM destroyed, foreign-NM registry-only — 5.0c Design B) are **lifecycle, not locator**, and are untouched.

### 4b. `ChatManager` static census — recorded leftovers (story 10.1)

ChatManager (fan-in 17, replicated `NetworkBehaviour` singleton) is **injected as a concrete dependency** (D-NFR6 — its consumers use only the fire-and-forget send/notify surface, a presentation side-effect of power logic already POCO'd in Epic 4; no slice would earn a unit-test mock). It is **NOT de-singletonised** (no `ChatManager.For(nm)` per-NM registry — out of Epic 10's "inject by fan-in" scope): it stays one global, and the **`CompositionRoot` chat accessor is the one sanctioned indirection point**, serving `ChatManager.instance` (the root already legitimately calls `*.For(`, so a singleton-backed chat resolver belongs there — it is *not* a leftover). Every **power + PowerComponent** now reads an injected `chatManager` base field (lane C, resolved once in `OnNetworkSpawn` via the root), so 13 files stopped touching the global; guard #1 forbids `ChatManager.instance` in the registered set to lock it. `ChatManager.instance` is now a **recorded-callers-only façade** (`// recorded: dies in 12.3`); survivors:

| Caller(s) | Member(s) | Why on the façade | Planned death |
|---|---|---|---|
| `CompositionRoot` (the root) | serves `instance` via the `ChatManager` accessor | the ONE sanctioned locator — chat is not de-singletonised, so the root is the indirection point (in `SceneWiredOnly`, never source-scanned). Not a leftover; dies only if chat gets a per-NM registry | **per-NM-registry story / 12.3** |
| `PowerEffectDispatcher` (static) | `DiscoverChatRpc`, `AddMessageLocal` | static dispatcher, **no injection context** (same bucket as its already-recorded `CharacterManager.instance` row above) | **Epic 11.1 / 12** |
| `ChatPanel`, `ChatNotificationComponent`, `ChatWindow` | reads (events / `activeChatId` / `discoveredChatIds` / `GetChatWindow`) + `ChangeActiveChat` / `TrySendChatMessage` | UI leaves — presentation, no injection context yet | **Epic 12.2** |

The `SERVER_CLIENT_ID` **const** (read by `PCardsShuffling` / `PCReparentOnChain`) is a compile-time constant, not a singleton read — left as-is.

### 4c. `RoleTargetSystem` static census — recorded leftovers (story 10.2)

`RoleTargetSystem` (fan-in 16, namespace `RoleTarget`) is the same shape as ChatManager — a **non-de-singletonised** replicated singleton (naive `OnNetworkSpawn` duplicate-guard, no `RoleTargetSystem.For(nm)`), injected **concrete** (D-NFR6: the consumers fire targeting RPCs / one UI read; no slice earns a mock). 13 targeting powers consume it via the `Power.roleTargetSystem` base field (lane C) and `RobotBoardInfo` via a lane-C field resolved in its own `OnNetworkSpawn` (it was already a registered `All` consumer, so it migrated to keep the guard-#1 lock clean). `RoleTargetSystem.instance` → recorded-callers-only façade (`// recorded: dies in 12.3`); guard #1 forbids it in the registered set. Survivors:

| Caller(s) | Member(s) | Why on the façade | Planned death |
|---|---|---|---|
| `CompositionRoot` (the root) | serves `instance` via the `RoleTargetSystem` accessor | the ONE sanctioned locator — not de-singletonised, so the root is the indirection point (`SceneWiredOnly`, never source-scanned) | **per-NM-registry story / 12.3** |
| `PowerEffectDispatcher` (static) | `NewTargeting` (brick) | static dispatcher, **no injection context** (same bucket as its `CharacterManager.instance`/`ChatManager.instance`); the Epic 11.1 dispatcher→POCO pass would take the targeting dependency as a parameter from the already-injected calling power | **Epic 11.1 / 12** |

Premise correction (recorded): the 10.2 story expected `TargetUtils` to be the static-util RoleTargetSystem consumer. It is **not** — `TargetUtils` consumes `CharacterManager.instance` + `CompositionRoot.For(Singleton).GameInfoRevealer`, never `RoleTargetSystem`. Its `CharacterManager.instance` reads stay §4a-recorded (Epic 10.5). No `TargetUtils` edit in 10.2.

### 4d. `BoardManager` static census — recorded leftovers (story 10.3)

`BoardManager` (global namespace, 257 LOC, fan-in 13) is the same shape — a non-de-singletonised replicated singleton — injected **concrete** (D-NFR6). Two story premises were stale and corrected: it has **zero hub-hops of its own** (its `characterManager`/`gameInfoRevealer` are already lane-A `[SerializeField]`, 7.2/7.3), and the **despawn-authority NFR is moot here** (its cards are LOCAL `Instantiate`/`Destroy` objects, not `NetworkObject`s — no server-only `Despawn`). Every reachable consumer was migrated: the 7 game-loop **GameStates** via a new `GameState.boardManager` lane-B field pushed by `SetupGameStates` (from `CompositionRoot.For(nm).BoardManager`); **GameManager** through the root; **FocusManager** + **CardEffectManager** via lane-A `[SerializeField]` (MCP scene-wired + read-back verified); **GameInfoRevealer** via a lane-C `OnNetworkSpawn` resolve. `BoardManager.instance` → recorded-callers façade (`// recorded: dies in 12.3`); **guard #1 forbids it** in the registered set. Survivors:

| Caller(s) | Member(s) | Why on the façade | Planned death |
|---|---|---|---|
| `CompositionRoot` (the root) | serves `instance` via the `BoardManager` accessor | the ONE sanctioned locator — not de-singletonised (`SceneWiredOnly`, never source-scanned) | **per-NM-registry story / 12.3** |
| `SelectionFlowService` | `visibleCards` (×2) | POCO `sealed class`, **no Unity lifecycle** — genuinely un-wireable; takes the dep as a parameter from the calling state when POCO-ised | **Epic 11.2** (board logic → POCO) |
| `CardPickerManager` | `AddNewCard` (×2), `visibleCards` | UI leaf — kept the global (no new field, no unwired-ref risk) | **Epic 12.2** |

Recorded only because un-wireable (SelectionFlowService = POCO) or a deferred UI leaf (CardPickerManager) — neither is registered, so guard #1 does not scan them. `typeof(BoardManager)` is in `InjectedManagerTypes` (guard #2 verifies the FocusManager + CardEffectManager scene wires). Process note: the first pass recorded FocusManager/GameInfoRevealer/CardEffectManager to dodge scene-wiring; Poyo's **never-defer-wiring** rule corrected it — a deferred wireable consumer is a silent playtest NRE he cannot trace + a skip-decision forgotten by next session. The wiring was completed.

### 4e. Remaining replicated singletons — batch census (story 10.4)

The 5 leftover replicated singletons, each with a decision (AC1/AC5). All the same non-de-singletonised shape; the root serves each from its singleton.

| Singleton | Decision | How / why |
|---|---|---|
| `GameAudioManager` | **OPT-OUT** (recorded, stays whitelisted) | Global FMOD façade consumed from ~19 sites incl. **non-injectable contexts** (ChatManager Awake event handlers, static-ish power audio, `?.`-null-safe everywhere). Injecting it changes nothing about the audio architecture (FMOD-only rule holds) and would thread a dep through contexts with no lifecycle. Deliberately NOT in `ForbiddenLocators`; consumers keep the global. |
| `ChainingManager` | **MIGRATED + locked** | 3 chaining powers (`Power.chainingManager` base field, lane C) + `VoteState` (inherited `GameState.chainingManager`, lane-B push 7.4). Root accessor added. Guard #1 locks `ChainingManager.instance`. ChainingManager itself stays a mixed file (own `GameManager.For` hop) off the registry. |
| `StatesCanvas` | **MIGRATED + locked** | Sole consumer `GameState.OnStateCreated` → injected `GameState.statesCanvas` (lane-B push). Null-tolerant (only used when `stateUIPrefab != null`; minimal harnesses set none). Guard #1 locks `StatesCanvas.Instance`. |
| `MessageManager` | **MIGRATED + locked** | `SendMessagePanel` (lane C — `messageManager` resolved in `OnNetworkSpawn`, asserted: GameScene NB, used on the local client) rerouted off `MessageManager.instance.SendMessageRpc`. Root accessor added. Guard #1 locks `MessageManager.instance`. Recorded non-registered survivors (keep the global → Epic 12.2): `AwakeningRecapMessages` (StateUI prefab leaf — also still reads `GameManager.instance.currentDay`, an unrelated Epic-12 hop) + `AnonymousRevealedMessagesComponent` (UI leaf). `MessageManager` itself is in the `All` registry (8.3, `IGameLoop` lane-A) and uses the bare `instance` self-ref, so the lock does not bite it. |
| `LobbyPlayerInfoHolder` | **MIGRATED + locked** | 4 player-name powers rerouted onto a new `Power.lobbyPlayerInfoHolder` base field (lane C, null-tolerant): PBlessing / PTruthChains / PHighPriorityBounty / PCardsShuffling. `Character` rerouted onto a lane-C field (null-tolerant — `GetOwnerPseudo` already guards null). The holder **itself** dropped its own `CharacterManager.instance.GetSafeRpcTarget` hop onto a lane-C `characterManager` (null-tolerant — that path is server-only and fired in-line during its own `OnNetworkSpawn`, where the registry is populated; clears the §4a row below). Root accessor added; holder registered (NoLocatorOnly); guard #1 locks the qualified instance accessor. Recorded non-registered survivors (keep the global): `PlayerButtonObject` / `ConnectedPlayerPanel` / `ChatPanel` (UI → Epic 12.2), `UlongExtensions` (static ext, genuinely un-injectable → param when POCO'd, Epic 10.5), `CharacterManager.AddDebugPlayer` (debug). NFR5: `GetSafeRpcTarget` stays verbatim on the concrete CharacterManager. |

Per-target commits (10.4 Dev Notes). The 2 PENDING targets are their own passes; the story stays in-progress until they land.

## 5. The two permanent guards

1. **`DiSeamNoLocatorGuardTests`** (exists — story 6.1, `[Category("DiSeamGuard")]`, source scan). A migrated consumer must never reference `GameManager.instance` / `CharacterManager.instance` again. Extended by story 6.3: in migrated consumers, `CompositionRoot` may appear **only inside `OnNetworkSpawn`** (the lane C whitelist).
2. **`SceneWiringGuardTests`** (story 6.2, EditMode). Loads the composition roots (GameScene; prefab assets for prefab-placed consumers) and asserts every injected `[SerializeField]` dependency of every migrated consumer instance is **non-null**. This converts "forgot a drag-drop" from a silent runtime NRE into red CI — the piece that makes lane A scale to 50+ consumers.

Both guards consume **one shared curated migrated-consumers set** — each Epic 7+ story appends a type once and both guards pick it up. Runtime backstop: the init-time `Assert.IsNotNull` (§3e).

## 6. Principles / invariants (non-negotiable)

- **Behaviour-preserving, every commit.** The golden/differential masters (Waves 1–4), the wire-format guard (5.1), the leaf-POCO guard (5.2), and `MultiClientGameFixture` (5.0) are the net. Baseline **PM 145 / EM 155** (plus the guard suites this track adds). A golden that *moves* means hidden behaviour was disturbed — stop and investigate, never "re-bless".
- **Shippable at every commit (NFR7).** Strangler pattern: introduce interface + new wiring alongside the old static, migrate call sites in batches the compiler enumerates (delete the symbol → CS0117/CS0103 → reroute), then remove the old path. Never leave both a clean core and the God Object as permanent debt.
- **Real unit tests are a deliverable, not a side effect.** Each extracted POCO ships EditMode tests; each decoupled `NetworkBehaviour` becomes reachable by the multi-client fixture without booting the whole scene.
- **Verify, don't force.** A reference with no injection context (static utility, serializable value object, pure UI leaf bound to the local player) may legitimately stay on a façade — **record why**, don't contort the design.
- **Network-critical work gated by the fixture** + `# REVIEW-REQUIRED` gds-code-review. NFR5 code relocated verbatim, never edited.
- **Serialized-field safety** (§3 lane A) applies to every story in the track.

## 7. Strategy — strangler + interface extraction + injection

Per God Object / system, the repeatable recipe:

1. **Characterize** the public surface actually used (the §1 histogram is step 0 for GameManager). Group members into intent interfaces.
2. **Introduce the interface(s)**; the existing manager implements them (zero behaviour change).
3. **Wire injection** per the three-lane convention (§3) — lane chosen by creation mode, decided once per type.
4. **Migrate call sites in batches** by destructive deletion of the static accessor for that slice, letting the compiler list every site; reroute to the injected dependency. Gate each batch at the full-suite baseline.
5. **Remove** the dead static/locator path; append the type(s) to the shared guard set so the locator cannot creep back.

## 8. Epic breakdown (D0–D6 → Epics 6–12)

Story-level source of truth: `planning-artifacts/epics.md`. One line each:

| Epic | Name | Delivers |
|---|---|---|
| **6 (D0)** | Composition root, three-lane seam & the two guards | 6.1 lane A (done) · 6.2 SceneWiringGuard · 6.3 CompositionRoot + lane C — **blocks Epic 7** |
| **7 (D1)** | Dismantle GameManager-as-locator | inject CharacterManager (78), GameInfoRevealer (31), chaining/bars; remove the pass-through fields |
| **8 (D2)** | Narrow the GameManager game-loop surface | `IGameLoop` + `IGameStateQuery`; decide the parked 5.3/5.4 index ownership |
| **9 (D3)** | Split CharacterManager | `ICharacterQuery` / `ICharacterCommand`; NFR5 verbatim; fixture-gated |
| **10 (D4)** | Remaining singletons → injection | 8 replicated + Mono statics by fan-in; recorded opt-outs allowed |
| **11 (D5)** | Per-system logic → POCO + unit tests | Powers, Board, Chat, Focus/Tooltip; thin adapters |
| **12 (D6)** | UI layer + final sweep | triage 48 files (verify-don't-force), reroute the justified, kill remaining statics, whole-track DoD gate |

Order maximises value-per-risk: seam first (unblocks all), then the locator hub (biggest win), then the second God Object, then the remaining singletons, per-system logic, UI last.

### 8a. Decision — parked 5.3/5.4 `GameLoopMachine` index ownership (story 8.4, 2026-06-12)

**Decision: CLOSE as won't-do.** Stories 5.3 (complete `GameLoopMachine` index ownership) and 5.4 (remove the final index façade) were parked by Poyo (2026-06-11) as "pure-archi, highest-risk silent-desync, no gameplay gain; folds into Epic 8 IF it still earns its risk." Story 8.4 re-evaluated that condition once the D2 narrowing landed; the narrowing **lowered** the payoff, so the fold-in condition is not met.

Decision inputs (assembled at 8.4 dev time):
- **Ownership / writes:** `currentGameStateIndex` (the `NetworkVariable<int>`) is owned and written by `GameManager` ALONE — server-side, in `SwitchGameState` (+ the index-0 reset). No other writer.
- **Reads, post-D2:** external read consumers now see the slice — 3 via `IGameStateQuery.currentGameStateIndex` (LightManager, BoardCameraManager, PowerManager), 2 still via the locator (RoomFog, AnonymeMessageButton → Epic 9), 2 via an injected concrete `gameManager` (GameState.`IsStateActive` internal, GameSnapshotBuilder off a passed param). The transition **arithmetic already lives in the POCO** (`GameLoopMachine.Advance/Rewind`, story 2.11b, EditMode-tested).
- **What 5.3 would still buy:** only architectural purity — the POCO owning the canonical index while the NV mirrors it. Consumers already depend on `IGameStateQuery.currentGameStateIndex`, so they are indifferent to who owns the index; no consumer-facing change, no new testability beyond what the 2.11b POCO + the slice already provide.
- **Risk (unchanged, rated #1 of the whole refactor):** a synchronous mirror between machine-settle and the NV write. Any suspension point that creeps between them → host advances, remote client frozen, **no exception** (silent multiplayer desync). It also touches the load-bearing `OnEnd → NV write → OnStart` ordering pinned by the 2.11a sequence golden.

Calculus: 5.3 now buys purity only, costs re-opening the highest-risk silent-desync surface + a `REVIEW-REQUIRED` review + a parameterized multi-client trace suite, for zero gameplay gain. **Closed.**

**Consequence — the index façade is PERMANENT by design (recorded exception for the §10 DoD / story 12.3).** With 5.3/5.4 closed, `GameManager` keeps OWNING `currentGameStateIndex` (the `NetworkVariable`) and performing the `OnEnd → NV write → OnStart` sequencing, while `GameLoopMachine` (POCO, Domain) computes only the transition arithmetic. This split is **GameManager's legitimate network-adapter role**, NOT a strangler façade awaiting removal: the NV is NGO replication state that must live on a `NetworkBehaviour`, and the ordering is behaviour pinned by the 2.11a golden. The whole-track DoD ("no strangler façade remains", story 12.3) explicitly EXCLUDES this adapter — it is a designed, permanent boundary, not parked debt. The 5.2 `LeafPocoNoFacadeGuard` is unaffected: it covers the Wave 1–3 leaf cores (VictoryEvaluator/VoteTally/ChainingResolver/RoleDistributor/PowerResolver), and `GameLoopMachine` already satisfies its spirit (a plain instantiable POCO with no static accessor / no static mutable state — the NV lives in `GameManager`, not the POCO).

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| A reroute silently changes which instance a call hits (multi-NM) | Production has one NM → injecting the scene instance is identical; `CompositionRoot.For(nm)` + the fixture prove the replicated case |
| Init/lifecycle order: an injected ref read before it is wired | Root registers at `Awake`; lane C resolves at `OnNetworkSpawn`; init-time asserts; boot smoke-test per epic |
| Forgotten scene/prefab wiring (lane A at scale) | SceneWiringGuard in CI + MCP-scripted wiring with read-back verification + init assert |
| Lane C resolution before the root exists | Root is scene-placed, Awake-registered, never spawn-registered; fixture covers the two-NM case |
| Golden moves during a "behaviour-preserving" reroute | That is the net working — stop, the reroute disturbed hidden behaviour |
| Scope creep into redesigning mechanics | Poyo owns design; behaviour-preserving only |
| Half-done God Object = worse than before | Strangler discipline: each epic ends with the old path *removed* + guard appended, never parked half-migrated |
| UI churn for little gain | UI is Epic 12, last, partially opt-out by verify-don't-force (recorded) |

## 10. Definition of done (whole effort)

No God Object remains a grab-bag; consumers depend on narrow injected interfaces via the three lanes, not `instance`/`For(nm)` locators; **the only surviving project static is `CompositionRoot`** (plus recorded verify-don't-force exceptions); decision logic lives in tested POCOs; **both guards** green and covering every migrated type; full EditMode + PlayMode suite green at baseline; boot smoke-test green; merged to `Dev` once, at the end. **Recorded permanent exception (§8a, story 8.4):** `GameManager` owning the `currentGameStateIndex` `NetworkVariable` + the `OnEnd → NV write → OnStart` sequencing is a designed network-adapter boundary (replication state must live on a `NetworkBehaviour`), NOT a strangler façade — story 12.3's "no façade remains" check excludes it.

---

**Source of truth for execution:** this doc + `planning-artifacts/epics.md` + `sprint-status.yaml`. Relates to [[project-despaghetti-plan]], [[project-poco-loop-progress]], [[project-desingleton-plan]], [[project-refactor-branch]], [[feedback-refactor-workflow]], [[feedback-serialized-field-rewiring]].
