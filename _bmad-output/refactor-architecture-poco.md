# Refactor Architecture — POCO core extraction (behavior-preserving)

> **Hard constraint:** game logic behavior must stay **identical**. This is a structural refactor, not a redesign. No mechanic changes. Every extraction is validated against characterization tests captured *before* the change.

> **Revision note (party-mode audit):** an earlier draft claimed `VictoryEvaluator` was an "already pure leaf". That was **wrong** — every `WinningCondition.CheckCondition()` pulls `GameManager.instance` and reads `NetworkVariable`s. The plan below is corrected: a snapshot + `WinningCondition` refonte is a **prerequisite of Wave 1**, not a later step. See §3a and §4.

## 1. Problem (grounded in current code)

The gameplay logic is fused to Unity / NGO, so it can only be exercised in slow PlayMode tests and is fragile to change:

- **Singletons as the only access path** — `GameManager.instance`, `PowerManager.instance`, `CharacterManager.instance`, `BoardManager.instance`. Nothing can be instantiated in a plain unit test.
- **Pure-looking logic trapped inside `NetworkBehaviour` / state `ScriptableObject`s, but actually coupled to the singleton graph.** Examples:
  - `GameManager.NextGameState()` (`GameManager.cs:152-176`) — the day-loop / state-advance arithmetic reads `currentGameStateIndex` (`NetworkVariable<int>`).
  - `VictoryConditionCheckState.OnStartStateServer()` (`VictoryConditionCheckState.cs:24-56`) — the win-team loop delegates to `WinningCondition.CheckCondition()`, and **each concrete condition pulls `GameManager.instance` + reads `NetworkVariable`s** (`WAnomalyCorruption.cs:21`, `WChosenChainedAllAnomaly.cs:21`, `WMarginalIsChainedWin.cs:16`, `WOmniscienceHackedCharacter.cs:17-36`).
- **Reflection-based RPC dispatch** (`GameManager.CallStateMethodRpc` / `CallMethodAfterRpc`, `GameManager.cs:304-376`) couples the state machine to the transport. Serializes type/method names as `FixedString64Bytes` — renaming a `GameState` class can break in-flight packets.
- **Non-deterministic beyond `Random`** — `CharacterManager` uses `UnityEngine.Random`, but there are **three further** non-determinism sources (see §3b): dictionary iteration order in `RoleAttributionState.cs:32,85`, frame-timed `Random` in `AwakeningState.cs:250` (called from `Update`), `Character` spawn-order driving `votesForPlayer` insertion in `VoteState.cs:162-166`.
- **Tests live in PlayMode** (`Tests/PlayMode/`: `GameManagerTests`, `PowerTests`, `VictoryConditionTests`, `RoleTests`, `ChainingManagerTests`…) where they should be fast EditMode tests. Only `Extensions/` logic is EditMode today. Several of these tests pass for the wrong reasons (e.g. `GameManagerTests.cs:40` adds `GameManager` without a `NetworkObject`, so it captures no network regression).

## 2. Target architecture — Humble Object + lite Ports & Adapters

Split each gameplay system into two halves:

```
┌─────────────────────────────────────────────┐
│  DOMAIN CORE  (POCO, no UnityEngine/Netcode) │   ← fast EditMode tests
│  - pure rules: state machine, role distrib,  │
│    power resolution, victory eval, vote tally│
│  - operates on snapshots (value objects)     │
│  - depends only on PORT interfaces           │
└───────────────▲─────────────────────────────┘
                │ called by / returns decisions to
┌───────────────┴─────────────────────────────┐
│  UNITY ADAPTER  (NetworkBehaviour, thin)     │
│  - owns NetworkVariables, RPCs, prefabs      │
│  - maps networked state → snapshot in        │
│  - applies core decision → NetworkVariable/  │
│    RPC out                                   │
│  - KEEPS GetSafeRpcTarget / IsLocalOrSimulated│
│    / strict server authority untouched       │
└──────────────────────────────────────────────┘
```

Humble Object is the right pattern here — not ECS (5–15 entities, no data/system decomposition payoff), not event-sourced (replay/serialization overhead with no benefit), not full MVC (the "view" is all of Unity, the "model" is NGO → three layers for nothing).

### Assembly boundary enforces purity — `CorruptionDuPortail.Domain`

Create a new asmdef `CorruptionDuPortail.Domain` with `noEngineReferences: true` and `references: []`. The compiler then *structurally* forbids `UnityEngine` / `Unity.Netcode` / FMOD / DOTween in the core — a CI `grep` is **not** an equivalent substitute (it misses transitive deps, `using` aliases, and partial classes). The existing single gameplay asmdef `Game` (`Assets/Scripts/Game.asmdef`, today `autoReferenced: true`) references `Domain`, never the reverse.

**What goes in `Domain` (and what does NOT):**

- **In Domain:** `WinningTeam` (enum), `CharacterSnapshot`, `GameSnapshot`, `IWinningCondition` (push-snapshot predicate), `IWinningConditionEvaluator`, and the POCO rules (`VictoryEvaluator`, `VoteTally`, `ChainingResolver`, `GameLoopMachine`, `RoleDistributor`, `PowerResolver`).
- **Stays in `Game`:** `WinningCondition` (it is `INetworkSerializable`, `WinningCondition.cs:11`), `Role` (it is `INetworkSerializable` + embeds FMOD `EventReference`, `Role.cs:23,37-38`), `Character`, `Power` (all `NetworkBehaviour`). These **implement** the Domain interfaces; they do not move.

### Domain core pieces (extraction targets)

| POCO class | Extracted from | Pure responsibility |
|---|---|---|
| `GameLoopMachine` | `GameManager.NextGameState/PreviousGameState/SwitchGameState` math | advance/rewind index over an ordered `StateDescriptor[]` with `isInGameLoop`, day-pass detection, first-loop detection. `ignoreGameLoop` becomes a **call parameter / internal state**, not a global mutable field (`GameManager.cs:40`). |
| `RoleDistributor` | `CharacterManager` role assignment + `UnityEngine.Random` | assign roles from settings + player list, **deterministic via `IRandomProvider`** AND a fixed, explicit ordering of the role pool (see §3b) |
| `VictoryEvaluator` | `VictoryConditionCheckState.cs:24-45` loop (NOT the conditions themselves) | `Evaluate(IReadOnlyList<CharacterSnapshot>) → Dictionary<WinningTeam, HashSet<ulong>>`; calls `IWinningCondition.CheckCondition(snapshot)` |
| `VoteTally` | `VoteState` / `VoteRecapState` | count votes → outcome |
| `ChainingResolver` | `ChainingManager` | accusation/chain resolution |
| `PowerResolver` | `Power` / `PowerManager` effect arithmetic only | returns `EffectDescriptor` value objects; **all** FMOD/Focus/RPC side-effects (`Power.StartUse/StopUse`, `Power.cs:120,183-196`) stay in the adapter |

### Domain model (value objects, immutable snapshots)

`CharacterSnapshot` — built from server truth, must be **lossless** for every field a rule reads:

```
CharacterSnapshot {
    ulong  ownerClientId;
    bool   isFake;                    // = ownerClientId.IsFakeClientId(), set AT SOURCE in the mapping (never recomputed in the POCO — the extension lives in an NGO assembly)
    bool   isCorrupted;               // from NetworkVariable<bool>
    bool   isChained;                 // from NetworkVariable<bool>
    FactionType factionType;          // from Role (replicated via Role.NetworkSerialize:97)
    ulong  hackedByOmniscienceTarget; // from POmniscience.hackedCharacterClientId — a PLAIN ulong on a NetworkBehaviour, NOT a NetworkVariable; read directly from the live Power instance (POmniscience.cs:14, set server-side in OnCardClickedRpc:41)
}
GameSnapshot { IReadOnlyList<CharacterSnapshot> characters; int day; int currentStateIndex; }
StateDescriptor { stateId; bool isInGameLoop; }   // replaces reading the SerializedDictionary at decision time
```

> **The `hackedCharacterClientId` trap:** it is not a `NetworkVariable`, so it never appears in `Role.NetworkSerialize`. The snapshot builder MUST read it off the live `POmniscience` instance in `character.role.powers`, never from a serialized `Role` copy — otherwise it is always `HACKED_CHARACTER_DEFAULT` and `WOmniscienceHackedCharacter` silently never wins.

### Ports (interfaces the core needs; Unity implements them)

- `IRandomProvider` — `Next(int max)`, seedable. Necessary but **not sufficient** for determinism (see §3b).
- `IGameEventBus` (or adapter-side manual dispatch) — `GameLoopMachine` must emit `onGameStarted` / `onNewDayPassed` equivalents currently held as `NetworkAction` (`GameManager.cs:46-47`) and consumed by `PowerManager.Start()` (`PowerManager.cs:38`). The core returns a result struct; the adapter fires the events. Do **not** let the core reference `NetworkAction`.
- `IGameClock` — only for the frame-timed `AwakeningState` randomness, which is explicitly **out of scope** for Wave 1 (see §3b/C).

The core never references `NetworkVariable`, `RpcParams`, `GetSafeRpcTarget`. Those stay 100% in the adapter.

## 3. Behavior-preservation strategy (non-negotiable)

### 3a. `WinningCondition` refonte — the prerequisite (was misidentified as "Wave 1 pure leaf")

`VictoryEvaluator` cannot be POCO until the conditions stop pulling the singleton. The migration is **strangler, shippable at every commit**, and is **server-only** (`CheckCondition()` has a single prod call site, `VictoryConditionCheckState.cs:35`, under `Assert.IsTrue(IsServer)`). Crucially, the wire serialization does **not** change: `Role.NetworkSerialize` (`Role.cs:92-135`) sends only the condition's `AssemblyQualifiedName` + `ownerClientId` (`WinningCondition.cs:19-22`); no concrete condition overrides `NetworkSerialize`.

1. Add `GameSnapshot` / `CharacterSnapshot` (new file). Nothing else changes; compiles.
2. Add a **synchronous** `GameSnapshotBuilder.FromLiveState(...)` server-side in `VictoryConditionCheckState` (iterates `role.powers` to find `POmniscience.hackedCharacterClientId`). Must run before any `await` — a NetworkVariable can change across a frame.
3. Add `virtual bool CheckCondition(GameSnapshot s) => CheckCondition();` on `WinningCondition` (default delegates to the old pull). Switch the state to call the snapshot overload; log on divergence.
4. Migrate concrete conditions **one commit each, easy → hard**: `WMarginalIsChainedWin` → `WAnomalyCorruption` → `WChosenChainedAllAnomaly` → `WOmniscienceHackedCharacter`.
5. Remove the compat delegation; `CheckCondition()` becomes abstract/removed.
6. Drop `using GameLogic;` from the conditions — the signal they are POCO. Promote `IWinningCondition` into `Domain`.

### 3b. Determinism — `IRandomProvider` is necessary but NOT sufficient

Three additional non-determinism sources must be pinned/quarantined, or golden masters are worthless:

- **A. Role-pool iteration order** — `RoleAttributionState.cs:32,85` indexes into `Dictionary.Keys.ToList()`. A seeded `Random` over a list whose order can drift (asset reload, `SerializedDictionary` order) is still non-deterministic. Fix: capture and freeze an explicit pool ordering before Wave 2.
- **B. Frame-timed randomness** — `AwakeningState.cs:250` calls `Random.Range` inside `StateUpdateServer()` → `Update()`. The count of calls before a transition depends on framerate. **Out of scope for Phase 0 / Wave 1**; needs an `IGameClock` + seed isolation. Leave in PlayMode, ungoldened, flagged.
- **C. Spawn-order-dependent insertion** — `VoteState.cs:162-166` builds `votesForPlayer` by iterating `GetCharacters()`. Snapshot construction order becomes the contract `VoteTally` must respect; pin it with a test.

### 3c. Test strategy (golden → losslessness → differential → cut)

Reuse the existing PlayMode `StartHost()` setup (`VictoryConditionTests`, `NetworkTestHelper`) — it is the only way to get live `NetworkVariable`s.

1. **Golden masters first, before touching any code.** ~23 boundary cases across the 4 conditions; each test **encodes the current verdict as an `Assert`** (it is both golden and regression), `[Category("GoldenMaster")]` (filter via `run_tests`). Capture current behavior as-is, including vacuously-true empty-list cases — do not "fix" them.
2. **Losslessness of the mapping** — one test per field a condition reads (7 fields → ≥7 tests). `GameSnapshotBuilder.FromLiveState()` is the single source of truth, specified by these tests. Include a dedicated `hackedCharacterClientId` test asserting it is read from the live `POmniscience`.
3. **Differential tests** — while both signatures coexist (step 3a.3): on the same live NGO state, `Assert.AreEqual(condition.CheckCondition(), condition.CheckCondition(snapshot))` across the full matrix. **One red differential = migration halts.**
4. **Cut** — once all differentials are green, remove the old signature; golden masters auto-retarget to the new one.

**Highest-risk conditions** (tightest coverage): `WOmniscienceHackedCharacter` (double lookup + non-NV field + 4-way conjunction for its only `true`), then `WMarginalIsChainedWin` (zero existing coverage, depends on `ownerClientId` propagation).

### 3d. General guards

1. **Test gate between waves.** Full EditMode + PlayMode suite green before the next wave (matches the validated multi-agent refactor workflow: parallel disjoint, verify diffs, gate between waves, hold network-critical). Note: the current suite is thin — add the §3c coverage before relying on the gate.
2. **Strangler-fig, not big-bang.** Keep the static `instance` accessor as a temporary façade while the core is carved out. Remove façades only once nothing depends on them.
3. **POCO never triggers a state transition.** Waves 1–3 cores return a *decision*; the adapter calls `NextGameState`/`SetGameState`. If a POCO calls a transition directly, `SwitchGameState` + `DoStateMethodRpc` never fire and clients freeze in the old state (`VictoryConditionCheckState.cs:49-56`, `GameManager.cs:196-208`).

## 4. Extraction waves (corrected ordering)

**Phase 0-a — characterization & golden tests.** No new asmdef. Pin current behavior (§3c step 1, §3b A/C). This is real work, not a formality — the "thin shim" must drive the existing NGO host.

**Phase 0-b — create `CorruptionDuPortail.Domain.asmdef`** (`noEngineReferences: true`) containing only zero-dependency types: `WinningTeam`, `CharacterSnapshot`, `GameSnapshot`, `IWinningConditionEvaluator`. The compiler boundary must exist the moment the first POCO interface is written, so it cannot silently re-acquire a Unity `using`. *(Resolved disagreement: asmdef at the start of extraction — not Phase 0-a, not after Wave 1.)*

**Wave 1 — victory + loop math (depends on the snapshot, NOT a free leaf)**
- Execute the `WinningCondition` refonte (§3a) + `GameSnapshotBuilder` + losslessness/differential tests (§3c 2–4).
- Extract `VictoryEvaluator` (the loop), `VoteTally`, `ChainingResolver` into `Domain`.
- Extract `GameLoopMachine` arithmetic. **Its adapter integration must preserve the load-bearing `OnEndStateClient → write currentGameStateIndex.Value → OnStartStateClient` ordering** (`GameManager.cs:201-208`); client listeners react to the `NetworkVariable.OnValueChanged` directly (`BoardCameraManager.cs:53`, `RoomFog.cs:37`, `LightManager.cs:14`). Validate this ordering with a **PlayMode** test, not just EditMode arithmetic.
- Convert the now-POCO victory/chaining tests PlayMode → EditMode.

**Wave 2 — determinism + role distribution**
- Add `IRandomProvider`; freeze the role-pool ordering (§3b A). Adapter injects a Unity-backed impl; tests inject a seeded one.
- Extract `RoleDistributor`. Golden-test the assignment.

**Wave 3 — power resolution**
- Extract `PowerResolver` (effect arithmetic only) → returns `EffectDescriptor` value objects. NGO ownership (`NetworkObject.TrySetParent`, `ownerClientId`) and all FMOD/Focus/RPC side-effects stay in the adapter.

**Wave 4 — HELD (network-critical, do last, smallest diffs, most review)**
- `GameLoopMachine` fully owning the state index while the adapter mirrors it into `currentGameStateIndex`.
- Reflection RPC dispatch (`CallStateMethodRpc` / `CallMethodAfterRpc`). Do not rename/move `GameState` classes without checking in-flight `FixedString64Bytes` packets.
- **Do not touch** `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` simulated-gateway semantics — preserve verbatim; the bot-debug flow silently breaks otherwise.

## 5. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `VictoryEvaluator` re-enters NGO via `WinningCondition` pulling the singleton (tests green, prod desynced) | `WinningCondition` refonte (§3a) is a **prerequisite** of Wave 1; differential tests prove parity |
| `hackedCharacterClientId` read from serialized `Role` → always default | Snapshot builder reads it from the **live `POmniscience`** instance; dedicated losslessness test |
| `CharacterSnapshot.isFake` recomputed in the POCO (extension lives in an NGO assembly) | `isFake` set **at source** in the mapping; bots mis-filtered otherwise → false game end (`VictoryConditionCheckState.cs:28`) |
| `GameLoopMachine` adapter splits "decide" from "write NV" across a frame | Preserve `OnEnd → write NV → OnStart` ordering in one method; PlayMode callback-order test |
| `GetSafeRpcTarget` leaks into a "pure" `PowerResolver` (12+ call sites: `LobbyPlayerInfoHolder.cs:79`, `ChatManager.cs:174`, `Power.cs:158`, all concrete powers) | `PowerResolver` returns `EffectDescriptor`; adapter dispatches RPCs after |
| `Role`/`EventReference` (FMOD) can't cross the asmdef → dual source of truth | Adapter maps `roleID → sound`; `RoleSnapshot` carries no `EventReference`; tested mapping |
| Determinism beyond `Random` (dict order, frame timing, spawn order) | §3b: freeze pool order; quarantine `AwakeningState` frame randomness from Wave 1 |
| Scope creep into redesign | Constraint at top; reviewer rejects any behavior change not backed by an unchanged golden/differential test |

## 6. Definition of done (per system)

- POCO class with no `UnityEngine`/`Unity.Netcode` `using`, in the `Domain` asmdef (compiler-enforced).
- Golden + differential tests proved parity across the extraction; fast EditMode tests now cover the rules (moved off PlayMode where the logic became POCO).
- Adapter reduced to I/O: map-in (lossless), call core, apply-out — including event dispatch and RPC targeting it still owns.
- State-transition ordering and the simulated-client gateway verified unchanged (PlayMode where network-observable).
- Full suite green; behavior characterization tests unchanged.

---

## Story 1.4 — `GameSnapshotBuilder` equivalence contract (foundation invariant)

*Born with the snapshot value objects in Story 1.4. The builder itself is implemented in Story 2.1; this section is the spec it must satisfy.*

**Equivalence contract.** For any live server state `S`, `GameSnapshotBuilder.FromLiveState(S)` must produce a snapshot `s` such that, for every winning condition `c`, `c.CheckCondition(s)` equals `c.CheckCondition()` evaluated against `S` — **bit-for-bit**, across the full Story 1.2 boundary matrix.

**The `hackedByOmniscienceTarget` rule (load-bearing).** `CharacterSnapshot.HackedByOmniscienceTarget` must be read from the **live `POmniscience` instance** in `character.role.powers`, never from a serialized `Role` copy — `hackedCharacterClientId` is a plain `ulong` on a `NetworkBehaviour`, never part of `Role.NetworkSerialize`. Reading it from a serialized Role yields `HACKED_CHARACTER_DEFAULT` and `WOmniscienceHackedCharacter` silently never wins (see the trap note above, line ~82).

**Synchrony.** `FromLiveState` must run **synchronously before any `await`** — a `NetworkVariable` can change across a frame, so a snapshot torn across an await is not a faithful capture of `S`.

**Anti-tautology / mutation-sentinel inheritance.** The Story 1.4 value objects are proven **field-sensitive in equality** (`SnapshotValueObjectTests.CharacterSnapshot_MutatingExactlyOneField_BreaksEquality`, one case per field incl. `hackedByOmniscienceTarget`). Therefore the Epic 2 differential — which keys on snapshot equality — **cannot be blind** to a lying mapping that corrupts a single field: corrupting that field necessarily breaks equality and turns at least one differential case red. Story 2.1/2.2 inherit this guarantee; they must NOT weaken `CharacterSnapshot`/`GameSnapshot` equality (e.g. drop a field from `Equals`) without re-deriving the differential's blindness analysis.
