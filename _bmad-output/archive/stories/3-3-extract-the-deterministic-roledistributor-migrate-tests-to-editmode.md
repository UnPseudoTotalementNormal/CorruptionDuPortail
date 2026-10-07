# Story 3.3: Extract the deterministic `RoleDistributor` + migrate tests to EditMode

Status: review

## Story

As a developer (Poyo),
I want role assignment extracted as a POCO `RoleDistributor` driven by `IRandomProvider` and the frozen pool order,
so that distribution is deterministic, fast-testable, and engine-free.

## Acceptance Criteria

**Given** the goldens of 3.2
**When** `RoleDistributor` is extracted into `Domain`
**Then** it assigns roles from settings + player list using `IRandomProvider` and the explicit frozen pool ordering (no `Dictionary.Keys.ToList()` drift)
**And** it reproduces the 3.2 goldens bit-for-bit under the same seed
**And** it returns the assignment as data; the adapter applies it (NFR4)
**And** the assignment tests are migrated PlayMode → EditMode in this commit
**And** the full suite is green (NFR6 gate before Wave 3)

## Acceptance reading notes (binding)

1. **FAKE-PATH COVERAGE FIRST (user decision).** The 3.2 goldens only cover `fakeCount == 0`. Before extracting, extend the live golden corpus to the fake path (`sum(counts) > N` → `CreateNewFakeCharacter` loop runs), pinning the ordered (fake-roles, real-roles) assignment on the CURRENT code. Only then extract — so both loops are proven behavior-preserving.
2. **The selection algorithm (both loops).** `OnStartStateServer` runs two draw loops over a SHARED, mutating count map:
   - **fake loop** (`fakeCount` draws): available = frozen order ∩ `_fakeRoles` (the `canBeFake` subset) with remaining count > 0; `idx = Random.Range(0, available.Count)`; pick; decrement the shared setting; a role exhausted (count→0) is removed from both `_fakeRoles` and `_rolesToAttribute`.
   - **real loop** (one draw per non-fake character): available = frozen order with remaining count > 0; same draw/decrement/remove.
   The two loops share the same per-role remaining counts (the `RoleAttributionSetting` objects are shared) and draw from the SAME RNG in order (all fakes, then all reals).
3. **POCO** `RoleDistributor` (pure Domain, decision-only NFR4) operating on indices into the frozen order:
   - `RoleDistribution Distribute(IReadOnlyList<int> initialCounts, IReadOnlyList<bool> canBeFake, int fakeCount, int realCount, IRandomProvider rng)` → returns `{ IReadOnlyList<int> FakeRoleIndices, IReadOnlyList<int> RealRoleIndices }` (each value is an index into the frozen order).
   - Reproduces the exact draw order (fakes then reals), the shared-count depletion, the `>0` availability filter, and the `_fakeRoles ∩ remaining` fake filter. **Does not mutate the caller's input** (works on an internal copy — addresses the 3.2 shared-mutation finding).
   - No NGO, no `RoleDataObject`, no `Random` — the adapter maps indices ↔ `RoleDataObject` and applies side effects.
4. **Adapter** (`RoleAttributionState.OnStartStateServer`): build `initialCounts` / `canBeFake` from `GetFrozenRolePoolOrder()` + the settings; compute `fakeCount` / `realCount` as today; call `RoleDistributor.Distribute(..., new UnityRandomProvider())`; then for each fake index create a fake character + apply the role, and for each real index apply the role to the next real character — preserving the existing `Clone` / `GivePowerToCharacter` / `GiveRoleToCharacterRpc` side effects in the SAME order. `Random.Range` is removed from the state.
5. **Behavior-preservation:** the 3.2 + fake-path PlayMode goldens stay green unchanged, now delegating to the POCO under `UnityRandomProvider` + `InitState(seed)` (`Next(max)` == `Random.Range(0, max)`).
6. **EditMode migration:** add EditMode `RoleDistributorTests` `[Category("RoleDistributor")]` driving the POCO with `SeededRandomProvider` (pure, no host) — property tests on count-exhaustion, fake-then-real ordering, frozen-order determinism, no-input-mutation. The PlayMode goldens remain (live-NetworkVariable oracle, by design).

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — Fake-path golden** added to `RoleAssignmentGoldenMasterTests` on CURRENT code (discovery → pin the ordered fake+real assignment)
- [ ] **T2 — `RoleDistributor` + `RoleDistribution` in Domain** (note 3); `DomainPurity` green
- [ ] **T3 — Re-point `RoleAttributionState.OnStartStateServer`** to the POCO + `UnityRandomProvider` (note 4); `read_console` clean
- [ ] **T4 — EditMode `RoleDistributorTests`** (note 6)
- [ ] **T5 — Prove**: `RoleDistributor` + `RoleAssignment` + full regression green (the goldens re-verify the live path unchanged); console clean

## Dev Notes

**Created:** `Assets/Scripts/Domain/RoleDistributor.cs`, `Assets/Scripts/Tests/Editor/RoleDistributorTests.cs`.
**Modified:** `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` (delegate the selection to the POCO), `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs` (add the fake-path golden).
**Must NOT change:** the frozen pool order, the side-effect order (Clone/power/RPC), the `IRandomProvider` impls.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.3] (lines 431–445)
- [Source: Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:29-130] — the two-loop selection being extracted
- [Source: Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs] — the 3.2 goldens (extended here to the fake path)

### Previous story intelligence

- 3.2 finding: `OnStartStateServer` mutates shared `RoleAttributionSetting` counts; the POCO must work on a copy (no caller mutation).
- 3.1 `UnityRandomProvider.Next(max)` == `Random.Range(0, max)`; `SeededRandomProvider` for pure EditMode property tests.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- **Fake-path characterized first** (user decision): added `RoleAssignment_FakePath_Exhaustion` golden on the CURRENT code — N=2 reals, [A:2,B:2] canBeFake, seed 5 → fakes `[B,B]`, reals `[A,A]` (pins the shared-count depletion across both loops). Then extracted.
- **`RoleDistributor` + `RoleDistribution` POCO** (Domain): `Distribute(initialCounts, canBeFake, fakeCount, realCount, rng)` returns frozen-order indices for fakes then reals, reproducing the two-loop draw, the `>0` availability filter, the canBeFake∩remaining fake filter, the shared depletion, and the empty-fake break. Works on an internal copy — never mutates caller input (closes the 3.2 finding). Decision-only (NFR4).
- **`RoleAttributionState.OnStartStateServer` re-pointed**: builds counts/canBeFake in frozen order, computes fakeCount/realCount as before, calls the POCO with `UnityRandomProvider`, then `ApplyRole` (extracted side-effect body — Clone/role/power/RPC in the same order) per fake (with `CreateNewFakeCharacter`) then per real. `Random.Range` removed from the state; dead unreachable `WaitAndNextState` block dropped.
- **Behavior-preserving (proven):** the PlayMode goldens (5 cases incl. the fake path) pass UNCHANGED with the live path now delegating to the POCO under `UnityRandomProvider` + `InitState(seed)` — bit-for-bit reproduction.
- **EditMode migration:** 7 `RoleDistributorTests` `[Category("RoleDistributor")]` with a scripted stub RNG — frozen-order exhaustion, fake-then-real shared depletion, canBeFake filter, empty-fake break, index-into-available (not raw role index), no-input-mutation, seeded determinism.
- Full suite green (NFR6 gate before Wave 3): **130/130 EditMode + 134/134 PlayMode**.

### File List

- **Added:** `Assets/Scripts/Domain/RoleDistributor.cs`, `Assets/Scripts/Tests/Editor/RoleDistributorTests.cs`
- **Modified:** `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` (delegate selection to the POCO), `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs` (fake-path golden)

### Change Log

- 2026-06-11 — Story 3.3 drafted; characterize the fake path then extract `RoleDistributor`, re-point the adapter, migrate property tests to EditMode.
