# Story: Ugues — Marque d'Hurluberluges

Status: review

<!-- Implementation complete except the design-gated attribution add (held for Poyo). Kit built + wired +
     EditMode-tested (342 green). End-to-end NGO flow needs a Poyo 2-build playtest. -->


<!-- Standalone content story (new role). NOT part of the refactor epics 1–13 tracked in sprint-status.yaml,
     so sprint-status.yaml is intentionally left untouched. Design owner: Poyo. Spec captured faithfully
     from the Discord task "Faire Ugues" (thread body, 2026-07-08). -->

## Story

As a **player dealt the Ugues role (chosen / masked faction)**,
I want **my power "Marque d'Hurluberluges" to steal a copy of 3 random active élu powers at the start of the game and let me spend one per night**,
so that **I play as an unpredictable power-thief whose kit is drawn from whatever chosen roles are in the game, factice or not.**

## Design Spec (source of truth — Poyo)

From the Discord "Faire Ugues" thread:

> **Marque d'Hurluberluges** — La première nuit, Uges stock aléatoirement trois pouvoirs actifs temporaires d'élus\* présents dans la partie (même factice). Chaque nuit, il peut utiliser l'un des pouvoirs stockés, mais une fois utilisé, le pouvoir est perdu.
> \*présent dans la partie (même factice)

**Locked decisions (Poyo, 2026-07-09 via AskUserQuestion):**

| # | Question | Decision |
|---|----------|----------|
| 1 | Faction of the stealable pool | **Chosen (élus) only** (`FactionType.chosen`), Ugues excluded |
| 2 | What "active" power means | **Non-passive powers** (`Power.isPassive == false`) |
| 3 | Copy vs steal-away | **Copy** — the original owner keeps their power (works uniformly for factice, who have no live player) |
| 4 | Which stored power to use each night | **Ugues chooses** from his 3 stored powers in the power bar |

**Assumptions (reasonable defaults — flag if Poyo disagrees, see "Open Design Confirmations"):**
- Storing happens **at game start** so the 3 copies exist before Ugues' first awakening ⇒ Ugues **can use one from night 1 onward**.
- If **fewer than 3** eligible powers exist in the pool, store **what is available** (0–3).
- A stored copy is **consumed after a single use** (`maxPowerUse = 1`, no regen) and then **removed** ("perdu").

## Acceptance Criteria

1. **Given** a game containing at least 3 non-passive powers across chosen-faction roles (real and/or factice) other than Ugues, **when** the game starts, **then** Ugues receives exactly 3 fresh **copies** of powers randomly drawn from that pool, parented under his Character.
2. **Given** the eligible pool has N < 3 powers, **when** the game starts, **then** Ugues receives exactly N copies (no error, no duplicates of the same source instance).
3. **Given** a power was copied to Ugues, **when** any original owner (real or factice) is inspected, **then** that owner **still holds** their power (copy, not move).
4. **Given** Ugues holds stored copies, **when** the local Ugues player opens the power bar, **then** each stored non-passive copy appears as a usable entry alongside no visible "Marque d'Hurluberluges" engine entry.
5. **Given** Ugues selects a stored copy and completes its normal use flow, **then** the copied power executes with Ugues as owner (`ownerClientId` = Ugues), acting exactly as that power would for its native role.
6. **Given** a stored copy has been used once, **when** the use resolves on the server, **then** the copy is consumed (removed / despawned) and no longer appears in Ugues' power bar ("perdu").
7. **Only** chosen-faction, non-passive powers are ever eligible; passive powers and non-chosen roles are never drawn.
8. **Given** the same fixed RNG seed and the same game composition, **when** the game starts, **then** the 3 drawn powers are deterministic (selection driven by `IRandomProvider`, not `System.Random`/`UnityEngine.Random` static).

## Tasks / Subtasks

- [x] **Task 0 — Pre-build verification & audit (AC: 1,7)**
  - [x] Factice characters DO hold live non-passive `role.powers`: `RoleAttributionState.OnStartStateServer:65` calls `ApplyRole` → `GivePowerToCharacter` for fakes. (Static-verified; a live 2-build playtest is still Poyo's — cf. no-playtest-by-Claude.)
  - [x] Audited the 3 stealable actives overriding `OnGameStartedServer`: `PChainedByTheShadows` = `base` only (inert); `PDroolyHealing`/`PBoundByInk` add a per-instance `AwakeningState` subscription. **Decision: do NOT skip `OnGameStartedServer` on copies** (they need per-instance init) and **do NOT despawn** the spent copy — the object lingers alive so its awakening subscription never dangles (see Task 3). Documented caveat in Dev Notes.
- [x] **Task 1 — Pure selection POCO + EditMode tests (AC: 1,2,7,8)**
  - [x] `CorruptionDuPortail.Domain.StolenPowerSelector` (+ `PowerCandidate` struct): `SelectDistinct(count, pick, rng)` = distinct random pick capped at what exists; `SelectStealable(candidates, pick, rng)` = pure eligibility filter (chosen ∧ ¬self ∧ ¬passive ∧ ¬stolen) then pick, returning ORIGINAL indices. Engine-free, C# 9-safe (`readonly struct`).
  - [x] 9 EditMode tests (`StolenPowerSelectorTests`, category `StolenPowerSelector`): distinct/cap-3/N<3/empty/in-range/determinism (`SeededRandomProvider`) + filter excludes non-chosen/passive/self/already-stolen + original-index mapping. **9/9 green.**
- [x] **Task 2 — `PMarqueHurluberluges` power script (AC: 1,2,3,5,7,8)**
  - [x] `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs : Power`; `isPassive` set on the prefab (never in the bar).
  - [x] Steal runs in `OnGameStartedServer()` (server, guarded once by `_hasStolen`) — chosen over an awakening-event hook because `PowerManager.OnGameStarted` already iterates every character's populated `role.powers`, so the pool is complete (no attribution race) and it avoids the fragile awaken-event path (`AwakenCharacterServerRpc` fires `SleepCharacterClientRpc`). Builds a `PowerCandidate` list parallel to the live powers → `StolenPowerSelector.SelectStealable(..., new UnityRandomProvider())` → copy path.
- [x] **Task 3 — Copy + consume-on-use plumbing (AC: 3,5,6)**
  - [x] Reuses `CharacterManager.GivePowerToCharacter(uguesId, sourcePower, onReady)` (clones via `Instantiate` — same path `PReincarnation.cs:53-56`). Copy, not move: originals retained.
  - [x] Added optional `System.Action<Power> _onReady` to `GivePowerToCharacter` (+ mirrored on `ICharacterCommand`), fired from `OnPowerReparentComplete` after the copy is registered in the owner's `role.powers`.
  - [x] `ConfigureStolenCopy`: `isStolenCopy.Value = true`, `maxPowerUse = 1`, `powerUseRegenPerAwakening = 0`, `powerUseLeft.Value = 1`. **Consume = spent-forever** (decrement to 0, never refilled) + hidden from the bar; the copy is intentionally NOT despawned (avoids despawn-during-own-RPC + dangling awakening subs). `isStolenCopy` promoted to `NetworkVariable<bool>` so the owner client's bar can hide it.
  - [x] `PowersBar.CreatePowerBar` skips `isStolenCopy.Value && powerUseLeft.Value <= 0` ("perdu"). Server-authority preserved; nothing raw-`Destroy`ed.
- [x] **Task 4 — Assets & scene/SO wiring (AC: 1,4,5)** — *except the design-gated attribution add (held)*
  - [x] Prefab `Assets/Prefabs/Powers/MarqueHurluberluges.prefab` (dup of CorruptionInsight → swapped script GUID → `powerName`/`powerDescription`/`isPassive`/`hasToBeAwakened=0`/`maxPowerUse=1` via MCP → prefab-stage save regenerated a unique `GlobalObjectIdHash` 2667139434).
  - [x] Registered in `Assets/DefaultNetworkPrefabs.asset`.
  - [x] Wired into `Assets/ScriptableObjects/Characters/Uges.asset` `powers`.
  - [x] Awakening: Ugues is **already** at `AwakeningState.awakeningOrder[0]` (verified — awakens first, GDD-faithful). No change needed.
  - [ ] **HELD (Poyo's call): attribution.** Ugues is NOT in `RoleAttributionState.roleAttributionDictionary`, so he cannot be dealt in real matches yet. Left untouched pending explicit go-live approval (see Open Design Confirmations #2). The full kit works the moment he's added; host role-counts still gate actual dealing.
- [x] **Task 5 — Automated coverage (AC: 1,2,7 pure; 3,5,6 by construction)**
  - [x] Coverage strategy changed from a full multi-NM PlayMode test to **EditMode filter+select tests** (Task 1): the eligibility rule (AC 7) and the pick mechanic (AC 1,2,8) are now deterministically covered without the flaky 2-NM harness (port-7777, unreliable `IsOwner` — cf. memory). The end-to-end NGO flow (copy spawn → single use → bar hide) is left for Poyo's 2-build playtest; documented, not silently skipped.
- [x] **Task 6 — Verify & report**
  - [x] `read_console` clean (0 `error CS`) after every script/asset change.
  - [x] `run_tests` EditMode: **StolenPowerSelector 9/9**, full EditMode suite **333/333** (no regressions from the `Power`/`CharacterManager`/`ICharacterCommand`/`PowersBar` edits). PlayMode not run (flaky harness; e2e = Poyo playtest).

## Dev Notes

### System map (verified, with file:line)

**Role pool / factice**
- Attribution: `RoleAttributionState.OnStartStateServer()` `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:29-74`; role+powers applied by `ApplyRole` `:94-109` (Clone role → `GivePowerToCharacter` loop → `GiveRoleToCharacterRpc`).
- Fakes ARE given powers: `ApplyRole(_frozenOrder[_fakeRoleIndex], Command.CreateNewFakeCharacter())` `:65`.
- Factice = a real `Character` with `isFake == true` (`Character.cs:29`), owner id = `GameValues.FAKE_CLIENT_ID - n` (`CharacterManager.cs:396-400`). `CharacterManager.GetCharacters(false)` includes fakes; filter `!c.isFake` to exclude.
- Faction enum `FactionType { anomaly, chosen, marginal, unknown }` (`Assets/Scripts/Domain/FactionType.cs`); "élu" = `chosen`. `Role.factionType` (`Role.cs:25`).

**Power model & instancing**
- `Power : NetworkBehaviour` (`Assets/Scripts/Characters/Powers/Power.cs`). Key fields: `isPassive`, `maxPowerUse` (`:48`), `powerUseLeft` NetworkVariable (`:47`), `powerUseRegenPerAwakening` (`:49`), `hasToBeAwakened`, `targetIncludeFlags`. Events: `onPowerUsedServer` (`:99`).
- Runtime power list lives on `Role.powers` (`Role.cs:30`), filled by `Character.CheckForPowersRpc()` (`Character.cs:115-132`).
- Give a character a fresh power copy: `CharacterManager.GivePowerToCharacter(ulong, Power)` `:485-499` — `Instantiate(_power, null)` (clones the passed instance's GO) → `idHolderServer` → `Spawn(true)` → `WaitForParentToSpawnAndSet` → `OnPowerReparentComplete`.
- Remove/despawn: `CharacterManager.RemovePowerFromCharacter(ulong, Power)` `:501-519` (RPC-removes from list + `Despawn()`).
- **Closest analog to copy: `PReincarnation.cs:46-57`** — loops target's `role.powers` and `GivePowerToCharacter(ownerClientId.Value, _rolePower)`. Also flips itself passive after use (`ChangeIsPassiveRpc(true)`).

**Awakening / night**
- `AwakeningState` (`Assets/Scripts/GameLogic/GameStates/AwakeningState.cs`); order authored as `awakeningOrder: List<AwakeningLayerObject>` `:25`; layer 0 wakes first. `AwakeLayer` `:77-106`. `PowerManager.OnGameStarted()` `:80-101` calls `OnGameStartedServer()` on every non-fake role power; `OnPowerSpawned` `:52-65` re-calls it on **late-spawned** powers if the game already started — this is exactly the path a stolen copy hits (relevant to Task 0 audit).
- Per-awakening use refill: `Role.AwakenRole()` `Role.cs:48-57` refills `powerUseLeft` per `powerUseRegenPerAwakening` (`-1` = full). Set copy regen to **0** so a spent copy stays spent.

**Use flow / power bar (no UI work needed)**
- `PowersBar.CreatePowerBar` (`Assets/Scripts/Board/UI/PowerBar/PowersBar.cs:139-176`) builds one entry per **non-passive** power of the local character → stored copies show automatically; the `isPassive` engine power does not.
- Click → `PowerUsageManager.TrySelectPower` (`PowerUsageManager.cs:44-63`) → `Power.StartUse()` → (target flow via `selectionFlowService`) → `Power.OnUsed()` → `OnUsedServer()` decrements `powerUseLeft` and fires `onPowerUsedServer` (our consume hook).

### RNG / determinism
- Port `IRandomProvider` (`Assets/Scripts/Domain/IRandomProvider.cs`); impls `UnityRandomProvider` (`Assets/Scripts/GameLogic/UnityRandomProvider.cs`) + `SeededRandomProvider` (`Assets/Scripts/Domain/SeededRandomProvider.cs`). `RoleAttributionState` uses `new UnityRandomProvider()` directly (`:60`) — mirror that in the power adapter; the pure selection POCO takes `IRandomProvider` and is EditMode-tested with `SeededRandomProvider` (see `RoleDistributorTests.cs`, `RandomProviderTests.cs`).

### Consume-on-use & copy configuration
- The copy must be configured **after** it reparents (async). `GivePowerToCharacter` currently returns void; add an optional `Action<Power> onReady` forwarded through `OnPowerReparentComplete`. Existing callers unaffected (default null).
- Set on the copy: `maxPowerUse = 1`, `powerUseLeft.Value = 1` (server), `powerUseRegenPerAwakening = 0`, `isStolenCopy = true`.
- Consume: subscribe `copy.onPowerUsedServer += consume;` where `consume` calls `RemovePowerFromCharacter`. Cache both the delegate and the copy ref; unsubscribe in the copy's despawn to satisfy the sub/unsub-mirror rule.

### Files to touch
- **NEW** `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs`
- **NEW** `Assets/Scripts/Domain/<UguesPowerSteal POCO>.cs` (name TBD, e.g. `StolenPowerSelector`)
- **NEW** EditMode test (`Assets/Scripts/Tests/Editor/…`) + PlayMode test (`Assets/Scripts/Tests/PlayMode/…`)
- **UPDATE** `Assets/Scripts/Characters/CharacterManager.cs` — add `onReady` callback to `GivePowerToCharacter` / `OnPowerReparentComplete`.
- **UPDATE** `Assets/Scripts/Characters/Powers/Power.cs` — add `[NonSerialized] public bool isStolenCopy`.
- **UPDATE (asset)** `Assets/ScriptableObjects/Characters/Uges.asset` — add Marque prefab to `powers`.
- **UPDATE (asset)** `RoleAttributionState` SO — add Ugues to attribution dict (design confirm).
- **UPDATE (asset)** `AwakeningState` SO — ensure Ugues in `awakeningOrder[0]`.
- **UPDATE** NGO NetworkPrefabs list — register Marque prefab (if not auto).

### Project Structure Notes
- POCO selection logic goes in `CorruptionDuPortail.Domain` (pure, EditMode-testable). If a test asmdef can't see the new Domain type, add an explicit Domain reference to that test asmdef (autoReferenced doesn't reach test asmdefs — CS0012 otherwise).
- Unity is **C# 9** (no `record struct`, no file-scoped namespace). Use plain `struct` + one `IEquatable` if a value type is needed.

### Project Context Rules (from _bmad-output/project-context.md)
- **Server authority strict** — mutate game state (steal, config copy, despawn) on **server only**; clients propose via `ServerRpc`.
- **`NetworkObject.Spawn()` / `Despawn(destroy:true)` server-only**; clients must never `Destroy()` a NetworkObject (`:66-67`). Use `RemovePowerFromCharacter` (does the RPC list-removal + despawn).
- **`GetSafeRpcTarget(clientId)`** wraps every RPC target (clientId ≥ 100 = simulated bot intercepted by host); **`IsLocalOrSimulated`** instead of `IsLocalClient`.
- **Resolve `NetworkManager` via `base.NetworkManager` / `NetworkObject.NetworkManager`, never `NetworkManager.Singleton`** — breaks the multi-NM test harness (`:158`).
- **Async = UniTask** (never `System.Threading.Tasks.Task`); **audio = FMOD** via `GameAudioManager` (the Marque engine power is silent, but any copied power keeps its own FMOD events).
- **SOs read-only at runtime** (`:104,352,586`) — never mutate the `Uges.asset`/state SOs at runtime; clone (`ApplyRole` already clones the role).
- **Renaming a `[SerializeField]`** needs `[FormerlySerializedAs]` or it silently wipes prefab/scene data (`:95,584`). Prefer append-don't-rename; re-wire every instance via MCP; verify by read-back.
- **New power** ⇒ Tests.Editor for pure logic **and** Tests.PlayMode for the RPC flow (`:400`).
- **Sub/unsub mirror** — every `NetworkVariable`/event subscription has a matching unsubscribe; a Spawned NetworkBehaviour unsubscribes in `OnNetworkDespawn`, caching the resolved target (`:194`).

### References
- Discord task "Faire Ugues" thread body (spec) — 2026-07-08.
- GDD role/power tables — `_bmad-output/planning-artifacts/gdds/gdd-Corruption Du Portail-2026-05-29/gdd.md:166-168, 184, 203-239` (Ugues `[WIP]`, awakening order, passive/active power split).
- `RoleID.Uges = 9999` — `Assets/Scripts/Characters/RoleID.cs:20`.
- `Uges.asset` current state (`powers: []`, chosen/masked, portrait wired) — `Assets/ScriptableObjects/Characters/Uges.asset`.

## Open Design Confirmations (Poyo — non-blocking; sensible defaults assumed)

1. **Night-1 use** — assumed Ugues can use a stored power **from night 1** (stored at game start). If storing should block use on night 1 (store-only first night), gate `CanUse` on `nightIndex > 0` for stolen copies.
2. **Make Ugues dealable now** — Task 4 adds him to the attribution dictionary so he can appear in games. Confirm you want him live (vs. building the kit but leaving him out of the pool for now).
3. **Pool < 3** — assumed store what's available. Alternative: guarantee 3 by relaxing filters (not recommended — breaks "élus only").
4. **Unusable-as-Ugues copies** — some powers gate validity on the caster's own role/state; as a copy they may occasionally be unusable. Assumed acceptable. Flag if any specific power must be excluded from the pool.

## Dev Agent Record

### Agent Model Used
claude-opus-4-8

### Debug Log References
- Temp diagnostics use the `[UGUES]` tag (empty-pool + unresolved-owner logs in `PMarqueHurluberluges`). Filter `read_console` by `[UGUES]`.

### Completion Notes List
- Story authored by context-engine analysis (Explore agent full pipeline map + direct code verification of factice-power-instancing, RNG port, and OnGameStartedServer override set). Design decisions 1–4 ratified by Poyo.
- **Implemented on branch `feat/role-ugues-marque-hurluberluges`. No commit made** (awaiting Poyo per project rule).
- Steal trigger = `OnGameStartedServer` (not an awakening event): `PowerManager.OnGameStarted` already walks every populated `role.powers`, so the pool is complete → no attribution race, and it dodges the awaken-event fragility (`AwakenCharacterServerRpc` fires `SleepCharacterClientRpc`).
- Consume model = **spent-forever + hidden**, NOT despawn. Rationale: despawning during the copy's own `OnUsed` RPC chain is unsafe, and stolen `PDroolyHealing`/`PBoundByInk` copies hold a never-unsubscribed `AwakeningState` subscription — keeping the object alive means that sub never dangles. `isStolenCopy` is a `NetworkVariable<bool>` so the owner client's bar can hide the spent copy.
- **Known caveat**: a stolen `PDroolyHealing`/`PBoundByInk` copy keeps its `AwakeningState` subscription for the rest of the match (pre-existing no-unsub pattern; harmless while the object stays alive — it just no-ops on later nights). If these ever need despawn, add matching `OnNetworkDespawn` unsubscribes to those two powers first.
- **HELD for Poyo**: attribution add (go-live). Everything else is done; adding Ugues to `RoleAttributionState.roleAttributionDictionary` (any non-zero host count) makes him fully playable.
- Tests: EditMode **9/9** (StolenPowerSelector) + full EditMode **333/333** (no regressions). PlayMode e2e deferred to a Poyo 2-build playtest (flaky harness).

### File List
**New**
- `Assets/Scripts/Domain/StolenPowerSelector.cs` — pure selector kernel (`SelectDistinct`, `SelectStealable`, `PowerCandidate`).
- `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` — the power (server steal + copy config).
- `Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs` — 9 EditMode tests.
- `Assets/Prefabs/Powers/MarqueHurluberluges.prefab` (+ `.meta`) — the power prefab.

**Modified**
- `Assets/Scripts/Characters/CharacterManager.cs` — `GivePowerToCharacter` gains optional `Action<Power> _onReady`.
- `Assets/Scripts/Characters/ICharacterCommand.cs` — interface signature mirrored.
- `Assets/Scripts/Characters/Powers/Power.cs` — `NetworkVariable<bool> isStolenCopy`.
- `Assets/Scripts/Board/UI/PowerBar/PowersBar.cs` — hide spent stolen copies from the bar.
- `Assets/DefaultNetworkPrefabs.asset` — registered the Marque prefab.
- `Assets/ScriptableObjects/Characters/Uges.asset` — `powers` now holds the Marque power.

### Change Log
- 2026-07-09 — Implemented Ugues / Marque d'Hurluberluges (steal 3 random chosen active powers at game start, one-shot each, Ugues picks). POCO + power + prefab + wiring; EditMode 9/9 + full suite 333/333. Attribution go-live HELD for Poyo. No commit.
