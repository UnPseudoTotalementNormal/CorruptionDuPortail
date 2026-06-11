# Story 1.0: Prove the PlayMode harness is faithful before trusting any golden

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want a one-shot mutation kill-test proving the existing `StartHost()` harness actually exercises the production `WinningCondition` code path,
so that golden masters are captured on a faithful harness instead of certifying a blurry photo as ground truth.

## Acceptance Criteria

**Given** the current PlayMode harness (`VictoryConditionTests`, `NetworkTestHelper`), known to be thin and to contain tests that pass for the wrong reasons (e.g. a `GameManager` added without a `NetworkObject`)
**When** one `WinningCondition`'s verdict is deliberately inverted at the source (e.g. flip a `>` to `<`)
**Then** the harness-driven test for that condition **must turn red**
**And** if it stays green, the harness does not traverse the code under test and Epic 1 is blocked until the harness is fixed
**And** this kill-test is a documented, repeatable gate, not a throwaway check

### Acceptance reading notes (binding — resolves the AC's illustrative wording)

1. **"flip a `>` to `<`" is illustrative, not literal.** The 4 conditions are **boolean predicates**, not numeric comparisons (verified — see Dev Notes "Condition source map"). The faithful mutation is a **boolean verdict inversion** (e.g. `return true` ↔ `return false`, or drop the `!` in `WAnomalyCorruption.cs:23`).
2. **"inverted at the source" reconciled with "repeatable gate, not throwaway".** A literal source edit is not a permanent, CI-repeatable artifact, and a production mutation flag is **forbidden** by project-context ("No `TEST_ForceState` backdoors"). Therefore the deliverable is a **mutation meta-test** living in `Tests.PlayMode` that inverts a condition's verdict via a **test-only mutant subclass** (`public override bool CheckCondition() => !base.CheckCondition();`). `base.CheckCondition()` executes the **real production logic** verbatim, so the inversion proves the harness assertion actually observes the production-computed verdict. (A one-shot manual source flip MAY be done during dev as an extra sanity check, but the committed gate is the meta-test.)
3. **"must turn red" is encoded as a passing assertion.** The meta-test is **green** when it proves the harness assertion would **fail** against the mutant. Concretely: on a live state where the real condition's verdict is known, assert the real condition returns that verdict (positive control = harness traverses code), AND assert that the golden-style assertion against the **mutant** throws `AssertionException` (the net bites). A meta-test that itself goes red means the harness is **not faithful** → Epic 1 is blocked (AC line 4).
4. **Scope:** at least one currently-harnessed condition must be kill-tested. `WAnomalyCorruption` is the recommended target (simplest, already has live-state setup). Covering the second currently-harnessed condition (`WChosenChainedAllAnomaly`) is a SHOULD, not a blocker. Full boundary coverage of all 4 conditions is **Story 1.2's** job — do **not** pull it forward here.

## Tasks / Subtasks

- [x] **Task 1 — Author the mutation kill-test (meta-test) in PlayMode** (AC: all)
  - [x] Add `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionHarnessFidelityTests.cs` (mirror source path of the conditions under test; namespace `Tests.PlayMode`). See Dev Notes "File layout" — confirm/create the `Characters/` subfolder under `Tests/PlayMode/`.
  - [x] Reuse the **exact** `VictoryConditionTests` NGO setup pattern (NetworkManager + UnityTransport + `StartHost()`, `GameManager` **with** `NetworkObject` + `Spawn()`, `CharacterManager` with reflection-injected `_charactersParent` / `_characterPrefab`, `NetworkTestHelper.WaitUntilAllSpawnedOrTimeout`). Do **not** invent a new harness — the point is to test the harness that the goldens (Story 1.2 / 1.5) will use.
  - [x] Define a **test-only mutant subclass** per kill-tested condition, e.g. `private sealed class MutantAnomalyCorruption : WAnomalyCorruption { public override bool CheckCondition() => !base.CheckCondition(); }`. No production file is modified.
  - [x] Tag the test `[Category("HarnessFidelity")]` so it is independently filterable via `run_tests`.
- [x] **Task 2 — Positive control: prove the harness traverses production code** (AC: line 1, 3)
  - [x] Build a live NGO state with a deterministic, known verdict (e.g. 2 characters both `isCorrupted.Value = true` → `WAnomalyCorruption.CheckCondition()` MUST be `true`).
  - [x] Assert the **real** condition returns the known verdict. If this fails, the setup never reaches production logic (e.g. `GameManager.instance` null, characters not spawned) → harness is broken, surface it.
- [x] **Task 3 — Kill assertion: prove the net bites** (AC: line 3, 4)
  - [x] On the **same** live state, assert `Assert.Throws<AssertionException>(() => Assert.IsTrue(mutant.CheckCondition(), "..."))` — i.e. the golden-style assertion against the inverted verdict fails.
  - [x] Add a `[Test]`/comment-level note (the "documented gate") stating: *if this meta-test ever goes red, the harness does not faithfully traverse the condition under test and Epic 1 golden capture (1.2 / 1.5) is blocked until fixed.*
- [x] **Task 4 — (SHOULD) Second condition** (AC: line 5, scope note 4)
  - [x] Repeat Tasks 2–3 for `WChosenChainedAllAnomaly` (set `role.factionType` + `isChained.Value` per the existing `VictoryConditionTests` pattern, lines 117–148).
- [x] **Task 5 — Verify & gate**
  - [x] `mcp__UnityMCP__read_console` → zero compile errors.
  - [x] `mcp__UnityMCP__run_tests` with `testMode: PlayMode`, `category: HarnessFidelity` → green.
  - [x] Run full PlayMode suite (`testMode: PlayMode`) → no regression in `VictoryConditionTests` / `GameManagerTests`.

## Dev Notes

### What this story is and is NOT

- **IS:** a single, permanent, repeatable PlayMode meta-test that proves the `StartHost()` harness observes the **production** `WinningCondition.CheckCondition()` output. It is the **first blocking gate of Epic 1** (`epics.md` Epic 1 summary: "Blocking gates: harness fidelity (1.0)").
- **IS NOT:** golden masters of the conditions (Story 1.2), determinism pinning (Story 1.1), the Domain asmdef (1.3), snapshot value objects (1.4), or `VoteTally`/`ChainingResolver` goldens (1.5). **Touch no production code.** No new asmdef. No snapshot types. Inverting verdicts via a mutant subclass in the test assembly only.

### Why this gate exists (grounded)

The refactor's safety net is golden/differential tests captured on the **existing** PlayMode `StartHost()` setup — "it is the only way to get live `NetworkVariable`s" [Source: `_bmad-output/refactor-architecture-poco.md#3c`]. But the current suite is thin and **some tests pass for the wrong reasons**: `GameManagerTests` adds `GameManager` **without a `NetworkObject` and never `Spawn()`s it** (`GameManagerTests.cs:38-40`), so it captures no network regression [Source: `refactor-architecture-poco.md#1`]. If the victory harness is similarly hollow, every golden built on it is "a blurry photo certified as ground truth." This story proves the victory harness is **not** hollow before 1.2/1.5 trust it.

### The harness under test (read before writing)

`VictoryConditionTests` (`Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`) is the faithful-looking reference setup — **copy its pattern**:
- `SetUp` (lines 32-70): NetworkManager + `UnityTransport`, `EnableSceneManagement = false`; a dummy `Character` prefab registered as a `NetworkPrefab`; `StartHost()` asserted; `GameManager` created **with `NetworkObject` + `Spawn()`** (line 51-56) — this is the key difference from the broken `GameManagerTests`; `CharacterManager` spawned; private fields `_charactersParent` / `_characterPrefab` injected via `ReflectionHelper.SetPrivateField` (lines 66-67); `ignoreGameLoop = true` (line 53) and a `DummyGameState` added (line 54-55) to keep the loop inert.
- `TearDown` (lines 72-86): `Shutdown()` + bounded wait, then **resets the `GameManager` / `CharacterManager` static `instance` to null via reflection** (lines 78-79) — load-bearing because domain reload may be disabled (statics survive across PlayMode sessions) [Source: `project-context.md#Domain reload`]. Replicate this teardown or your second test inherits a stale singleton.
- Existing live-state recipes you can reuse verbatim: `WAnomalyCorruption` true/false (lines 88-114), `WChosenChainedAllAnomaly` true/false (lines 116-148).

`NetworkTestHelper` (`Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs`): bounded spawn waits — `WaitUntilAllSpawnedOrTimeout(...)`, never `WaitForSeconds` [Source: `project-context.md#PlayMode`].

`ReflectionHelper` (`Assets/Scripts/ReflectionHelper.cs`): `SetPrivateField(instance, name, value)` and `SetPrivateField(type, staticName, value)` — used for `_charactersParent`, `_characterPrefab`, and the static `instance` reset.

### Condition source map (why the mutation is boolean, not `>`/`<`)

All 4 conditions are boolean predicates pulling `GameManager.instance` (the exact coupling this whole refactor removes). None contain a numeric comparison to flip:
- `WAnomalyCorruption.cs:19-29` — `foreach` characters, `if (!_character.isCorrupted.Value) return false;` else `true`. **Recommended kill target.** Mutant inverts final verdict.
- `WChosenChainedAllAnomaly.cs:19-36` — for non-fake characters, skip non-anomaly, `if (!isChained.Value) return false;` else `true`.
- `WMarginalIsChainedWin.cs:14-23` — owner lookup, null/`isFake` → false, else `isChained.Value`.
- `WOmniscienceHackedCharacter.cs:15-36` — 4-way conjunction + double lookup + non-NV `hackedCharacterClientId`. **Not** in scope here (no current harness test; hardest case, golden-ed in 1.2 / migrated in 2.6).
- Base `WinningCondition.cs:11-23` — `abstract bool CheckCondition()`, `INetworkSerializable`. The override in each concrete class is **not sealed** → a test subclass can override it. The mutant calls `base.CheckCondition()` (real logic) and negates.

> Faithful-mutation principle: the mutant must run the **production** computation and only invert the result. Do **not** reimplement the condition logic in the mutant — that would test the test, not the harness.

### Recommended test shape (illustrative — adapt to existing style)

```csharp
// Assets/Scripts/Tests/PlayMode/Characters/WinningConditionHarnessFidelityTests.cs
[Category("HarnessFidelity")]
public class WinningConditionHarnessFidelityTests
{
    // ... SetUp / TearDown copied from VictoryConditionTests ...

    private sealed class MutantAnomalyCorruption : WAnomalyCorruption
    {
        // Traverses production logic, inverts only the verdict.
        public override bool CheckCondition() => !base.CheckCondition();
    }

    [UnityTest]
    public IEnumerator Harness_TraversesProductionVerdict_AndCatchesInversion()
    {
        Character c1 = _characterManager.AddNewCharacter(1);
        Character c2 = _characterManager.AddNewCharacter(2);
        yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);
        c1.isCorrupted.Value = true;
        c2.isCorrupted.Value = true;

        // Positive control: harness actually reaches production logic.
        Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
            "Harness did not traverse production WAnomalyCorruption — golden capture is unsafe.");

        // Kill assertion: the harness assertion bites on an inverted verdict.
        Assert.Throws<AssertionException>(
            () => Assert.IsTrue(new MutantAnomalyCorruption().CheckCondition()),
            "Harness assertion did NOT catch an inverted verdict — harness is hollow, Epic 1 blocked.");
    }
}
```

### Testing standards (binding)

- PlayMode via the existing `StartHost()` harness — required for live `NetworkVariable`s [Source: `refactor-architecture-poco.md#3c`]. Do **not** call `NetworkManager.Singleton.StartHost()` outside the established setup pattern beyond what `VictoryConditionTests` already does.
- `[UnityTest] IEnumerator` + yield `null` / `NetworkTestHelper` waits. **Never** `WaitForSeconds` / `Thread.Sleep` (CI flakes) [Source: `project-context.md#What NOT to do in tests`].
- Each test isolated, any order: reconstruct state per test; reset static `instance` in `TearDown` [Source: `project-context.md#Test isolation`].
- Tag `[Category("HarnessFidelity")]`; method name = scenario (`Foo_Bar_WhenQux`) [Source: `project-context.md#File layout & naming`].
- `read_console` (compile errors) → targeted `run_tests` → full PlayMode suite, before declaring done [Source: `CLAUDE.md#Build / Test / Run`].

### Project Structure Notes

- File path mirrors source: conditions live in `Assets/Scripts/Characters/WinningConditions/`; the test goes under `Assets/Scripts/Tests/PlayMode/` — use a `Characters/` (or `Characters/WinningConditions/`) subfolder to mirror, matching the rule "test path mirrors source" [Source: `project-context.md#File layout & naming`]. `Tests/PlayMode/` is currently flat (no subfolders yet) — creating the subfolder is acceptable and preferred; if it adds friction, a flat placement next to `VictoryConditionTests.cs` is tolerable.
- New type goes in the existing `Tests.PlayMode` asmdef (depends on `Game` + `NetworkTestHelper`). **No new asmdef.** [Source: `project-context.md#Assembly definitions`]
- One top-level type per file; mutant subclasses are `private`/`sealed` nested helpers (small, test-only) [Source: `project-context.md#File layout`].

### Anti-patterns to avoid (would fail review)

- ❌ Adding a production mutation flag / `TEST_ForceState` backdoor on `WinningCondition` — forbidden [Source: `project-context.md#Testability constraints`]. Use the test-only mutant subclass.
- ❌ Reimplementing condition logic in the mutant — it must call `base.CheckCondition()`.
- ❌ Editing any file under `Assets/Scripts/Characters/WinningConditions/` — this story is test-only.
- ❌ Pulling Story 1.2 boundary coverage (all branches × all 4 conditions) into this story — out of scope; this gate is about harness **fidelity**, not condition **coverage**.
- ❌ `WaitForSeconds` / real-time waits; leaving `Debug.Log` in the committed test.
- ❌ Forgetting the static `instance` reset in `TearDown` → stale singleton across tests → flaky/false-green.

### Project Context Rules

Extracted from `_bmad-output/project-context.md`, scoped to this story:
- **Networked init in `OnNetworkSpawn`, not `Awake`** — characters/managers must be `Spawn()`ed and awaited (`WaitUntilAllSpawnedOrTimeout`) before reading their `NetworkVariable`s, else verdict reads garbage.
- **`NetworkVariable` server authority** — the harness runs as host (server), so directly setting `isCorrupted.Value` etc. is valid here (matches existing `VictoryConditionTests`). This is a test-only host-side write, not a client mutation.
- **Domain reload may be disabled** — static `instance` fields survive across PlayMode sessions; reset them in `TearDown` (`project-context.md#Domain reload`, `#Test isolation`).
- **PlayMode test rules** — `NetworkTestHelper`-bounded waits only; no `WaitForSeconds`; route through the established host setup, not a raw `StartHost()` (`project-context.md#PlayMode`).
- **Test isolation** — independent, any-order; per-test state reconstruction (`project-context.md#Test isolation`).
- **Commits** — Conventional Commits, English, body explaining *why*, no AI attribution. Pure tooling/test change → write a body, `UX:` line may be skipped (no player-facing change) (`CLAUDE.md#Commits`). Suggested: `test(victory): add harness-fidelity kill-test gating Epic 1 golden capture`.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story 1.0`] — AC verbatim; Epic 1 blocking-gate framing.
- [Source: `_bmad-output/refactor-architecture-poco.md#1`] — `GameManagerTests.cs:40` "passes for wrong reasons" (no `NetworkObject`); thin-suite warning.
- [Source: `_bmad-output/refactor-architecture-poco.md#3c`] — golden→losslessness→differential→cut strategy; existing `StartHost()` harness is the only live-`NetworkVariable` path.
- [Source: `_bmad-output/refactor-architecture-poco.md#3d`] — "the current suite is thin — add the §3c coverage before relying on the gate."
- [Source: `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`] — harness setup/teardown + live-state recipes to copy.
- [Source: `Assets/Scripts/Tests/PlayMode/GameManagerTests.cs:38-40`] — the broken-by-construction counter-example.
- [Source: `Assets/Scripts/Characters/WinningConditions/*.cs`] — condition source (boolean predicates; non-sealed overrides).
- [Source: `_bmad-output/project-context.md`] — testing rules, domain-reload, no-backdoor constraint, commit conventions.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- `read_console` (errors, filtered + unfiltered) after `refresh_unity` compile → 0 errors.
- `run_tests` PlayMode `category:HarnessFidelity` → job `894962fb...` → 2/2 passed, 0 failed.
- `run_tests` PlayMode full suite → job `46c9760a...` → 33/33 passed, 0 failed, 0 skipped (no regression).

### Completion Notes List

- Added one permanent PlayMode meta-test class proving the `StartHost()` harness observes the **production** `WinningCondition.CheckCondition()` verdict. Test-only; **no production file touched**.
- Setup/teardown copied verbatim from `VictoryConditionTests` (NetworkManager+UnityTransport, `StartHost()`, `GameManager` **with** `NetworkObject`+`Spawn()`, reflection-injected `_charactersParent`/`_characterPrefab`, static `instance` reset in teardown). Same harness the goldens (1.2/1.5) will use — no new harness invented.
- Two kill-tested conditions: `WAnomalyCorruption` (required target) + `WChosenChainedAllAnomaly` (SHOULD). Each via a `private sealed` mutant subclass overriding `CheckCondition() => !base.CheckCondition()` — runs real production logic, inverts only the verdict (faithful mutation; no logic reimplemented).
- Each test = positive control (real condition returns known verdict → harness traverses prod code) + kill assertion (`Assert.Throws<AssertionException>` on golden-style assert against the mutant → the net bites).
- Documented gate recorded in the class XML doc: if any test here goes RED, the harness is not faithful and Epic 1 golden capture (1.2/1.5) is BLOCKED until fixed.
- No production mutation flag / backdoor (forbidden). No new asmdef — lives in existing `Tests.PlayMode`. No `WaitForSeconds` — bounded `NetworkTestHelper` waits only. No `Debug.Log` left in.
- `WOmniscienceHackedCharacter` / `WMarginalIsChainedWin` intentionally out of scope (no current harness test; full boundary coverage is Story 1.2).
- Suggested commit: `test(victory): add harness-fidelity kill-test gating Epic 1 golden capture`.

### File List

- `Assets/Scripts/Tests/PlayMode/Characters/WinningConditionHarnessFidelityTests.cs` (new)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified — story status ready-for-dev → in-progress → review)

## Change Log

| Date | Change |
|---|---|
| 2026-06-10 | Implemented Story 1.0 harness-fidelity kill-test (2 conditions, meta-test gate). PlayMode 33/33 green, HarnessFidelity 2/2 green, 0 compile errors. Status → review. |
