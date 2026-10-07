# Story 5.0c: `CharacterManager` per-NetworkManager registry + primary façade

Status: done

## Story

As a developer,
I want `CharacterManager` resolvable via `CharacterManager.For(NetworkManager)` with `instance` reduced to a primary-NM façade,
so that a second in-process client's replica coexists instead of being destroyed at Awake — without changing the bot flow (`GetSafeRpcTarget` / `IsLocalOrSimulated`) by one byte.

## Acceptance Criteria

1. A probe (NGO source reading + recorded conclusion) settles whether `NetworkObject.NetworkManagerOwner` is assigned before or after `Awake()` of a replicated object, and the Awake-guard design is chosen per the decision tree in Dev Notes.
2. Same-NM duplicates are still destroyed (today's semantics); replicas owned by a different `NetworkManager` are registry-only — never destroyed, never touching `instance`.
3. `CharacterManager.For(nm)` resolves the registry; for the primary NM it falls back to `instance` so the Awake→spawn window behaves exactly as today.
4. Gameplay call sites (powers, `Character`, POCO power objects, GameLogic) resolve via `For(...)`; UI call sites stay on the `instance` façade.
5. `GetSafeRpcTarget` (`CharacterManager.cs:45-52`) and `IsLocalOrSimulated` (`CharacterManager.cs:167-172`) method **bodies are byte-identical** — only callers' access paths change.
6. Full EM + PM suite + console clean after **every batch**; one conventional commit per batch.

## Tasks / Subtasks

- [x] Task 1: Record test baseline (same procedure as 5.0a Task 1).
- [x] Task 2: Probe — NGO source reading (AC: 1)
  - [x] Locate NGO 2.6.0 package source: `Library/PackageCache/com.unity.netcode.gameobjects@*/Runtime/` (glob it; exact hash suffix varies).
  - [x] Read `Spawning/NetworkSpawnManager.cs` (look for `CreateLocalNetworkObject` / `InstantiateAndSpawn...`) and `Core/NetworkObject.cs` (look for `NetworkManagerOwner` assignments and the `NetworkManager` property getter).
  - [x] Answer: NetworkManagerOwner is assigned AFTER `Object.Instantiate` returns → NOT visible in `Awake`. Evidence in Dev Agent Record.
  - [x] Pick design A or B per the decision tree below. → **Design B** (probe conclusive: owner not visible at Awake).
- [x] Task 3: Batch 1 — registry + guard, no call-site change (AC: 2, 3)
  - [x] Implement registry, `For()`, chosen Awake/spawn guard in `CharacterManager.cs` (Design B). Commit 2541ca2.
  - [x] Gate + commit. (155 EM + 138 PM)
- [x] Task 4: Batch 2 — powers call sites (AC: 4, 5) — 30 reroutes / 15 files. Gate + commit 5a09082.
- [x] Task 5: Batch 3 — `Character.cs`, GameInfoRevealer, BoardManager, POCO power object, GameStates (AC: 4, 5). Gate + commit 8fd254f.
- [x] Task 6: Verified UI/static/Mono-presentation untouched; `GetSafeRpcTarget`/`IsLocalOrSimulated`/`GetLocalClientId` bodies byte-identical via `git diff ebfdce4..HEAD` (AC: 5).

## Dev Notes

### Current state of `CharacterManager.cs` (read it fully before editing)

- `CharacterManager : NetworkBehaviour` (`CharacterManager.cs:16`), `public static CharacterManager instance;` (line 18 — plain field, not property).
- `Awake()` (lines 115-123): `if (instance != null && instance != this) { Destroy(this.gameObject); return; } instance = this;`
- `OnNetworkSpawn` (125-137): subscribes `networkedCharacters.OnListChanged`, dirties cache.
- `OnNetworkDespawn` (139-149): unsubscribes; `if (instance == this) instance = null;`
- `OnDestroy` (151-163): safety-net unsubscribe; `if (instance == this) instance = null;`
- Carries the **bot flow**: `GetSafeRpcTarget` (45-52, builds `RpcParams` via instance-level `NetworkManager.RpcTarget` — already per-instance, good), `IsLocalOrSimulated` (167-172), `GetLocalClientId` (165, uses `NetworkManager.LocalClientId` — already per-instance).

### Target additions (Batch 1)

```csharp
private static readonly Dictionary<NetworkManager, CharacterManager> s_byNetworkManager = new();

/// Resolves the CharacterManager owned by the given NetworkManager. For the primary
/// (Singleton) manager this falls back to the Awake-claimed instance so the
/// pre-spawn window behaves exactly as the historical static access.
public static CharacterManager For(NetworkManager _networkManager)
{
    if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _manager) && _manager != null)
    {
        return _manager;
    }
    return _networkManager == NetworkManager.Singleton ? instance : null;
}
```

Registry writes: `s_byNetworkManager[NetworkManager] = this;` at the **top of `OnNetworkSpawn`** (the inherited `NetworkManager` property is always valid there). Registry cleanup in `OnNetworkDespawn` AND `OnDestroy` (mirror the existing double-cleanup pattern of `instance`): `if (s_byNetworkManager.TryGetValue(NetworkManager, out var _m) && _m == this) s_byNetworkManager.Remove(NetworkManager);` — in `OnDestroy`, guard `NetworkManager` access with a try/catch-free null check via `TryGetComponent<NetworkObject>` if needed; NGO gotcha: `NetworkManager.Singleton == null` during shutdown teardown.

**Domain-reload trap (project runs with domain reload disabled):** statics survive Play Mode sessions. Add:

```csharp
#if UNITY_EDITOR
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStaticsForDomainReloadDisabled()
{
    s_byNetworkManager.Clear();
    instance = null;
}
#endif
```

Note: `instance = null` here is **new** behavior on play-mode-restart-without-domain-reload — but the existing despawn/destroy paths already null it, so this only covers crash-teardown; acceptable and aligned with project rule "any mutable static must register SubsystemRegistration reset".

### Awake-guard decision tree (after Task 2 probe)

**Design A — `NetworkManagerOwner` IS visible at Awake** (probe says: set before Instantiate returns, e.g. via prefab-handler/inactive-instantiate path):

```csharp
private void Awake()
{
    var _owner = GetComponent<NetworkObject>() != null ? GetComponent<NetworkObject>().NetworkManager : NetworkManager.Singleton;
    // NetworkObject.NetworkManager returns NetworkManagerOwner ?? Singleton, so a
    // scene-placed object (owner unset) still resolves to the primary — production identical.
    if (_owner == NetworkManager.Singleton)
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }
    // Foreign-NM replica: no static claim, no destroy; registry claim happens in OnNetworkSpawn.
}
```

**Design B — owner NOT visible at Awake** (probe says: assigned after Instantiate): the Awake guard cannot distinguish a foreign replica from a true duplicate. Then:
- Keep Awake **claim-if-free only**: `if (instance == null) instance = this;` — no destroy in Awake.
- Move duplicate destruction to `OnNetworkSpawn`, where `NetworkManager` is authoritative: if `s_byNetworkManager` already holds a different live manager for this `NetworkManager` → `Destroy(gameObject)` (same-NM duplicate, today's semantics, one frame later); if `instance` was claimed by us but our NM ≠ `NetworkManager.Singleton` → release the claim (`if (instance == this) instance = null;`) so the primary can claim it.
- Design B slightly delays duplicate destruction (Awake→spawn) — in production a scene has exactly one scene-placed CharacterManager, so the destroy path is defensive-only; record this delta explicitly in the Dev Agent Record and the commit body.

If the probe is inconclusive from source reading, choose **Design B** (it does not depend on the answer).

### Call-site migration pattern (Batches 2-3)

| Caller kind | Replacement for `CharacterManager.instance` |
|---|---|
| Inside a `NetworkBehaviour` (powers `P*.cs`, `Character.cs`, `GameInfoRevealer`, `BoardManager`, `Card`, `PowerEffectDispatcher`, `PowerComponents`) | `CharacterManager.For(NetworkManager)` |
| Inside a `GameState` (VoteState, GameEndingState, AwakeningState) | `gameManager.characterManager` (field already wired on `GameManager.cs:35`) — verify it is assigned in the scene/prefab before relying on it; if null at runtime, use `CharacterManager.For(gameManager.NetworkManager)` instead and note it |
| POCO power objects with a bound `Power` (`PersonalBeaconObject`) | `CharacterManager.For(_ownerPower.NetworkManager)` |
| Mono UI / presentation (see "stay-on-façade" list) | unchanged |
| Mono GameLogic singletons (`PowerManager`, `PowerUsageManager`, `ChatManager`, …) | unchanged in this story (no NM context of their own) |

Local-variable idiom for files with several uses in one method: `var _characterManager = CharacterManager.For(NetworkManager);` once, then reuse. Null-conditional NOT needed — `For` falls back to `instance` for the primary, so null-ness is the same as today.

**Batch 2 files (powers, ~20 files):** `TargetUtils.cs`, `PVisionOfTheImpossible.cs`, `PPersonalBeacons.cs`, `PowerEffectDispatcher.cs`, `Power.cs`, `PLegacy.cs`, `PLackOfAffection.cs` (3 uses — includes the `IsLocalOrSimulated` call at line 47: becomes `CharacterManager.For(NetworkManager).IsLocalOrSimulated(targetClientId)`), `PInfiniteMessage.cs`, `PHighPriorityBounty.cs`, `PEyeOfTheVoid.cs`, `PDroolyHealing.cs`, `PClandestineObservation.cs`, `PCardsShuffling.cs` (8 uses), `PBoundByInk.cs`, `PBlessing.cs`, `PCReparentOnChain.cs`, `PCPowerUnlockWhenChain.cs`.

**Batch 3 files:** `Character.cs` (4), `PersonalBeaconObject.cs` (2 — via `_ownerPower`), `GameInfoRevealer.cs` (5), `GameLogic/GameStates/{VoteState,GameEndingState,AwakeningState}.cs` (1 each — via `gameManager.characterManager`), `Board/Card.cs` (4), `Board/BoardManager.cs` (1), `FX/RoomFog.cs` — **check**: `RoomFog` is in FX; if it is a MonoBehaviour, it stays on the façade (it is presentation).

**Stay-on-façade (do NOT edit):** `UI/StateUI/VoteStateUI.cs`, `UI/InfoTable/InfoTableSystem.cs`, `UI/CardUI/MeIconCard.cs`, `UI/BoardUI/CardPickerManager.cs`, `UI/BoardUI/AnonymeMessageButton.cs`, `Network/LobbyPlayerInfoHolder.cs`, `Misc/DevIdentityController.cs`, `MessageSystem/SendMessagePanel.cs`, `ChatSystem/ChatWindow.cs`, `ChatSystem/ChatManager.cs`, `GameLogic/PowerUsageManager.cs`, `GameLogic/PowerManager.cs`, `Board/UI/PowerBar/PowersBar.cs`.

For every file you touch: READ it first, find the exact `CharacterManager.instance` occurrences (count must match the table in the architecture doc §1), and confirm the class kind (NetworkBehaviour vs Mono) before applying the mapping. If a file doesn't match its expected kind, stop and reclassify rather than forcing the pattern.

### What "byte-identical bot flow" means concretely (AC: 5)

`git diff` on `CharacterManager.cs` must show ZERO changes inside the bodies of `GetSafeRpcTarget`, `IsLocalOrSimulated`, `GetLocalClientId`. The `clientId >= 100` literal appears nowhere else in your diff. If a test exercising bots (`NetworkTestHelper`-based PlayMode) fails after a batch, revert the batch and bisect file-by-file.

### Gate procedure (per batch)

`refresh_unity` (compile=request) → `read_console` errors empty → full EM + PM at baseline → commit. Never stack two batches in one commit.

### Commit message templates

Batch 1: `refactor(net): add per-NetworkManager registry to CharacterManager` — body explains registry + chosen Awake design (A or B, with the probe conclusion) + production no-op argument + issue #50 motivation.
Batches 2-3: `refactor(net): resolve CharacterManager via For(NetworkManager) in <scope>` — body: mechanical reroute, façade fallback makes it production-identical, UI intentionally left on façade.
No `UX:` lines (pure infra). No AI attribution.

### Project Structure Notes

- Main file: `Assets/Scripts/Characters/CharacterManager.cs`. Call sites across `Characters/`, `GameLogic/`, `Board/`.
- Prerequisites: 5.0a + 5.0b merged.
- Branch: `epic5-network`.

### Project Context Rules (extracted from project-context.md)

- Silent killers #1/#2: `GetSafeRpcTarget` on every RPC target; `IsLocalOrSimulated` instead of `IsLocalClient` — this story must not alter either's semantics.
- Domain reload disabled: mutable statics need `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset (implemented in Batch 1).
- `OnNetworkDespawn` fires before `OnDestroy`; `NetworkManager.Singleton` may be null in `OnDestroy` during shutdown — null-check.
- Networked init in `OnNetworkSpawn`, not Awake (the registry claim follows this rule).
- Tests: PlayMode multi-client flows route through `NetworkTestHelper`; never `StartHost()` directly.
- Commits: English, conventional, body always, no AI attribution.

### References

- [Source: _bmad-output/refactor-architecture-desingleton.md#2 — core pattern, resolution table; #3 open question 1; #4 slice 3; #5 risks]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0c]
- Blast-radius file list: architecture doc §1 (78 usages / 39 files, measured 2026-06-11)
- NGO source to probe: `Library/PackageCache/com.unity.netcode.gameobjects@*/Runtime/Spawning/NetworkSpawnManager.cs`, `.../Core/NetworkObject.cs`

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story workflow)

### Debug Log References

**Task 1 — baseline (2026-06-11):** EditMode 155/155 passed, PlayMode 138/138 passed. Matches expected 155 EM + 138 PM.

**Task 2 — NGO probe → Design B chosen.** Question: when a client instantiates a replicated prefab from a `CreateObjectMessage`, is `NetworkManagerOwner` visible in `Awake`? **Answer: NO — it is assigned AFTER `Object.Instantiate` returns**, so `Awake` cannot distinguish a foreign replica from a true duplicate. Evidence (NGO 2.6.0, `com.unity.netcode.gameobjects@7e60351bfc70`):
- `Spawning/NetworkSpawnManager.cs:879-881` — `InstantiateNetworkPrefab` does `UnityEngine.Object.Instantiate(networkPrefab)`; `Awake` runs synchronously *here*, before any owner assignment.
- Client replica path: `CreateLocalNetworkObject` (:894) → `GetNetworkObjectToSpawn` (:805/:861) → `InstantiateNetworkPrefab` (:881, Instantiate→Awake) → returns at :1017 with owner still unset; owner is set later in `SpawnNetworkObjectLocally:1055` (`networkObject.NetworkManagerOwner = NetworkManager`).
- Server `InstantiateAndSpawn` path: `InstantiateNetworkPrefab` (:777, Instantiate→Awake) precedes `networkObject.NetworkManagerOwner = networkManager` (:786). Same ordering.
- `Core/NetworkObject.cs:362` — `public NetworkManager NetworkManager => NetworkManagerOwner ? NetworkManagerOwner : NetworkManager.Singleton;` → with owner null at Awake, the property resolves to `Singleton`.
- `Core/NetworkObject.cs:1772-1774` — confirms the `NetworkManagerOwner == null → Singleton` fallback.

Design B consequence (recorded delta): the Awake guard becomes claim-if-free only (`if (instance == null) instance = this;`, no destroy); same-NM duplicate destruction and foreign-NM façade reconciliation move to `OnNetworkSpawn` where `NetworkManager` is authoritative — one frame later than the historical Awake destroy. Production has exactly one scene-placed `CharacterManager`, so the destroy path is defensive-only and the delta is unobservable in production.

### Completion Notes List

- **Design B implemented** (per probe). `CharacterManager.cs`: added `s_byNetworkManager` registry + `For(NetworkManager)` resolver + `SubsystemRegistration` static reset; Awake reduced to claim-if-free; `OnNetworkSpawn` does same-NM-duplicate destroy + foreign-replica façade reconciliation + registry write; `OnNetworkDespawn`/`OnDestroy` unregister by value (teardown-safe). AC1/AC2/AC3 satisfied.
- **Bot flow byte-identical (AC5):** `git diff ebfdce4..HEAD` on `CharacterManager.cs` shows zero +/- lines inside `GetSafeRpcTarget` (now line 77), `IsLocalOrSimulated` (260), `GetLocalClientId` (258). The `clientId >= 100` literal appears nowhere else in the diff. Only callers' access paths changed.
- **Call-site migration (AC4):** 45 of 78 `CharacterManager.instance` sites rerouted (Batch 2: 30 across 15 power NetworkBehaviours → `For(NetworkManager)`; Batch 3: 15 — Character/GameInfoRevealer/BoardManager → `For(NetworkManager)`, PersonalBeaconObject → `For(_ownerPower.NetworkManager)`, 3 GameStates → `gameManager.characterManager`). 33 sites intentionally remain on the `instance` façade (UI / Mono-singleton / static presentation).
- **Reclassifications from the story's batch lists** (per the "stop and reclassify, don't force the pattern" rule — each lacks a NetworkManager context of its own):
  - `PowerEffectDispatcher.cs` (static class) — stays on façade; threading an NM through `Dispatch(...)` is an out-of-scope public-API change and it equally depends on GameManager/RoleTargetSystem/ChatManager singletons.
  - `TargetUtils.cs` (static class) — stays on façade; no NM, also still reads `GameManager.instance` (de-singletonised separately in 5.0d).
  - `Board/Card.cs` (MonoBehaviour presentation) — stays on façade; the 4 sites are local-identity event (un)subscriptions in Awake/OnDestroy.
  - `FX/RoomFog.cs` (MonoBehaviour FX) — stays on façade.
- **Behaviour-preservation argument:** production has exactly one scene-placed `CharacterManager`; `For(Singleton)` resolves to the registry entry or falls back to the Awake-claimed `instance`, so `For(...) === instance` in every production state. The only delta (Design B) is that a same-NM duplicate would be destroyed in `OnNetworkSpawn` instead of `Awake` — one frame later, defensive-only, unobservable with a single manager.
- **Gates:** full EditMode + PlayMode green at baseline (155 EM + 138 PM) after each of the 3 batch commits and at story end. Console clean (0 errors) after each compile.
- **# REVIEW-REQUIRED:** run `/gds-code-review` (or cheap local `/code-review`) before merging — this story touches the `GetSafeRpcTarget`/`IsLocalOrSimulated` access paths (bot flow), which tests cannot fully catch.

### File List

- `Assets/Scripts/Characters/CharacterManager.cs` (Batch 1 — registry + For + Design B guard + reset)
- `Assets/Scripts/Characters/Powers/PBlessing.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PBoundByInk.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PClandestineObservation.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PHighPriorityBounty.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PDroolyHealing.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PInfiniteMessage.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PEyeOfTheVoid.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PLackOfAffection.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PLegacy.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/Power.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PPersonalBeacons.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PVisionOfTheImpossible.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PowerComponents/PCReparentOnChain.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/PowerComponents/PCPowerUnlockWhenChain.cs` (Batch 2)
- `Assets/Scripts/Characters/Character.cs` (Batch 3)
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` (Batch 3)
- `Assets/Scripts/Board/BoardManager.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PowerObjects/PersonalBeaconObject.cs` (Batch 3)
- `Assets/Scripts/GameLogic/GameStates/VoteState.cs` (Batch 3)
- `Assets/Scripts/GameLogic/GameStates/GameEndingState.cs` (Batch 3)
- `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs` (Batch 3)

### Review Findings

_Code review (Poyo-triggered, # REVIEW-REQUIRED) 2026-06-11 — inline 3-lens pass (Blind Hunter / Edge Case Hunter / Acceptance Auditor over `git diff ebfdce4..HEAD`). Outcome: 0 decision-needed, 1 patch, 1 defer, 3 dismissed. No HIGH/MED correctness bug; change is behaviour-preserving for the single-NetworkManager production case (suite green 155 EM + 138 PM; bot-flow bodies byte-identical)._

- [x] [Review][Patch] Tighten the `SubsystemRegistration` reset comment so it does not read as a subscription/teardown cleanup — it is a play-restart backstop only (statics survive Play sessions with domain reload disabled); the real `OnListChanged` unsubscribe lives in `OnNetworkDespawn`/`OnDestroy`. [Assets/Scripts/Characters/CharacterManager.cs ~ ResetStaticsForDomainReloadDisabled] — fixed e0942d9
- [x] [Review][Defer] `PowerEffectDispatcher.ResolveTarget` still resolves `GetSafeRpcTarget` through the `instance` façade while migrated powers resolve per-NM. Identical under one NetworkManager; once a 2nd in-process client exists a replica's dispatched effects would route bot interception through the primary manager. Revisit when the dispatcher is de-singletonised. [Assets/Scripts/Characters/Powers/PowerEffectDispatcher.cs:95] — deferred, out of 5.0c scope.

_Dismissed (noise / handled): AC2 "never touching instance" relaxed by the spec's own Design B claim-if-free path (documented in Dev Agent Record); cross-object `OnNetworkSpawn` ordering is covered by the Awake-claimed `instance` fallback (a strength, not a defect); `For(null)` divergence has no reachable call site (all touched callers hold a valid NetworkManager)._
