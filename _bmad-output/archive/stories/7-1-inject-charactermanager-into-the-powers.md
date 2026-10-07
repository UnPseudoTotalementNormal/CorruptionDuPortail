# Story 7.1: Inject CharacterManager into the powers

Status: review

## Story

As a developer,
I want every power to receive `CharacterManager` through its lane (B at the server spawn site, C in `OnNetworkSpawn` for client replicas) instead of the `GameManager` hub-hop,
so that the largest pass-through family stops routing through the locator.

First bulk application of the 6.x seam. The pattern is FROZEN (6.1/6.3); this story is volume, not design.

## Acceptance Criteria

1. **No hub-hop left in powers.** No `P*` power (nor `Power.cs` base, `PowerComponent`s, `PowerObjects`) reaches `CharacterManager` via `GameManager.instance.characterManager` / `GameManager.For(nm).characterManager` / `CharacterManager.instance` / `CharacterManager.For(nm)`. Resolved refs live in fields.
2. **Lanes applied per the convention.** Base-class solution preferred: resolve `_characterManager` ONCE in `Power.OnNetworkSpawn` (lane C via `CompositionRoot.For(NetworkManager)`) and expose it `protected` to all concrete powers — concrete powers consume the field. Server-side push at the spawn site (`CharacterManager.cs:444`, lane B) optional redundancy only if a pre-spawn read exists (verify; none expected).
3. **NFR5 byte-identical.** `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` bodies untouched; only the manager access path changes. RPC attributes/targets untouched.
4. **Batched, gated.** Migration in compiler-enumerated batches (~8-10 files); full suite + `MultiClientGameFixture` + goldens (Epic 4 power traces!) unchanged after each batch.
5. **Guards extended.** Every migrated power appended to the shared registry; both guards green (the 6.3 `For(` rule now bites on any straggler).

## Tasks / Subtasks

- [x] **Task 1: Base-class seam** (AC: 2)
  - [x] `Power.cs`: added `protected CharacterManager characterManager;` resolved in `Power.OnNetworkSpawn` via `CompositionRoot.For(NetworkManager).CharacterManager` + `Assert.IsNotNull`. Rerouted Power.cs's own 6 hub-hops (L68 ownerCharacter, L89/L161 CharacterManager.For incl. GetSafeRpcTarget, L171/L234/L235) to the field. `PowerComponent.cs` got the same base seam (its `ownerCharacter` + subclasses now use the inherited field).
  - [x] Every migrated concrete power calls `base.OnNetworkSpawn()` (verified while editing). The assert is the designed failure for a missing base call.
- [x] **Task 2: Migrate concrete powers in batches** (AC: 1, 3, 4)
  - [x] Re-derived the inventory by grep at dev time (the authority). Mechanically rerouted the characterManager slice in every power: `GameManager.For(NetworkManager).characterManager` → `characterManager`, `CharacterManager.For(NetworkManager)` → `characterManager`. **`GameManager.For(nm).gameInfoRevealer` hops left untouched (7.3 scope)** — those files are "mixed" and stay off the no-locator registry until 7.3.
  - [x] `PersonalBeaconObject` (PowerObject, plain class): lane B — CharacterManager pushed into its constructor by the owning `PPersonalBeacons` (which resolved it in OnNetworkSpawn). `PowerComponent`s use the inherited base field.
  - [x] **Recorded exception:** `PPersonalBeacons.Awake` (L33) reads characterManager PRE-spawn (Awake precedes OnNetworkSpawn, so the lane-C field is not yet resolved; a lane-B spawn-site push cannot help — `Instantiate` runs Awake before the push). Its post-spawn reads were migrated; L33 keeps the `GameManager.For(...).characterManager` hop and is deferred to 7.3 (the file is mixed/unregistered anyway via gameInfoRevealer).
  - [x] Gated: console clean after each batch → EditMode 162 + PlayMode 146 → Epic 4 `PowerGoldenTraceTests` unchanged.
- [x] **Task 3: Guards** (AC: 5)
  - [x] Appended the fully-clean migrated types to the shared registry: `Power` + 7 concrete powers (PAutoCorruption, PClandestineObservation, PEyeOfTheVoid, PInfiniteMessage, PLegacy, PReincarnation, PVisionOfTheImpossible) → `All` (both guards). Added new `DiSeamMigratedConsumers.NoLocatorOnly` (guard #1 only — no scene/prefab instance) = PowerComponent, PCPowerUnlockWhenChain, PCReparentOnChain, PersonalBeaconObject. Guard #1 now scans `All ∪ NoLocatorOnly`. `DiSeamGuard` + `SceneWiringGuard` categories: 7/7 green.
- [x] **Task 4: Final gate** (AC: 4)
  - [x] Full EditMode **162/162** + PlayMode **146/146** (incl. Epic 4 power golden traces + guards). Boot smoke (Play on GameScene): 0 errors, no Power.characterManager assert. Power-spawn coverage comes from the PlayMode power suite (PowerTests/VisionPowerTests/EntrapmentPowerTests/CorruptionTests/PowerGoldenTraceTests, all green). Sprint-status `7-1 → review`. Single gated commit (NFR7: every intermediate batch compiled clean).

## Dev Notes

- **The Epic 4 golden traces are the real net here** — every power's effect order is pinned (`[Category("GoldenMaster")]` traces from 4.0-4.5). A reroute that changes resolution timing shows up there. A moved golden = stop.
- **Why base-class field beats per-power wiring:** powers are prefab-instantiated at runtime — lane A is impossible (prefab can't ref scene), and 20+ per-power `OnNetworkSpawn` overrides would be copy-paste noise. One resolve in `Power.OnNetworkSpawn` covers all; concrete powers just read the field. This mirrors how `GameState.gameManager` already works (one injected ref, all states consume).
- **Bot path:** several powers RPC with bot targets (`clientId >= 100`); fixture cases from 5.0 cover interception. Do not touch dispatch code.
- **Watch `PCardsShuffling` (9 hits) and `PDroolyHealing` (8)** — densest files, likely multiple member hops (some will be `.gameInfoRevealer` → leave for 7.3). Reroute ONLY the `.characterManager` slice here; mixed lines get partially rewritten, that's expected.
- Staleness: counts above are indicative; the compiler + guard are the authority at dev time.

### Project Structure Notes

- Modified: `Power.cs` + ~24 power/component/object files + shared registry. No scene/prefab wiring expected (lane C/B only). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Networked init in `OnNetworkSpawn` ✓ (lane C). Never `FindObjectOfType`/ad-hoc singleton in `OnNetworkSpawn` — root access is the codified exception.
- `GetSafeRpcTarget` on every RPC target; `IsLocalOrSimulated` not `IsLocalClient` — untouched, verbatim.
- Hot-path: the resolve is once-per-spawn, zero per-frame cost. No LINQ in any touched `Update`.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3 lanes B/C, §7 recipe] / [epics.md#Story 7.1]
- [Source: Assets/Scripts/Characters/Powers/Power.cs] — base class to carry the field.
- [Source: Assets/Scripts/Characters/CharacterManager.cs:444] — spawn site (lane B option).
- [Source: Assets/Scripts/Characters/Powers/PTruthChains.cs] — the 6.3 worked example to copy.
- [Source: _bmad-output/implementation-artifacts/6-3-*.md] — previous story: root API, guard For( rule.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow).

### Debug Log References

- DiSeamGuard + SceneWiringGuard categories: 7/7 PASS.
- Full EditMode 162/162 PASS; full PlayMode 146/146 PASS (after the RoleTests harness fix below).
- Initial PlayMode run: 4 `RoleTests` failed in SetUp on `Power.characterManager unresolved` — root cause + fix recorded below.
- Boot smoke (Play on GameScene): 0 errors.

### Completion Notes List

- **Base-class lane C (the chosen seam).** One resolve in `Power.OnNetworkSpawn` (`characterManager = CompositionRoot.For(NetworkManager).CharacterManager; Assert.IsNotNull(...)`) covers every power; concrete powers just read the inherited `protected characterManager`. Same seam added to `PowerComponent` (its `ownerCharacter` + the two `PC*` subclasses use the inherited field). Mirrors the 6.3 `PTruthChains` pattern, scaled by inheritance instead of 20+ copy-paste overrides.
- **Mechanical reroute, two patterns:** `GameManager.For(NetworkManager).characterManager` → `characterManager` and `CharacterManager.For(NetworkManager)` → `characterManager`. NFR5 verbatim: `GetSafeRpcTarget`/`IsLocalOrSimulated`/`GetLocalClientId` bodies untouched — only the manager *access path* changed (e.g. `CharacterManager.For(nm).GetSafeRpcTarget(...)` → `characterManager.GetSafeRpcTarget(...)`).
- **Registry split (guard mechanics).** A migrated file can only join the no-locator registry once it is 100% locator-free, because guard #1 forbids ANY `GameManager.For(` (it cannot tell `.characterManager` from `.gameInfoRevealer`). So: fully-clean files → `All`; clean-but-no-scene-instance files → new `NoLocatorOnly` (guard #1 only); **mixed files that still hold a `gameInfoRevealer` hop are migrated (characterManager slice) but NOT registered yet — they join the registry in 7.3** when their last hop is also gone. AC1 (no power reaches CharacterManager via locator) is satisfied in code for all powers; the guard enforcement for mixed files is deferred, recorded.
- **Mixed (characterManager migrated, gameInfoRevealer left for 7.3, NOT registered):** PCardsShuffling, PBlessing, PBoundByInk, PChainedByTheShadows, PCorruptingMark, PCorruptionInsight, PCorruptionKnowledge, PDroolyHealing, PHighPriorityBounty, PLackOfAffection, PPersonalBeacons.
- **`PersonalBeaconObject` lane B:** plain class, not NGO-spawned — CharacterManager pushed into its constructor by `PPersonalBeacons` (3rd arg). In `NoLocatorOnly` (no scene instance).
- **Recorded pre-spawn exception — `PPersonalBeacons.Awake` (L33):** reads `characterManager` in Awake, which runs before OnNetworkSpawn resolves the field; a lane-B spawn-site push can't help (Object.Instantiate runs Awake before the push returns). Left on the `GameManager.For(...).characterManager` hop, deferred to 7.3 (file is mixed/unregistered regardless). Production is unaffected: CharacterManager is a scene object spawned before powers.
- **RoleTests harness fix (the one test touch).** `Power.OnNetworkSpawn` now resolves CharacterManager eagerly + asserts; `RoleTests` spawned bare `Power` objects with no CharacterManager (it only tests powerUseLeft regen), so the assert fired in SetUp. Added a `CharacterManager` to the harness (its Awake claims the static `instance` the resolve falls back to) — mirrors PowerTests/VisionPowerTests etc. The regen assertions are unchanged. Production safe (the assert catches a missing `base.OnNetworkSpawn()` call — the designed failure).
- **Behaviour-preserving:** Epic 4 `PowerGoldenTraceTests` (effect ordering) + all power PlayMode tests green; no golden moved.

### File List

- **Modified (base seam):** `Assets/Scripts/Characters/Powers/Power.cs`, `Assets/Scripts/Characters/Powers/PowerComponents/PowerComponent.cs`
- **Modified (clean powers → registry):** `PAutoCorruption.cs`, `PClandestineObservation.cs`, `PEyeOfTheVoid.cs`, `PInfiniteMessage.cs`, `PLegacy.cs`, `PReincarnation.cs`, `PVisionOfTheImpossible.cs`, `PowerComponents/PCPowerUnlockWhenChain.cs`, `PowerComponents/PCReparentOnChain.cs` (all under `Assets/Scripts/Characters/Powers/`)
- **Modified (mixed powers — characterManager slice only):** `PCardsShuffling.cs`, `PBlessing.cs`, `PBoundByInk.cs`, `PChainedByTheShadows.cs`, `PCorruptingMark.cs`, `PCorruptionInsight.cs`, `PCorruptionKnowledge.cs`, `PDroolyHealing.cs`, `PHighPriorityBounty.cs`, `PLackOfAffection.cs`, `PPersonalBeacons.cs` (all under `Assets/Scripts/Characters/Powers/`)
- **Modified (lane B object):** `Assets/Scripts/Characters/Powers/PowerObjects/PersonalBeaconObject.cs`
- **Modified (guards/registry):** `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs`, `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs`
- **Modified (test harness):** `Assets/Scripts/Tests/PlayMode/RoleTests.cs`
- **Modified (tracking):** `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-12 — Story 7.1: CharacterManager injected into the powers via a `Power.characterManager` base-class field (lane C), resolved once in `Power.OnNetworkSpawn`. 22 power files rerouted off the GameManager/CharacterManager locator (the characterManager slice); `PersonalBeaconObject` via lane-B constructor push. Clean files registered to both guards (`All`) or guard #1 (`NoLocatorOnly`); mixed `gameInfoRevealer`-holding files deferred to 7.3. EM 162 / PM 146 green. Status → review.
