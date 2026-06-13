# Refactor Architecture — Manager de-singletonisation (per-NetworkManager resolution)

Status: **proposed** (inventory validated, no code written yet).
Branch: `epic5-network`. Driver: GitHub issue #50 — Story 5.0 (host + real in-process client fixture) is blocked because the game stack is built on process-global singletons. Companion of `refactor-architecture-poco.md` (same NFRs apply, NFR1/NFR5/NFR6/NFR7 in particular).

## 1. Problem (grounded in current code)

A real second in-process client makes NGO replicate the game's `NetworkObject`s a second time in the same process. Every replicated manager then runs its `Awake()` singleton guard against the **host's** static state:

- `GameManager.cs:51-60` — `Awake()` does `if (instance != null && instance != this) { Destroy(gameObject); return; }` then `instance = this`. The client-2 replica is **destroyed at Awake**, which breaks NGO replication for that client (worse than clobbering). `OnNetworkDespawn` (`GameManager.cs:78-91`) nulls the static.
- `CharacterManager.cs:115-123` — identical destroy-duplicate guard, identical clobber/destroy failure mode.
- `NetworkManager.Singleton` — 87 usages / 22 files. Three distinct populations (see §3).
- `NetworkAction.cs` (Plugins, POCO) — **36** `NetworkManager.Singleton` usages. Every `Register()`/`Invoke()`/`OnReceiveMessage()` goes through the Singleton's `CustomMessagingManager`. Two in-process clients ⇒ both register the **same global messageID** (e.g. `"onGameStarted"`, created in a `GameManager` *field initializer*) on the **same** messaging manager: `RegisterNamedMessageHandler` replaces the previous handler ⇒ silent cross-wiring between the two clients' listener lists.
- `NetworkBehaviourReferenceWrapper.cs:36` — wire-deserialized struct whose `TryGet` resolves through `NetworkManager.Singleton.SpawnManager`. A remote in-process client would resolve references against the **host's** spawn table.
- POCO power objects (e.g. `PersonalBeaconObject.cs:23-30`) read `CharacterManager.instance` / `NetworkManager.Singleton` in their constructor, but always receive a bound `Power` (`_ownerPower`) — a per-NM resolution path already exists.

### Blast radius (measured 2026-06-11)

| Singleton | Usages | Files | Notes |
|---|---|---|---|
| `GameManager.instance` | 161 | 67 | ~66 usages / 28 files are UI/presentation MonoBehaviours; ~70 in Powers/Characters/Board (NetworkBehaviours); ~22 in GameLogic (GameStates already hold an injected `gameManager`); 2 in tests |
| `CharacterManager.instance` | 78 | 39 | majority in powers/`Character` (NetworkBehaviours) + UI; carries `GetSafeRpcTarget` / `IsLocalOrSimulated` (the bot flow) |
| `NetworkManager.Singleton` | 87 | 22 | 36 in `NetworkAction.cs`; ~25 in NetworkBehaviours (mechanical); ~10 in GameStates; ~16 legitimate pre-game bootstrap (MainMenu, LobbySelectionPanel, LoginMenu, FacepunchTransport, NetworkTransportDetector) |

### The 22-singleton context (scope fence)

The project has 22 `static instance` singletons. Only **NetworkBehaviour** singletons are replicated by a second client and can clobber/destroy. Besides the two targets: `ChainingManager`, `RoleTargetSystem`, `BoardManager`, `StatesCanvas`, `MessageManager`, `ChatManager`, `LobbyPlayerInfoHolder`, `GameAudioManager` (8 more). Mono/POCO singletons (`PowerManager`, `TooltipManager`, `FocusManager`, `InputManager`, etc.) are scene-local, instantiated once per process, and harmless to the fixture.

**Decision:** this refactor de-singletonises the 3 targets only. The 8 remaining NetworkBehaviour singletons follow the *same established pattern* later, **on demand**, when the 5.0 fixture proves one of them blocking (the fixture can spawn only the prefabs it exercises, so most of them never instantiate twice in tests). Migrating all 22 now is churn with no test-coverage payoff (NFR7: shippable at every commit, no big-bang).

## 2. Target architecture — per-NetworkManager registry + primary façade

### Core pattern (applied identically to `GameManager` then `CharacterManager`)

```csharp
// Registry keyed by the owning NetworkManager (populated at spawn, cleaned at despawn/destroy)
private static readonly Dictionary<NetworkManager, GameManager> s_byNetworkManager = new();

public static GameManager For(NetworkManager networkManager) =>
    networkManager != null && s_byNetworkManager.TryGetValue(networkManager, out var gm) ? gm : null;

// `instance` stays, as a *façade* = the instance owned by NetworkManager.Singleton (the "primary").
// In production there is exactly one NetworkManager per process, so façade semantics are
// observationally identical to today (claimed in Awake, nulled at despawn).
public static GameManager instance { get; private set; }
```

`Awake()` guard becomes NM-aware:
- duplicate **within the same NetworkManager context** → destroy (today's semantics, unchanged);
- replica belonging to a **different NetworkManager** → register in `s_byNetworkManager` only; never touches the `instance` static, never destroyed.

### Resolution paths per consumer kind

| Consumer | Resolution | Why |
|---|---|---|
| `NetworkBehaviour` (powers, `Character`, managers themselves) | `GameManager.For(NetworkManager)` / inherited `this.NetworkManager` instead of `NetworkManager.Singleton` | `NetworkBehaviour.NetworkManager` falls back to `Singleton` when unowned ⇒ strictly identical in production |
| `GameState` (ScriptableObject clones) | already-injected `gameManager` field (`GameManager.cs:117`), `gameManager.NetworkManager`, `gameManager.characterManager` | injection already exists; zero new plumbing |
| POCO power objects (`PersonalBeaconObject`, …) | derive from the bound `Power` passed to the ctor (`_ownerPower.NetworkManager`, `For(...)`) | binding already exists |
| `NetworkAction` | injected `NetworkManager` field; the existing `NetworkBehaviour`-bound ctor derives it (`_networkBehaviour.NetworkManager`); the unbound ctor keeps the `Singleton` fallback | preserves current behavior for unbound global actions |
| `NetworkBehaviourReferenceWrapper` | `TryGet<T>(out T, NetworkManager nm = null)` — explicit NM when the call site has context, `Singleton` fallback otherwise | wire-struct cannot carry the NM; the *call site* has it |
| UI / presentation MonoBehaviours (~28 files) | **stay on the `instance` façade** | UI is inherently bound to the local (primary) player; the fixture never exercises it; migrating it is a later, dedicated pass |
| Pre-game bootstrap (MainMenu, lobby, transport) | **keep `NetworkManager.Singleton`** | runs before any game NM duality can exist; legitimately global |

### `GetSafeRpcTarget` / `IsLocalOrSimulated` (NFR5 — verbatim)

Both remain **instance methods of `CharacterManager`**, body untouched. `GetSafeRpcTarget` already routes through the instance-level `NetworkManager.RpcTarget` (`CharacterManager.cs:45-52`). Only the *access path* of callers changes (`CharacterManager.instance.GetSafeRpcTarget(...)` → resolved instance). The `clientId >= 100` interception semantics are byte-identical; the host-intercept bot flow is regression-gated by the existing suite.

## 3. Behavior-preservation strategy (non-negotiable)

Production runs exactly **one** `NetworkManager` per process. Therefore:

- `For(NetworkManager.Singleton)` ≡ `instance` ≡ today's behavior — every migration is observationally a no-op in production;
- the façade `instance` keeps today's lifecycle (claimed in `Awake`, nulled at despawn) for the primary NM;
- no golden/differential test may change (NFR1); the 2.11a sequence golden and the Waves 1–3 suites are the oracle;
- every slice is gated: `refresh_unity` → `read_console` 0 errors → full EditMode + PlayMode suite green → one conventional commit;
- the **boot smoke-test** (PlayMode full game without exception) guards init/lifecycle-order regressions that singleton claims may have hidden.

### Open implementation questions (resolved by probe, not by assumption)

1. **Is `NetworkObject.NetworkManager` resolvable in `Awake()` of a replicated object?** NGO sets `NetworkManagerOwner` at instantiation for dynamically spawned objects, but this must be **proven by a probe test** before the NM-aware Awake guard relies on it. Fallback if false: keep Awake's same-NM duplicate destroy on `Singleton`-owned objects only, and defer the registry claim + foreign-replica detection to `OnNetworkSpawn` (where `NetworkManager` is always valid).
2. **`NetworkAction` registration timing.** `GameManager.onGameStarted` / `onNewDayPassed` are field initializers (run at Awake-time, registered on `Singleton`). Binding them to the owning NM may require moving creation to `OnNetworkSpawn` — a timing change. The slice must characterize current registration timing first (when does the first `Invoke`/listener attach happen relative to spawn?) and preserve it; if timing cannot be preserved, the unbound-with-fallback form stays and only the *fixture* uses NM-bound actions.

## 4. Migration slices (one manager at a time, smallest first)

Ordered least- → most-coupled. Each slice = one story (epics.md, Epic 5, stories 5.0a–5.0e), several small gated commits.

1. **5.0a — mechanical `NetworkManager` resolution (~14 sites, zero observable change).** Inside NetworkBehaviours: `NetworkManager.Singleton` → inherited `NetworkManager` (`GameManager.cs:400`, `CharacterManager.cs:342,358`, `PVisionOfTheImpossible.cs:109`). Inside GameStates: → `gameManager.NetworkManager` (`AwakeningState.cs:184,325`, `TakeDownThePortalState.cs:123,171,241,258`, `LobbyState.cs:53,58,62`). `NetworkBehaviourReferenceWrapper.TryGet` gains the optional NM parameter (fallback `Singleton`). Out of scope: bootstrap UI, transports, dev scripts (`IsServerTest`, `NetworkActionTester`, `DevIdentityController`), Mono `PowerManager` (scene-local, not replicated).
2. **5.0b — `NetworkAction` NM injection (plugin, isolated).** Private `NetworkManager` field resolved once; bound ctor derives it from `_networkBehaviour.NetworkManager`; unbound ctor falls back to `Singleton` (current behavior). All 36 internal `Singleton` reads route through the field. Then, *as a separate commit with its own characterization*, evaluate binding `GameManager`'s two global actions (open question §3.2).
3. **5.0c — `CharacterManager` registry + façade (78 usages / 39 files).** Probe test for §3.1 first. Registry + NM-aware Awake guard + façade. Migrate call sites in batches of ~10 files: powers → `Character` → POCO power objects (via `_ownerPower`) → GameLogic. UI call sites stay on the façade. `GetSafeRpcTarget` / `IsLocalOrSimulated` bodies untouched.
4. **5.0d — `GameManager` registry + façade (161 usages / 67 files, the monster).** Same pattern. Migrate gameplay call sites only (~95: Powers/Characters/Board/GameLogic; GameStates use the injected `gameManager` they already have). The ~66 UI usages stay on the façade (documented follow-up: dedicated UI-injection pass, out of this epic).
5. **5.0e — coexistence gate (pre-fixture).** Minimal PlayMode probe: two `NetworkManager`s in-process, spawn `GameManager` + `CharacterManager` replicas for the second client, assert (a) no replica destroyed at Awake, (b) `instance` façades still point at the primary's objects, (c) `For(nm2)` resolves the second client's instances, (d) a `NetworkAction` bound to nm2 does not hijack the primary's handler. Green gate ⇒ Story 5.0 (full fixture) is unblocked.

## 5. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `NetworkManagerOwner` not set at Awake of replicated objects → NM-aware guard impossible at Awake | Probe test first (5.0c AC #1); fallback design in §3.1 |
| `NetworkAction` registration-timing change breaks listeners attached pre-spawn | Characterize timing before touching; keep fallback ctor; bind only where timing is provably identical |
| Bot flow (`clientId >= 100`) silently broken by a resolution-path change | NFR5: method bodies verbatim; existing bot-flow tests + boot smoke-test in every slice gate |
| Hidden init-order dependency on `instance` being non-null between Awake and spawn | Façade keeps today's Awake claim for the primary NM — that window is unchanged |
| The 8 other NetworkBehaviour singletons clobber when the fixture spawns full scenes | Fixture spawns only the prefabs it exercises; extend the (then-established) pattern on demand |
| Suite green but multiplayer-only regression | That is precisely what 5.0e + 5.0 add; until then, slices are production-no-op by construction (§3) |

## 6. Definition of done

- The three targets resolvable per `NetworkManager` (`For(nm)`), `instance` reduced to a primary-NM façade with unchanged production semantics.
- 5.0e coexistence gate green: two in-process clients, no clobber, no destroy, no cross-wired `NetworkAction`.
- Full EditMode + PlayMode suite green at every commit; no golden changed; boot smoke-test green.
- `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` byte-identical (NFR5).
- Story 5.0 (real-client fixture) implementable without touching game singletons again — unblocking 5.3/5.4.
