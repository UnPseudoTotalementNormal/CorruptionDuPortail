# Story 1.1: Pin and quarantine the non-`Random` non-determinism sources (§3b)

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want the non-`Random` non-determinism sources pinned or quarantined before any golden is captured,
so that golden masters are reproducible and no later wave can turn the gate flaky.

## Acceptance Criteria

**Given** §3b lists three sources beyond `Random`
**When** this story lands
**Then** the vote-insertion order (`VoteState.cs:162-166`, §3b C) is pinned by a test fixing snapshot construction order
**And** the `AwakeningState` frame-timed randomness (`AwakeningState.cs:250`, §3b B) is explicitly flagged and left in PlayMode, ungoldened, with a test/assertion proving **no golden case in Story 1.2 depends on `AwakeningState`** (isolation, not just a note)
**And** the role-pool iteration order (§3b A) is **frozen now** with an explicit, deterministic ordering — the earlier conditional "freeze IF role-attribution is golden-tested later" AC is removed as undecidable at close; freezing now costs almost nothing and removes the latent debt

### Acceptance reading notes (binding — resolves ambiguity before you start)

1. **Three independent deliverables, one per §3b source. Different scopes — do not conflate.**
   - **§3b C (vote order)** → **test-only**, PlayMode. Pin the *current* iteration order. No production change.
   - **§3b B (awakening RNG)** → **test-only + a one-line quarantine marker comment** on `AwakeningState.cs:250`. Isolation test proves the 4 `WinningCondition`s are invariant to awakening state. No logic change.
   - **§3b A (role-pool order)** → the **only production change** in this story: an in-place freeze of the iteration order in `RoleAttributionState`, behavior-preserving to the *authored* `SerializedDictionary` order, plus a determinism test.
2. **§3b A is a freeze, NOT an extraction.** Do **not** create `RoleDistributor`, do **not** touch the `Domain` asmdef (it does not exist yet — that is Story 1.3), do **not** add `IRandomProvider` (Story 3.1). Keep `UnityEngine.Random` exactly where it is. You are only making the *pool ordering* explicit and stable; the random *selection* over that pool is untouched.
3. **"Behavior-preserving" for §3b A is provable.** The current de-facto order **is** the authored `SerializedDictionary` order (see Dev Notes "Why the freeze is behavior-preserving"). Freezing to that order is a no-op on current behavior and only removes the drift risk. You must prove it: capture the authored order, assert the frozen order equals it.
4. **No golden masters here.** Story 1.2 (the 4 `WinningCondition` goldens) and Story 1.5 (`VoteTally`/`ChainingResolver` goldens) are downstream and depend on this story. This story makes the *substrate* deterministic so those goldens are reproducible. Do not pull golden capture forward.
5. **Tag everything `[Category("Determinism")]`** so the three pins are independently filterable via `run_tests` (mirrors the `[Category("HarnessFidelity")]` precedent from Story 1.0). This category is new — introduce it.

## Tasks / Subtasks

- [x] **Task 1 — §3b C: Pin the vote-insertion order (PlayMode, test-only)** (AC: line 3)
  - [x] Add `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/VoteInsertionOrderTests.cs` (mirror source path of `VoteState.cs`; namespace `Tests.PlayMode`). Create the `GameLogic/GameStates/` subfolder under `Tests/PlayMode/` if absent.
  - [x] Reuse the **exact** `VictoryConditionTests` NGO setup pattern (NetworkManager + `UnityTransport` + `StartHost()`, `GameManager` **with** `NetworkObject` + `Spawn()`, `CharacterManager` with reflection-injected `_charactersParent` / `_characterPrefab`, `NetworkTestHelper.WaitUntilAllSpawnedOrTimeout`, static `instance` reset in `TearDown`). Do **not** invent a new harness.
  - [x] Add a known set of non-fake characters in a **deterministic add order** (e.g. clientIds 1, 2, 3 via `_characterManager.AddNewCharacter(n)`); await spawn.
  - [x] Assert that `gameManager.characterManager.GetCharacters().Where(c => !c.isFake)` returns characters in the **same order they were added** — i.e. `networkedCharacters` (NetworkList) insertion/spawn order (see Dev Notes "Why vote order = spawn order"). This is the contract `VoteTally` (Story 2.9) must respect.
  - [x] Assert that the `votesForPlayer` **key order** built in `VoteState.OnStartStateServer()` (`VoteState.cs:161-166`) follows that same character order, with `SKIP_VOTE_ID` appended last. Prefer driving the real `OnStartStateServer()` over re-implementing the loop; if booting a full `VoteState` is too coupled, assert the equivalent ordering directly off `GetCharacters()` and document that `VoteState.cs:162` consumes exactly this sequence. — *Drove the real `OnStartStateServer()` (registered `VoteState` in `gameStates` so the internal RPC dispatch resolves).* 
  - [x] Tag `[Category("Determinism")]`.
- [x] **Task 2 — §3b B: Quarantine the AwakeningState frame-timed RNG (flag + isolation test)** (AC: line 4)
  - [x] Add a single, greppable quarantine marker comment immediately above `AwakeningState.cs:250` (the `Random.Range(0.0f, 1.0f)` call inside `StateUpdateServer`), e.g. `// [DETERMINISM-QUARANTINE §3b B] Frame-timed RNG: call count depends on framerate. Out of scope for Phase 0 / Wave 1 — needs IGameClock + seed isolation (Wave). MUST NOT feed any golden.` Do **not** change the logic.
  - [x] Add `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/AwakeningStateIsolationTests.cs`.
  - [x] **Isolation proof:** using the `VictoryConditionTests` harness, build a live state and assert that each of the 4 `WinningCondition`s' verdict is **invariant** to awakening state — vary `Character.isAwakened.Value` (and confirm no awakening timer/field is read by any condition) and assert `CheckCondition()` is unchanged for `WAnomalyCorruption`, `WChosenChainedAllAnomaly`, `WMarginalIsChainedWin`, `WOmniscienceHackedCharacter`. This proves Story 1.2 goldens cannot depend on `AwakeningState`. — *Verdicts captured at `isAwakened=false`, asserted invariant after toggling ON then back OFF (both directions).* 
  - [x] Add a documented gate note (class XML doc): *if this test ever goes red, a `WinningCondition` has started reading awakening state and the §3b B quarantine is breached — Story 1.2 golden capture is no longer safe until re-isolated.*
  - [x] Tag `[Category("Determinism")]`.
- [x] **Task 3 — §3b A: Freeze the role-pool iteration order (production change, behavior-preserving)** (AC: line 5)
  - [x] In `RoleAttributionState.cs`, replace the order-fragile pool handling with an **explicit frozen ordering** derived from the authored `SerializedDictionary` order:
    - At `:32`, build the working pool preserving authored order (the `SerializedDictionary` enumerates in serialized/authored order — capture that as the canonical sequence; do not let a plain `Dictionary` + later `.Remove()` define the order). — *Kept the working `Dictionary` for value lookups/removals; the canonical order is now sourced from the frozen accessor, not the dictionary's key order.*
    - [x] At `:85`, replace `_rolesToAttribute.Keys.ToList()[_randomRoleIndex]` with indexing into the frozen ordered sequence **filtered to still-available roles, preserving relative order** (no `Dictionary.Keys.ToList()` drift). The `Random.Range(0, count)` selection stays exactly as-is — only the list it indexes into is now order-stable. — *`GiveRandomRole` now indexes `GetFrozenRolePoolOrder().Where(_rolesToAttribute.ContainsKey)`; `Random.Range(0, count)` unchanged (count identical).*
  - [x] Expose the frozen ordering for testing via an `internal` accessor (e.g. `internal IReadOnlyList<RoleDataObject> GetFrozenRolePoolOrder()` computed from `roleAttributionDictionary`), guarded by `[assembly: InternalsVisibleTo("Tests.Editor")]` if not already present. **Do not widen to `public`** and **do not** add a `TEST_ForceState` backdoor. — *Added `Assets/Scripts/AssemblyInfo.cs` with `InternalsVisibleTo("Tests.Editor")`.*
  - [x] Add `Assets/Scripts/Tests/Editor/GameLogic/GameStates/RolePoolOrderingTests.cs` (EditMode — the ordering is pure over the dictionary; no NGO boot needed). Construct a `RoleAttributionState` via `ScriptableObject.CreateInstance<>()`, populate `roleAttributionDictionary` with a few `ScriptableObject.CreateInstance<RoleDataObject>()` entries in a known order.
  - [x] **Behavior-preservation proof:** assert `GetFrozenRolePoolOrder()` equals the authored insertion order of the `SerializedDictionary`, and is **stable across repeated calls** and **stable after a simulated removal** of a middle entry (relative order of survivors preserved). Tag `[Category("Determinism")]`.
- [x] **Task 4 — Verify & gate** (NFR6)
  - [x] `mcp__UnityMCP__read_console` → zero compile errors after each change (poll after `refresh_unity`). — *0 errors (after adding the `AYellowpaper.SerializedCollections` ref to `Tests.Editor`).* 
  - [x] `mcp__UnityMCP__run_tests` `category: Determinism` (both EditMode + PlayMode) → green. — *EditMode 2/2, PlayMode 3/3.*
  - [x] `mcp__UnityMCP__run_tests` full PlayMode suite → no regression (esp. `VictoryConditionTests`, `RoleTests`, `WinningConditionHarnessFidelityTests`, `GameManagerTests`). — *36/36 passed.*
  - [x] `mcp__UnityMCP__run_tests` full EditMode suite → green. — *62/62 passed.*

## Dev Notes

### What this story is and is NOT

- **IS:** the determinism substrate for Epic 1's goldens. Three independent §3b pins: two test-only (C, B) + one tightly-scoped behavior-preserving production freeze (A). It is the **second** Epic 1 prerequisite, after the harness-fidelity gate (Story 1.0, now `review`).
- **IS NOT:** golden masters (1.2 / 1.5), the `Domain` asmdef (1.3), snapshot value objects (1.4), `IRandomProvider` (3.1), or `RoleDistributor` (3.3). No `Domain` asmdef exists yet — do not create or reference one. No snapshot types. The `WinningCondition` refonte is Epic 2.

### Why this story exists (grounded)

> "**Non-deterministic beyond `Random`** — `CharacterManager` uses `UnityEngine.Random`, but there are **three further** non-determinism sources" [Source: `refactor-architecture-poco.md#1`]. "Three additional non-determinism sources must be pinned/quarantined, **or golden masters are worthless**" [Source: `refactor-architecture-poco.md#3b`].

Story 1.0 proved the harness is faithful. This story proves the substrate is reproducible, so the 1.2 / 1.5 goldens captured on that harness are stable run-to-run and cannot be silently invalidated by a later wave. Without it, a golden could pass locally and flake in CI, or rot the moment Epic 3 touches role attribution.

### §3b C — vote-insertion order (test-only)

`VoteState.OnStartStateServer()` (`VoteState.cs:158-179`):
```csharp
votesForPlayer.Clear();
foreach (var _character in gameManager.characterManager.GetCharacters().Where(_c => !_c.isFake))
    votesForPlayer.Add(_character.ownerClientId.Value, new List<ulong>());
votesForPlayer.Add(SKIP_VOTE_ID, new List<ulong>());
```
`votesForPlayer` insertion order == `GetCharacters()` iteration order, with `SKIP_VOTE_ID` last.

**Why vote order = spawn order:** `GetCharacters()` returns `new List<Character>(_characters)` (`CharacterManager.cs:200`). `_characters` is rebuilt by iterating `networkedCharacters` (a `NetworkList<NetworkBehaviourReference>`) **in list order** (`CharacterManager.cs:81-100`). `networkedCharacters` is appended server-side in `AddNewCharacter` / `CreateNewFakeCharacter` (`networkedCharacters.Add(...)`, `CharacterManager.cs:300`). So the order is **server-side spawn/add order** — deterministic for a given add sequence. The test pins exactly this: add in a known order → assert `GetCharacters()` (non-fake) preserves it → assert `votesForPlayer` keys follow it. This becomes `VoteTally`'s ordering contract in Story 2.9 [Source: `epics.md#Story 2.9`; `refactor-architecture-poco.md#3b` C].

### §3b B — AwakeningState frame-timed RNG (quarantine)

`AwakeningState.StateUpdateServer()` (`AwakeningState.cs:232-256`) calls `Random.Range(0.0f, 1.0f)` per fake-awakened character **every server frame** while `currentAwakeningTimer <= currentAwakeningMaxTime / 1.25f`. The number of RNG draws before a transition depends on framerate → unreproducible. Architecture verdict: **out of scope for Phase 0 / Wave 1; needs an `IGameClock` + seed isolation; leave in PlayMode, ungoldened, flagged** [Source: `refactor-architecture-poco.md#3b` B, `#2` Ports `IGameClock`].

**Isolation, not just a note (binding AC).** None of the 4 conditions read awakening state — verify against the source map below and *prove* it with the invariance test. The condition reads are: `isCorrupted` (`WAnomalyCorruption`), `factionType`+`isChained` (`WChosenChainedAllAnomaly`), `isFake`+`isChained` (`WMarginalIsChainedWin`), `hackedCharacterClientId`+`factionType` (`WOmniscienceHackedCharacter`). `isAwakened` and the awakening timers appear in **none** of them. Toggling `isAwakened` and asserting verdict-invariance is the bite that catches a future regression where a condition starts depending on awakening state.

### §3b A — role-pool order freeze (the only production change)

Current code (`RoleAttributionState.cs`):
- `:32` `roleAttributionDictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` — converts the `SerializedDictionary` to a plain `Dictionary<,>`.
- `:85` `_rolesToAttribute.Keys.ToList()[_randomRoleIndex]` — indexes into `Dictionary` key order; `Random.Range(0, _rolesToAttribute.Count)` picks the index.
- Removals happen at `:39`, `:60`, `:109`.

**The fragility:** "A seeded `Random` over a list whose order can drift (asset reload, `SerializedDictionary` order) is still non-deterministic" [Source: `refactor-architecture-poco.md#3b` A]. `Dictionary.Keys` order is implementation-defined and can shift after removals/reinsertions.

**Why the freeze is behavior-preserving:** `AYellowpaper.SerializedCollections.SerializedDictionary` enumerates in its **serialized (authored) order**. A CLR `Dictionary<,>` built from that ordered sequence preserves insertion order for an add-only fill, and the current flow does not re-add after removing. So the de-facto current order **is** the authored order. Freezing to an explicit authored-order list is a no-op on today's behavior and only removes the drift risk. **You must prove this** (Task 3 behavior-preservation assertions), not assume it.

**No existing test breaks:** `RoleTests.cs` tests `Role.AwakenRole`/`SleepRole`, **not** `RoleAttributionState` attribution. No test in the suite asserts attribution outcomes, so the freeze has no golden/test to violate today. Story 3.2 will golden the post-freeze assignment under a fixed seed [Source: `epics.md#Story 3.2`].

> ⚠️ **Coverage gap to respect.** Per the quota-aware code-review policy, this story is **not** tagged `# REVIEW-REQUIRED`, and there is **no golden on role attribution until Story 3.2**. The behavior-preservation proof in Task 3 (authored-order equality + stability) is therefore your *only* safety net for the production change — make it tight. If you discover the de-facto order is **not** the authored order (proof fails), **stop and surface it** rather than silently changing assignments.

### Condition source map (for the §3b B isolation test)

[Source: `Assets/Scripts/Characters/WinningConditions/*.cs`; previous story `1-0` Dev Notes]
- `WAnomalyCorruption.cs:19-29` — all non-fake `isCorrupted.Value`.
- `WChosenChainedAllAnomaly.cs:19-36` — anomaly non-fake `isChained.Value`.
- `WMarginalIsChainedWin.cs:14-23` — owner lookup; `isFake` → false, else `isChained.Value`.
- `WOmniscienceHackedCharacter.cs:15-36` — 4-way conjunction, double lookup, non-NV `hackedCharacterClientId`.
- All pull `GameManager.instance` + `NetworkVariable`s → PlayMode harness required. None read awakening state.

### Testing standards (binding)

- **PlayMode** (§3b C, §3b B) via the existing `StartHost()` harness — the only path to live `NetworkVariable`s [Source: `refactor-architecture-poco.md#3c`]. Copy `VictoryConditionTests` setup/teardown verbatim; reset static `instance` in `TearDown` (domain reload may be disabled — statics survive across sessions) [Source: `project-context.md#Domain reload`, `#Test isolation`].
- **EditMode** (§3b A ordering) — the frozen ordering is pure over the dictionary; test without booting NGO [Source: `project-context.md#EditMode vs PlayMode`]. Expose internals via `InternalsVisibleTo("Tests.Editor")`, never widen `public` [Source: `project-context.md#Encapsulation`].
- `[UnityTest] IEnumerator` + `yield null` / `NetworkTestHelper` bounded waits. **Never** `WaitForSeconds` / `Thread.Sleep` (CI flakes) [Source: `project-context.md#PlayMode`, `#What NOT to do`].
- Each test isolated, any order; reconstruct state per test [Source: `project-context.md#Test isolation`].
- Tag `[Category("Determinism")]`; method name = scenario (`Foo_Bar_WhenQux`) [Source: `project-context.md#File layout & naming`].
- `read_console` (compile errors) → targeted `run_tests` → full suite, before declaring done [Source: `CLAUDE.md#Build / Test / Run`].

### Project Structure Notes

- File path mirrors source [Source: `project-context.md#File layout & naming`]:
  - `VoteState.cs` (`GameLogic/GameStates/`) → `Tests/PlayMode/GameLogic/GameStates/VoteInsertionOrderTests.cs`.
  - `AwakeningState.cs` (`GameLogic/GameStates/`) → `Tests/PlayMode/GameLogic/GameStates/AwakeningStateIsolationTests.cs`.
  - `RoleAttributionState.cs` (`GameLogic/GameStates/`) → `Tests/Editor/GameLogic/GameStates/RolePoolOrderingTests.cs`.
  - `Tests/PlayMode/` precedent: Story 1.0 created `Tests/PlayMode/Characters/`. Subfolders are accepted/preferred.
- PlayMode tests go in the existing `Tests.PlayMode` asmdef; EditMode test in `Tests.Editor`. **No new asmdef** [Source: `project-context.md#Assembly definitions`].
- One top-level type per file; mutant/helper subclasses are `private`/`sealed` nested [Source: `project-context.md#File layout`].

### Anti-patterns to avoid (would fail review)

- ❌ Extracting `RoleDistributor` / creating `Domain` asmdef / adding `IRandomProvider` — out of scope (Epics 1.3 / 3). This story **freezes in place** only.
- ❌ Changing the `Random.Range` *selection* logic in §3b A — only the ordered list it indexes into changes.
- ❌ Sorting the pool by name/id or any order **other than authored `SerializedDictionary` order** — that would change behavior. Authored order is the contract.
- ❌ Touching `AwakeningState` logic for §3b B — it is quarantined (flagged), not fixed, in this story.
- ❌ A `TEST_ForceState` backdoor or `public` widening to expose the pool order — use `internal` + `InternalsVisibleTo` [Source: `project-context.md#Testability constraints`].
- ❌ `WaitForSeconds` / real-time waits; leaving `Debug.Log` in committed tests; forgetting the static `instance` reset in PlayMode `TearDown`.
- ❌ Capturing any golden master here (1.2 / 1.5 job).

### Project Context Rules

Extracted from `_bmad-output/project-context.md`, scoped to this story:
- **Networked init in `OnNetworkSpawn`** — characters/managers must be `Spawn()`ed and awaited (`WaitUntilAllSpawnedOrTimeout`) before reading `NetworkVariable`s, else garbage reads (`#Unity / NGO lifecycle`).
- **`NetworkVariable` server authority** — harness runs as host (server), so directly setting `isCorrupted.Value` / `isAwakened.Value` is a valid test-only host-side write (matches `VictoryConditionTests`).
- **Domain reload may be disabled** — reset static `instance` in `TearDown` (`#Domain reload`, `#Test isolation`).
- **ScriptableObject runtime mutation persists to disk in Editor** — for the §3b A EditMode test, use freshly `CreateInstance<>()`'d throwaway SOs; never mutate a project asset (`#ScriptableObject runtime mutation`).
- **No LINQ in hot paths** — `RoleAttributionState.OnStartStateServer` is a one-shot state entry, **not** a per-frame hot path, so the existing LINQ there is acceptable; keep the freeze allocation-light but do not over-optimize (`#Hot path`).
- **Encapsulation** — `internal` + `InternalsVisibleTo`, never `public` for testability (`#Encapsulation`).
- **Commits** — Conventional Commits, English, body explaining *why*, no AI attribution. Mixed test + small production change → write a body; a `UX:` line may be skipped (no player-facing change — behavior is preserved by proof) (`CLAUDE.md#Commits`). Suggested: `test(determinism): pin vote order + quarantine awakening RNG; refactor(roles): freeze role-pool iteration order` — or split into two commits (one per concern) since §3b A is production and §3b B/C are test-only.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story 1.1`] — AC verbatim; Epic 1 determinism-substrate framing.
- [Source: `_bmad-output/refactor-architecture-poco.md#3b`] — the three sources A/B/C, "or golden masters are worthless".
- [Source: `_bmad-output/refactor-architecture-poco.md#1`] — non-determinism beyond `Random`; line refs.
- [Source: `_bmad-output/refactor-architecture-poco.md#2`] — `IGameClock` port (awakening, out of scope here); `IRandomProvider` (Story 3.1).
- [Source: `_bmad-output/refactor-architecture-poco.md#3c`] — golden→losslessness→differential strategy that depends on this substrate.
- [Source: `Assets/Scripts/GameLogic/GameStates/VoteState.cs:158-179`] — `votesForPlayer` insertion loop (§3b C).
- [Source: `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:232-256`] — frame-timed `Random.Range` (§3b B).
- [Source: `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:29-112`] — pool build + `GiveRandomRole` index selection (§3b A).
- [Source: `Assets/Scripts/Characters/CharacterManager.cs:69-100,188-201,300`] — `_characters` rebuild from `networkedCharacters` order (proves vote order = spawn order).
- [Source: `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`] — harness setup/teardown + live-state recipes to copy.
- [Source: `Assets/Scripts/Tests/PlayMode/RoleTests.cs`] — confirms no existing test asserts role attribution (freeze is safe).
- [Source: previous story `_bmad-output/implementation-artifacts/1-0-*.md`] — harness-fidelity pattern, `[Category("...")]` precedent, mutant-subclass discipline.
- [Source: `_bmad-output/project-context.md`] — testing rules, domain reload, no-backdoor, SO mutation, commit conventions.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow).

### Debug Log References

- Initial compile: `RolePoolOrderingTests.cs` CS0012 — `SerializedDictionary<,>` type not referenced by the `Tests.Editor` assembly. Touching `roleAttributionDictionary` (a `SerializedDictionary`) forces a metadata reference. Fixed by adding `AYellowpaper.SerializedCollections` to `Tests.Editor.asmdef` references; recompile → 0 errors.

### Completion Notes List

- **§3b C (vote order, test-only):** Pinned in `VoteInsertionOrderTests`. Two `[UnityTest]`s: (1) `GetCharacters().Where(!isFake)` preserves server-side add order (1,2,3); (2) drove the **real** `VoteState.OnStartStateServer()` (registered `VoteState` in `gameStates` so the internal `DoStateMethodRpc` dispatch resolves) and asserted `votesForPlayer` key order == [1,2,3,`SKIP_VOTE_ID`] with SKIP last. No production change.
- **§3b B (awakening RNG, quarantine):** Added the greppable `[DETERMINISM-QUARANTINE §3b B]` marker above the frame-timed `Random.Range` in `AwakeningState.StateUpdateServer` (no logic change). `AwakeningStateIsolationTests` proves all 4 `WinningCondition`s are verdict-invariant to `Character.isAwakened` (captured baseline at false; toggled ON then back OFF — both directions). Class XML doc carries the documented gate note.
- **§3b A (role-pool freeze, the only production change):** `RoleAttributionState` now exposes `internal IReadOnlyList<RoleDataObject> GetFrozenRolePoolOrder()` = the authored `SerializedDictionary` key order. `GiveRandomRole` indexes `GetFrozenRolePoolOrder().Where(_rolesToAttribute.ContainsKey)` instead of `_rolesToAttribute.Keys.ToList()`; `Random.Range(0, count)` is unchanged (count identical). Behavior-preserving and **proven** by `RolePoolOrderingTests` (authored-order equality, stability across calls, survivor relative-order preserved after middle removal). Internals exposed via new `AssemblyInfo.cs` `InternalsVisibleTo("Tests.Editor")` — no `public` widening, no `TEST_` backdoor.
- **Verification:** 0 compile errors. Determinism category green (EditMode 2/2, PlayMode 3/3). Full regression green: EditMode 62/62, PlayMode 36/36 (incl. `VictoryConditionTests`, `RoleTests`, `WinningConditionHarnessFidelityTests`, `GameManagerTests`). No golden captured (out of scope — 1.2 / 1.5).

### File List

- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` — **modified** (§3b A: `GetFrozenRolePoolOrder()` accessor + order-stable selection in `GiveRandomRole`).
- `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs` — **modified** (§3b B: quarantine marker comment only).
- `Assets/Scripts/AssemblyInfo.cs` — **added** (`InternalsVisibleTo("Tests.Editor")` for the Game assembly).
- `Assets/Scripts/Tests/Editor/Tests.Editor.asmdef` — **modified** (added `AYellowpaper.SerializedCollections` reference).
- `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/VoteInsertionOrderTests.cs` — **added** (§3b C pin).
- `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/AwakeningStateIsolationTests.cs` — **added** (§3b B isolation proof).
- `Assets/Scripts/Tests/Editor/GameLogic/GameStates/RolePoolOrderingTests.cs` — **added** (§3b A behavior-preservation proof).
- (Unity-generated `.meta` files for the new files/folders.)

## Change Log

- 2026-06-10 — Story 1.1 implemented: pinned §3b C vote-insertion order (test-only), quarantined §3b B awakening frame-timed RNG (marker + isolation proof), froze §3b A role-pool iteration order to authored `SerializedDictionary` order (behavior-preserving production change). New `[Category("Determinism")]`. All gates green; status → review.
