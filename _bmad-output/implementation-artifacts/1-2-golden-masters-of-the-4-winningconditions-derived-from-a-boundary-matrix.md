# Story 1.2: Golden masters of the 4 WinningConditions, derived from a boundary matrix

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want every `WinningCondition`'s current verdict pinned as golden-master tests derived from a branch×condition matrix,
so that any later extraction that changes behavior fails loudly, with traceability to the exact missing branch.

## Acceptance Criteria

**Given** the harness is proven faithful (Story 1.0) and the substrate is deterministic (Story 1.1)
**When** the golden suite is authored
**Then** the case set is **derived from a boundary matrix** annexed to the story (per condition: each boolean clause × {true,false}, each cardinality boundary {0,1,n}, each non-NV field read, each early-return) — the matrix is the gate artifact; "~23" is a footnote estimate, not a target
**And** `WOmniscienceHackedCharacter` (4-way conjunction + double lookup + non-NV `hackedCharacterClientId`) and `WMarginalIsChainedWin` (zero existing coverage) get the tightest, explicitly-enumerated coverage
**And** each case encodes the **current** verdict as an `Assert`, tagged `[Category("GoldenMaster")]`
**And** vacuously-true empty-list cases are captured as-is but tagged `[Category("VacuousTruth")]`, kept distinct so a zero-cardinality regression in Epic 2 is immediately legible
**And** each PlayMode case is fully isolated (state reconstructed per case, no shared host state)
**And** the wave gate is a **branch-coverage** check over the 4 conditions (every conditional branch exercised by ≥1 golden), not a green-suite or case-count check

### Acceptance reading notes (binding — resolves ambiguity before you start)

1. **Pure characterization on CURRENT code. Test-only. Zero production change.** You call the existing `WinningCondition.CheckCondition()` signature on live NGO state and pin its verdict. Do **not** add a snapshot signature, do **not** create the `Domain` asmdef, do **not** add `CharacterSnapshot`/`GameSnapshot`, do **not** touch any file under `Assets/Scripts/Characters/WinningConditions/`. Those are Stories 1.3 / 1.4 / Epic 2. If you modify a production file, you are off-scope.
2. **The boundary matrix IS the deliverable, the tests implement it.** Annex the matrix (in the test file header AND keep it in sync with the "Boundary matrix" section below). Every row = `(condition, branch/clause, input, current verdict)`. Every row maps to ≥1 `[UnityTest]`. The gate is **branch coverage** (every conditional branch in the 4 conditions hit by ≥1 golden), not "23 tests exist". Under-shooting the count is fine if every branch is covered; over-shooting without covering a branch fails the gate.
3. **"Golden = current verdict as-is, do NOT fix."** §3c step 1 is explicit: *capture current behavior including vacuously-true and surprising cases — do not "fix" them.* Two captured behaviors will look like bugs; they are golden anyway:
   - **`WOmniscienceHackedCharacter` throws `NullReferenceException` when the owner is not found** (`WOmniscienceHackedCharacter.cs:17-18`: `_ownerCharacter.role.powers` with **no null guard**). Contrast `WMarginalIsChainedWin.cs:17` which **does** null-guard the owner and returns `false`. This asymmetry is real and load-bearing. **Recommended:** encode the owner-not-found case for `WOmniscience` as `Assert.Throws<NullReferenceException>(...)`, tagged `[Category("GoldenMaster")]` — so if Epic 2 "accidentally" makes it return `false`, the golden goes red and forces an explicit decision. (Alternative — exclude it as an evaluation precondition — is permitted ONLY if you record the rationale in the test file; the recommended path is to capture the throw.)
   - **A non-fake `Character` with a null `role` throws `NullReferenceException`** in `WChosenChainedAllAnomaly.cs:25` (`_character.role.factionType`). Every non-fake character in a `WChosen` case MUST have a role assigned, or you capture an NRE instead of the branch you meant. This is a setup trap, not a golden to author — assign `role` on every non-fake character.
4. **"Vacuous" has two distinct flavors — tag BOTH `[Category("VacuousTruth")]`, keep them as separate cases:**
   - **Empty population** — `WAnomalyCorruption` / `WChosenChainedAllAnomaly` over zero non-fake characters → `true` (loop never runs).
   - **Empty relevant subset** — `WChosenChainedAllAnomaly` over a population with **no anomaly faction** (all `continue`d) → `true`. This is "all anomalies are chained" vacuously satisfied with zero anomalies. Keep distinct from empty-population so an Epic-2 zero-cardinality regression is legible (AC line: "kept distinct").
5. **Reuse the EXACT Story 1.0 / `VictoryConditionTests` harness. Do not invent a new one.** Story 1.0 already proved THIS harness traverses production logic. A new harness would void that proof. Copy `SetUp`/`TearDown` verbatim (NetworkManager + `UnityTransport` + `StartHost()`, `GameManager` **with** `NetworkObject` + `Spawn()`, `ignoreGameLoop=true` + `DummyGameState`, `CharacterManager` with reflection-injected `_charactersParent`/`_characterPrefab`, `WaitUntilAllSpawnedOrTimeout`, static `instance` reset in `TearDown`).
6. **New `[Category("GoldenMaster")]` and `[Category("VacuousTruth")]` are introduced here.** They join `[Category("HarnessFidelity")]` (1.0) and `[Category("Determinism")]` (1.1) as independently filterable via `run_tests`. NUnit allows multiple `[Category]` on one method — vacuous cases carry both `GoldenMaster` and `VacuousTruth`.

## Tasks / Subtasks

- [x] **Task 0 — Confirm prerequisites are green before capturing any golden** (AC: line 1 "Given")
  - [x] `mcp__UnityMCP__run_tests` `category: HarnessFidelity` (PlayMode) → green (Story 1.0 gate: harness is faithful). If red, **STOP** — goldens captured on an unfaithful harness are worthless. → **2/2 passed.**
  - [x] `mcp__UnityMCP__run_tests` `category: Determinism` (EditMode + PlayMode) → green (Story 1.1: substrate deterministic, `AwakeningState` isolated). If red, **STOP**. → **PlayMode 3/3, EditMode 2/2 passed.**
- [x] **Task 1 — Author the golden-master suite from the boundary matrix** (AC: lines 1, 3, 5)
  - [x] Add `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs` (the `Characters/` subfolder already exists from Story 1.0; namespace `Tests.PlayMode`). Single test class, harness `SetUp`/`TearDown` copied verbatim from `VictoryConditionTests` — same precedent as `WinningConditionHarnessFidelityTests` putting multiple conditions in one fixture.
  - [x] Paste the **Boundary matrix** (below) as a class-level XML-doc / comment block so the case↔branch mapping lives next to the tests (the gate artifact travels with the code).
  - [x] One `[UnityTest] IEnumerator` per matrix row; method name encodes the scenario (`WAnomalyCorruption_ReturnsFalse_WhenLastCharacterNotCorrupted`). Each method: build the live state per the row, then `Assert.IsTrue/IsFalse(condition.CheckCondition(), "...")` encoding the **current** verdict.
  - [x] Tag every method `[Category("GoldenMaster")]`. Add `[Category("VacuousTruth")]` additionally on the two vacuous flavors (reading note 4).
- [x] **Task 2 — `WAnomalyCorruption` coverage** (AC: lines 1, 3, 4)
  - [x] Cover branches: empty non-fake population → `true` (VacuousTruth); single corrupted → `true`; single not-corrupted → `false`; all-of-n corrupted → `true`; **first**-of-n not-corrupted → `false`; **last**-of-n not-corrupted → `false` (early-return position / short-circuit); a **fake** uncorrupted character present alongside corrupted reals → `true` (proves the `!isFake` filter). The two existing `VictoryConditionTests` cases (all-corrupted true / one-not-corrupted false) may be reproduced here under the `GoldenMaster` category — do not delete the originals. → **A1–A7, originals untouched.**
- [x] **Task 3 — `WChosenChainedAllAnomaly` coverage** (AC: lines 1, 3, 4)
  - [x] Cover branches: empty non-fake population → `true` (VacuousTruth, empty population); population of **only non-anomaly factions** → `true` (VacuousTruth, empty relevant subset — exercises the `continue`); one anomaly chained → `true`; one anomaly not-chained → `false`; mix (chosen `continue`d + all anomalies chained) → `true`; **first** anomaly not-chained → `false`; **last** anomaly not-chained → `false`; a **fake** unchained anomaly present alongside chained real anomalies → `true` (filter proof). → **C1–C8.**
  - [x] **Every** non-fake character in every case gets `role = new Role { factionType = ... }` assigned (reading note 3 — null role NREs).
- [x] **Task 4 — `WMarginalIsChainedWin` coverage (tightest — zero prior coverage)** (AC: lines 2, 3)
  - [x] Cover all 3 branches of `WMarginalIsChainedWin.cs:14-23`: owner **not found** (`ownerClientId` with no matching character) → `false`; owner found but **`isFake`** → `false`; owner found, real, `isChained=true` → `true`; owner found, real, `isChained=false` → `false`. → **M1–M4.**
  - [x] Set the condition's owner via `new WMarginalIsChainedWin { ownerClientId = <id> }` (the `ownerClientId` field lives on the base `WinningCondition.cs:13`). For the "fake owner" case, add a character whose id is a fake client id (so `isFake` is true) and point the condition at it.
- [x] **Task 5 — `WOmniscienceHackedCharacter` coverage (tightest — 4-way conjunction + double lookup)** (AC: lines 2, 3)
  - [x] Cover every branch of `WOmniscienceHackedCharacter.cs:15-36`:
    - owner **not found** → **current behavior is `NullReferenceException`** → `Assert.Throws<NullReferenceException>` (reading note 3; recommended path).
    - owner found, **no `POmniscience`** in `role.powers` → `false`.
    - omniscience present, `hackedCharacterClientId == HACKED_CHARACTER_DEFAULT` (4994996541621) → `false`.
    - omniscience present, hacked id set, **hacked character not found** → `false` (double-lookup not-found branch).
    - hacked found, **not chained** → `false` (isChained term).
    - hacked found, chained, **faction != chosen** → `false` (factionType term).
    - hacked found, chained, **faction == chosen** → `true` (the only true verdict). → **O1–O7.**
  - [x] Build the live `POmniscience`: it is a `NetworkBehaviour` (`Power : NetworkBehaviour`, `Power.cs:25`), so it must be **spawned**, not `new`-ed. Recipe in Dev Notes "WOmniscience live-state recipe". Set `hackedCharacterClientId` directly (plain `ulong` field, `POmniscience.cs:14`) and `_ownerCharacter.role.powers.Add(omni)` (list is `readonly` but mutable — `Role.cs:33`). → **`SpawnOmniscience()` helper: bare `NetworkObject` + `POmniscience` + `Spawn()`, awaited; no friction, no fallback needed.**
- [x] **Task 6 — Branch-coverage gate (the real AC, not the case count)** (AC: line 7)
  - [x] Produce a **coverage map**: every conditional branch / early-return / `continue` / clause-side in the 4 conditions ↔ the golden method(s) that exercise it. Record it in the test file header (alongside the matrix). A branch with no golden = gate failure → add the case. → **Coverage map in the class XML-doc header; every branch hit by ≥1 golden.**
  - [x] Sanity: confirm both clause sides ({true,false}) of each boolean read are present, and both double-lookup outcomes (found / not-found) for `WOmniscience`.
- [x] **Task 7 — Verify & gate** (NFR6)
  - [x] `mcp__UnityMCP__read_console` → zero compile errors after `refresh_unity` (poll until `isCompiling=false`). → **0 errors.**
  - [x] `mcp__UnityMCP__run_tests` `category: GoldenMaster` (PlayMode) → green (all current verdicts pinned). → **26/26 passed.**
  - [x] `mcp__UnityMCP__run_tests` full PlayMode suite → no regression (`VictoryConditionTests`, `WinningConditionHarnessFidelityTests`, `VoteInsertionOrderTests`, `AwakeningStateIsolationTests`, `GameManagerTests`, `RoleTests`). → **62/62 passed.**
  - [x] `mcp__UnityMCP__run_tests` full EditMode suite → green (`RolePoolOrderingTests` etc. unaffected). → **62/62 passed.**

## Dev Notes

### What this story is and is NOT

- **IS:** the third Epic 1 prerequisite — the golden-master corpus that freezes the 4 `WinningCondition`s' current verdicts on the faithful (1.0), deterministic (1.1) PlayMode harness. These goldens become the **standing oracle** that Epic 2's strangler migration (2.3–2.6) and the post-cut oracle swap (2.7b) compare against. Test-only.
- **IS NOT:** the `Domain` asmdef (1.3), snapshot value objects (1.4), `VoteTally`/`ChainingResolver` goldens (1.5 — separate corpus), the dual-signature `CheckCondition(snapshot)` (2.2), or any extraction (Epic 2). **No production code is touched. No new asmdef.** You pin the *current* `CheckCondition()` and nothing else.

### Why this story exists (grounded)

> "**Golden masters first, before touching any code.** ~23 boundary cases across the 4 conditions; each test **encodes the current verdict as an `Assert`** (it is both golden and regression), `[Category("GoldenMaster")]`. Capture current behavior as-is, including vacuously-true empty-list cases — do not 'fix' them." [Source: `refactor-architecture-poco.md#3c` step 1]

Story 1.0 proved the harness observes production verdicts; Story 1.1 made the substrate reproducible. This story spends that trust: it photographs every branch of every condition so that when Epic 2 rewrites how conditions are fed (snapshot instead of `GameManager.instance` pull), any drift in verdict trips a named golden with a traceable branch. Without the **branch-coverage** discipline (not case-count), a dead branch could be silently broken by the extraction and no golden would notice — which is exactly the failure 1.0's fidelity gate cannot catch on its own.

### Boundary matrix (the gate artifact — annex into the test file header)

Source code, read before authoring: `Assets/Scripts/Characters/WinningConditions/*.cs` (all four are boolean predicates pulling `GameManager.instance.characterManager`). Population for the two iterating conditions is `GetCharacters(false).Where(_c => !_c.isFake)` (`false` = no end-of-frame coroutine trigger). `Character` NV reads: `isCorrupted` / `isChained` (`Character.cs:21-22`), `isFake => ownerClientId.Value.IsFakeClientId()` (`Character.cs:29`).

**WAnomalyCorruption** — team `anomaly`; reads `isFake` (filter), `isCorrupted.Value`. (`WAnomalyCorruption.cs:19-29`)

| # | Branch / clause | Input | Verdict | Tags |
|---|---|---|---|---|
| A1 | empty population (loop body never runs) | 0 non-fake | `true` | GoldenMaster, **VacuousTruth** |
| A2 | n=1, corrupted | 1 non-fake `isCorrupted=true` | `true` | GoldenMaster |
| A3 | n=1, not corrupted (early return) | 1 non-fake `isCorrupted=false` | `false` | GoldenMaster |
| A4 | n, all corrupted | n `isCorrupted=true` | `true` | GoldenMaster |
| A5 | n, **first** not corrupted (short-circuit position) | [false,true,true] | `false` | GoldenMaster |
| A6 | n, **last** not corrupted | [true,true,false] | `false` | GoldenMaster |
| A7 | `!isFake` filter | fake `isCorrupted=false` + real `isCorrupted=true` | `true` | GoldenMaster |

**WChosenChainedAllAnomaly** — team `chosen`; reads `isFake` (filter), `role.factionType`, `isChained.Value`. `continue` on non-anomaly. (`WChosenChainedAllAnomaly.cs:19-36`)

| # | Branch / clause | Input | Verdict | Tags |
|---|---|---|---|---|
| C1 | empty population | 0 non-fake | `true` | GoldenMaster, **VacuousTruth** |
| C2 | empty relevant subset (all `continue`d) | only `chosen`/`marginal` factions, no anomaly | `true` | GoldenMaster, **VacuousTruth** |
| C3 | n=1 anomaly chained | 1 anomaly `isChained=true` | `true` | GoldenMaster |
| C4 | n=1 anomaly not chained (early return) | 1 anomaly `isChained=false` | `false` | GoldenMaster |
| C5 | mix: `chosen` continued + all anomalies chained | chosen + 2 anomalies chained | `true` | GoldenMaster |
| C6 | **first** anomaly not chained | anomalies [false,true] | `false` | GoldenMaster |
| C7 | **last** anomaly not chained | anomalies [true,false] | `false` | GoldenMaster |
| C8 | `!isFake` filter | fake anomaly `isChained=false` + real anomalies chained | `true` | GoldenMaster |

> Every non-fake character in C* MUST have `role` assigned (`role.factionType` NREs on null role — reading note 3).

**WMarginalIsChainedWin** — team `marginal`; reads `GetCharacter(ownerClientId)`, `isFake`, `isChained.Value`. (`WMarginalIsChainedWin.cs:14-23`) Zero prior coverage → enumerate all branches.

| # | Branch / clause | Input | Verdict | Tags |
|---|---|---|---|---|
| M1 | owner not found (null) → early return | `ownerClientId` matches no character | `false` | GoldenMaster |
| M2 | owner found but `isFake` → early return | owner is a fake-id character | `false` | GoldenMaster |
| M3 | owner real, chained | owner `isChained=true` | `true` | GoldenMaster |
| M4 | owner real, not chained | owner `isChained=false` | `false` | GoldenMaster |

**WOmniscienceHackedCharacter** — team `marginal`; reads `GetCharacter(ownerClientId)` (**no null guard**), `role.powers.Find(POmniscience)`, `omni.hackedCharacterClientId`, `HACKED_CHARACTER_DEFAULT`, `GetCharacter(hackedId)`, `hacked.isChained.Value`, `hacked.role.factionType==chosen`. (`WOmniscienceHackedCharacter.cs:15-36`) Hardest → tightest, one row per guard + both final clause sides.

| # | Branch / clause | Input | Verdict | Tags |
|---|---|---|---|---|
| O1 | owner not found (**no null guard** at `:17-18`) | `ownerClientId` matches no character | **throws `NullReferenceException`** | GoldenMaster |
| O2 | owner found, no `POmniscience` in powers | owner role with empty/other powers | `false` | GoldenMaster |
| O3 | omni present, `hackedCharacterClientId == DEFAULT` | omni never targeted | `false` | GoldenMaster |
| O4 | omni present, hacked id set, hacked **not found** (double-lookup not-found) | `hackedCharacterClientId` points nowhere | `false` | GoldenMaster |
| O5 | hacked found, **not chained** (term: `isChained`) | hacked `isChained=false`, faction `chosen` | `false` | GoldenMaster |
| O6 | hacked found, chained, **faction != chosen** (term: `factionType`) | hacked `isChained=true`, faction `anomaly` | `false` | GoldenMaster |
| O7 | hacked found, chained, **faction == chosen** (the win) | hacked `isChained=true`, faction `chosen` | `true` | GoldenMaster |

**Total ≈ 26 cases** (7+8+4+7). The "~23" in the epic/architecture is a footnote estimate — the binding target is **every branch above hit by ≥1 golden** (Task 6). Adjust counts freely as long as branch coverage holds.

### Harness recipe (copy verbatim from `VictoryConditionTests.cs`)

`SetUp` (lines 32-70) / `TearDown` (72-86) — reproduce exactly; this is the harness Story 1.0 certified:
- NetworkManager + `UnityTransport`, `EnableSceneManagement = false`; a dummy `Character` prefab (`NetworkObject` + `Character`) registered as `NetworkPrefab`; `Assert.IsTrue(StartHost())`.
- `GameManager` GO **with `NetworkObject`** then `GetComponent<NetworkObject>().Spawn()` (the key difference from the broken `GameManagerTests` that 1.0 called out); `ignoreGameLoop = true`; a `DummyGameState` added to `gameStates` to keep the loop inert.
- `CharacterManager` spawned; `_gameManager.characterManager = _characterManager`; `_charactersParent` (a spawned `NetworkObject`) and `_characterPrefab` injected via `ReflectionHelper.SetPrivateField` (`ReflectionHelper.cs`).
- `yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(...)` after spawns; **never `WaitForSeconds`**.
- `TearDown`: `Shutdown()` + bounded wait, then `ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null)` and same for `CharacterManager` (domain reload may be disabled — statics survive PlayMode sessions). Destroy the GOs. **Replicate this or case N+1 inherits a stale singleton.**

Per-case live-state primitives (from existing tests):
- `Character c = _characterManager.AddNewCharacter(<clientId>); yield return WaitUntilAllSpawnedOrTimeout(c);` then host-side NV writes: `c.isCorrupted.Value = ...`, `c.isChained.Value = ...`, `c.role = new Role { factionType = FactionType.anomaly }`.
- A fake character = `AddNewCharacter(<fake client id>)` so `ownerClientId.Value.IsFakeClientId()` is true (see `GameValues.FAKE_CLIENT_ID` / `CharacterManager.CreateNewFakeCharacter`). Confirm the fake-id helper before hardcoding a literal.

### WOmniscience live-state recipe (the hard one)

`POmniscience : Power : NetworkBehaviour` — cannot be `new`-ed; it must be a spawned `NetworkBehaviour`. Minimal faithful setup for O2–O7:
1. Add the owner character: `var owner = _characterManager.AddNewCharacter(1);` await spawn; `owner.role = new Role();`.
2. Spawn a `POmniscience`: instantiate a `GameObject` with `NetworkObject` + `POmniscience` (or reuse the spawn pattern), `Spawn()` it, await. (Mirror how `AddNewCharacter` spawns via `NetworkManager.SpawnManager.InstantiateAndSpawn` / `AddComponent` + `NetworkObject.Spawn()`.)
3. `owner.role.powers.Add(omni);` (`powers` is `readonly List<Power>` — the reference is readonly, the list is mutable; `.Add` is fine).
4. Set the hacked target directly: `omni.hackedCharacterClientId = <hackedId or leave DEFAULT for O3>;` (plain `ulong`, not an NV — `POmniscience.cs:14`).
5. For O4: set `hackedCharacterClientId` to an id with **no** character. For O5–O7: add the hacked character, set `isChained` + `role.factionType` per the row.
6. Point the condition at the owner: `new WOmniscienceHackedCharacter { ownerClientId = owner.ownerClientId.Value };`.
7. For O1 (owner not found): condition `ownerClientId` = an id with no character → `CheckCondition()` dereferences null `_ownerCharacter.role` → `Assert.Throws<NullReferenceException>(() => condition.CheckCondition())`.

If spawning a bare `POmniscience` proves too coupled (e.g. `OnNetworkSpawn` touches `targetValidator`/selection services that need more scene context), document the friction and fall back to the **simplest spawn that makes `role.powers.Find(...)` resolve a live `POmniscience` with a settable `hackedCharacterClientId`** — the golden only needs `CheckCondition()` to traverse the real branches, not the power's selection UI. Do **not** stub `CheckCondition` or reimplement the lookup.

### Condition asymmetry to capture faithfully (do not normalize)

- `WMarginalIsChainedWin` **null-guards** the owner (`:17` `if (_ownerCharacter == null || _ownerCharacter.isFake) return false;`).
- `WOmniscienceHackedCharacter` **does not** null-guard the owner (`:17-18` straight to `_ownerCharacter.role.powers`). Owner-not-found → NRE.
- Capturing both as-is (false vs throws) is the point: a future unification that makes both return `false` is then a *visible, intentional* golden change in Epic 2, not a silent one.

### Relationship to the rest of Epic 1 / Epic 2 (so you don't pull work forward)

- **1.0 (done, `review`)** gave you the faithful harness + the `[Category]` precedent + the mutant-subclass discipline. Reuse the harness; do not re-prove fidelity here.
- **1.1 (done, `review`)** pinned vote order, quarantined `AwakeningState` RNG, froze role-pool order, and **proved no condition reads awakening state** (`AwakeningStateIsolationTests`). That isolation is what makes these goldens safe — do not re-assert it.
- **1.5** golden-masters `VoteTally`/`ChainingResolver` — a **different** corpus; not this story.
- **2.2 / 2.7b** will reuse these exact goldens: 2.2 runs the snapshot-vs-pull differential against the same live states; 2.7b promotes these frozen vectors to the standing oracle after the old signature is cut. Write the cases so they read cleanly as a reusable verdict corpus (clear naming, isolated state) — Epic 2 depends on it.

### Testing standards (binding)

- **PlayMode only** via the existing `StartHost()` harness — the only path to live `NetworkVariable`s [Source: `refactor-architecture-poco.md#3c`]. These conditions pull `GameManager.instance` + NVs → EditMode cannot host them (that is exactly what Epic 2 fixes by extracting POCOs; not now).
- `[UnityTest] IEnumerator` + `yield return NetworkTestHelper.WaitUntil...`. **Never** `WaitForSeconds` / `Thread.Sleep` (CI flakes) [Source: `project-context.md#PlayMode`, `#What NOT to do`].
- Each case isolated, any order; state reconstructed per case by `SetUp`; static `instance` reset in `TearDown` [Source: `project-context.md#Test isolation`].
- `[Category("GoldenMaster")]` on all; `[Category("VacuousTruth")]` additionally on A1/C1/C2; method name = scenario `Condition_ReturnsX_WhenY` [Source: `project-context.md#File layout & naming`].
- No `Debug.Log` left in committed tests [Source: `project-context.md#What NOT to do`].
- `read_console` (compile errors) → `run_tests category:GoldenMaster` → full suite, before declaring done [Source: `CLAUDE.md#Build / Test / Run`].

### Project Structure Notes

- File path mirrors source: conditions in `Assets/Scripts/Characters/WinningConditions/` → test in `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs` (subfolder created by Story 1.0) [Source: `project-context.md#File layout & naming`].
- New type lives in the existing `Tests.PlayMode` asmdef (depends on `Game` + `NetworkTestHelper`). **No new asmdef** [Source: `project-context.md#Assembly definitions`].
- One top-level type per file. If a single fixture grows unwieldy, a shared abstract base fixture (`WinningConditionGoldenTestBase` carrying `SetUp`/`TearDown`) + 4 derived `[TestFixture]` classes is acceptable — but the single-file-multi-method shape matches the `VictoryConditionTests` precedent and is preferred for the first pass [Source: `project-context.md#File layout`].

### Anti-patterns to avoid (would fail review)

- ❌ Editing any file under `Assets/Scripts/Characters/WinningConditions/` (or anywhere in `Assets/Scripts` outside `Tests/`) — this story is **test-only**.
- ❌ Creating the `Domain` asmdef / `CharacterSnapshot` / `GameSnapshot` / a `CheckCondition(snapshot)` overload — Stories 1.3 / 1.4 / Epic 2.
- ❌ "Fixing" a captured behavior: the `WOmniscience` owner-not-found NRE and the vacuous-`true` cases are **goldens**, not bugs to correct (reading notes 3, 4).
- ❌ Targeting a **case count** ("must have 23") instead of **branch coverage** — the gate is every branch hit (Task 6).
- ❌ `new POmniscience()` — it is a `NetworkBehaviour`; it must be spawned. Stubbing/reimplementing the lookup defeats the golden.
- ❌ Forgetting `role` on a non-fake `WChosen` character → captures an NRE instead of the intended branch.
- ❌ `WaitForSeconds` / real-time waits; leaving `Debug.Log`; forgetting the static `instance` reset in `TearDown` → stale singleton → flaky/false-green.
- ❌ Inventing a new harness instead of copying the 1.0-certified one — voids the fidelity proof.

### Project Context Rules

Extracted from `_bmad-output/project-context.md`, scoped to this story:
- **Networked init in `OnNetworkSpawn`** — characters/managers/the `POmniscience` must be `Spawn()`ed and awaited (`WaitUntilAllSpawnedOrTimeout`) before reading NVs, else garbage reads (`#Unity / NGO lifecycle`).
- **`NetworkVariable` server authority** — harness runs as host (server), so directly setting `isCorrupted.Value` / `isChained.Value` is a valid test-only host-side write (matches `VictoryConditionTests`) (`#NGO ownership / authority`).
- **Domain reload may be disabled** — reset static `instance` in `TearDown` (`#Domain reload`, `#Test isolation`).
- **PlayMode test rules** — `NetworkTestHelper`-bounded waits only; route through the established host setup, not a raw `StartHost()` (`#PlayMode (NetworkTestHelper)`).
- **No LINQ-in-hot-path concern here** — these are one-shot test methods, not gameplay hot paths; clarity over micro-optimization.
- **Commits** — Conventional Commits, English, body explaining *why*, no AI attribution. Pure test addition → write a body; `UX:` line may be skipped (no player-facing change — behavior is only photographed) (`CLAUDE.md#Commits`). Suggested: `test(victory): golden-master the 4 WinningConditions from a boundary matrix`.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story 1.2`] — AC verbatim; Epic 1 golden-corpus framing; "boundary matrix is the gate artifact"; tightest coverage for `WOmniscience` + `WMarginal`.
- [Source: `_bmad-output/refactor-architecture-poco.md#3c`] — golden→losslessness→differential→cut; "golden masters first, capture as-is, do not fix"; `[Category("GoldenMaster")]`; ~23 cases footnote.
- [Source: `_bmad-output/refactor-architecture-poco.md#5` risk table] — `hackedCharacterClientId` read from live `POmniscience` (relevant to Epic 2's builder, context here).
- [Source: `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:19-29`] — all non-fake `isCorrupted`.
- [Source: `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:19-36`] — anomaly `isChained`; `continue` on non-anomaly; null-role NRE at `:25`.
- [Source: `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:14-23`] — owner null-guard + `isFake` + `isChained`.
- [Source: `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:15-36`] — 4-way conjunction, double lookup, **no owner null-guard** (NRE), non-NV `hackedCharacterClientId`.
- [Source: `Assets/Scripts/Characters/WinningConditions/WinningCondition.cs:13`] — `ownerClientId` field on the base.
- [Source: `Assets/Scripts/Characters/Powers/POmniscience.cs:14-15`] — `hackedCharacterClientId` plain field; `HACKED_CHARACTER_DEFAULT = 4994996541621`.
- [Source: `Assets/Scripts/Characters/Powers/Power.cs:25`] — `Power : NetworkBehaviour` (must be spawned).
- [Source: `Assets/Scripts/Characters/Role.cs:33`] — `readonly List<Power> powers` (mutable list).
- [Source: `Assets/Scripts/Characters/Character.cs:21-22,29`] — `isChained`/`isCorrupted` NVs; `isFake` derivation.
- [Source: `Assets/Scripts/Characters/CharacterManager.cs:183-201,276-308`] — `GetCharacter`/`GetCharacters`/`AddNewCharacter` (population + spawn pattern).
- [Source: `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`] — harness `SetUp`/`TearDown` + live-state recipes to copy.
- [Source: `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionHarnessFidelityTests.cs`] — Story 1.0 fidelity gate (proves this harness is faithful) + `[Category]` precedent.
- [Source: previous stories `1-0-*.md`, `1-1-*.md`] — harness pattern, mutant discipline, determinism substrate, isolation proof.
- [Source: `_bmad-output/project-context.md`] — PlayMode/test-isolation/domain-reload rules, commit conventions.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story workflow)

### Debug Log References

- Prereq gates (Task 0): HarnessFidelity PlayMode 2/2; Determinism PlayMode 3/3, EditMode 2/2 — all green before any golden captured.
- Category/`test_names`-filtered `run_tests` returned `total:0` immediately after the file was first compiled (the test runner had not yet re-scanned the newly-added assembly). A `refresh_unity` with `scope:all mode:force` followed by an unfiltered run resolved discovery; the `category:GoldenMaster` filter then correctly enumerated all 26. No production or test-code change was needed — purely a discovery-timing artifact.
- Benign per-test console output `No script asset for DummyGameState` originates from the nested `DummyGameState : GameState` (a `ScriptableObject` without its own asset file) — identical to the precedent in `VictoryConditionTests`/`WinningConditionHarnessFidelityTests`. Warning-level only; does not affect verdicts.

### Completion Notes List

- Added a single PlayMode fixture `WinningConditionGoldenMasterTests` (26 `[UnityTest]` cases) pinning the **current** verdict of all four `WinningCondition`s on the Story-1.0-certified `StartHost()` harness (`SetUp`/`TearDown` copied verbatim).
- Boundary matrix + branch-coverage map annexed as the class XML-doc header (the gate artifact travels with the code). Every conditional branch / early-return / `continue` / clause-side across the 4 conditions is hit by ≥1 golden (Task 6 gate satisfied).
- Captured-as-is surprising behaviours, NOT "fixed": `WOmniscienceHackedCharacter` owner-not-found → `Assert.Throws<NullReferenceException>` (O1, no null guard at `:17-18`); the asymmetric `WMarginalIsChainedWin` null-guard → `false` (M1). Two distinct vacuous flavors tagged `[Category("VacuousTruth")]`: empty population (A1, C1) and empty relevant subset / all-`continue`d (C2).
- `WOmniscienceHackedCharacter` live state built via a `SpawnOmniscience()` helper (bare `NetworkObject` + `POmniscience` + `Spawn()`, awaited with `WaitUntilAllSpawnedOrTimeout`) — `POmniscience` is a `NetworkBehaviour` and cannot be `new`-ed. No spawn friction encountered; the documented fallback was not required.
- New independently-filterable categories introduced: `GoldenMaster` (all 26) and `VacuousTruth` (A1/C1/C2). Vacuous cases carry both.
- **Test-only. Zero production code touched** — no file under `Assets/Scripts/` outside `Tests/` was modified; no `Domain` asmdef / snapshot types / `CheckCondition(snapshot)` overload created (Stories 1.3/1.4/Epic 2).
- Final gates: GoldenMaster 26/26 ✓ · full PlayMode 62/62 ✓ · full EditMode 62/62 ✓ · 0 compile errors.

### File List

- `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs` (new)

## Change Log

| Date | Change |
|---|---|
| 2026-06-10 | Story 1.2 drafted: golden-master corpus for the 4 WinningConditions, derived from an annexed branch×condition boundary matrix (~26 cases). Branch-coverage gate, VacuousTruth tagging (two flavors), WOmniscience owner-not-found NRE captured as-is. Test-only, no production change. Status → ready-for-dev. |
| 2026-06-10 | Story 1.2 implemented: added `WinningConditionGoldenMasterTests` (26 PlayMode goldens) with annexed boundary matrix + branch-coverage map. All gates green (GoldenMaster 26/26, full PlayMode 62/62, full EditMode 62/62, 0 compile errors). Test-only. Status → review. |
