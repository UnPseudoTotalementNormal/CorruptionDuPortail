# Story 2.8: Extract `VictoryEvaluator` (the loop) + migrate its tests to EditMode

Status: done

## Story

As a developer (Poyo),
I want the win-team loop extracted as a POCO `VictoryEvaluator` over snapshots,
so that victory evaluation runs as fast EditMode tests.

## Acceptance Criteria

**Given** the cut is complete (2.7/2.7b) and conditions are POCO-facing
**When** `VictoryEvaluator.Evaluate(...)` is extracted into `Domain`
**Then** it returns the win-team mapping by calling `IWinningCondition.CheckCondition(snapshot)`; it never triggers a state transition (NFR4 — returns a decision the adapter applies)
**And** `isFake` is set at the mapping source, not recomputed in the POCO (bots must not be mis-filtered)
**And** the victory loop tests run as EditMode in this same commit (incremental)

### Acceptance reading notes (binding)

1. **Signature deviation (documented).** The architecture sketch wrote `Evaluate(IReadOnlyList<CharacterSnapshot>)`, but `CharacterSnapshot` carries no conditions — the loop needs each non-fake owner's `winningConditions`. Use:
   ```csharp
   public Dictionary<WinningTeam, HashSet<ulong>> Evaluate(GameSnapshot snapshot, IEnumerable<ConditionsForOwner> owners)
   ```
   where `ConditionsForOwner` is a tiny Domain struct `{ ulong OwnerClientId; IEnumerable<IWinningCondition> Conditions; }`. The `snapshot` is what conditions evaluate against; `owners` carries the per-character conditions. This preserves the exact loop semantics (`VictoryConditionCheckState.cs:24-45`).
2. **`isFake` set at the source (AC).** The ADAPTER pre-filters fakes (`if (c.isFake) continue;`) when building `owners`, so the POCO never sees a fake and never recomputes `IsFakeClientId`. Fakes are excluded at the mapping boundary — exactly the AC's intent (bots not mis-filtered inside the POCO).
3. **NFR4 — decision only.** `VictoryEvaluator` is a pure POCO in `Domain`: it returns the `Dictionary<WinningTeam, HashSet<ulong>>` and triggers NO state transition / RPC (it structurally cannot — `Domain` has no engine access). The adapter applies the decision (`Count == 0 → NextGameState`, else `SetWinnersServer` + `SetGameState`).
4. **Behavior-preserving extraction.** The aggregation logic is moved verbatim: for each non-fake owner, for each condition, if `CheckCondition(snapshot)` then add `OwnerClientId` to that condition's team set. The adapter's surrounding logic (empty→NextGameState, else SetWinners+SetGameState) is unchanged.
5. **Tests migrate to EditMode.** `VictoryEvaluatorTests` (EditMode, `[Category("VictoryEvaluator")]`) tests the aggregation with fake `IWinningCondition` stubs — no NGO host, fast. Cover: empty owners → empty; single true condition → one team→owner; false condition → excluded; two owners winning the same team → both in the set; a condition's `GetWinningTeam` keys the set; mixed multi-owner. The existing PlayMode oracle/goldens (which test the conditions, not the aggregation) stay as-is.
6. Full regression green; shippable per commit.

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — `VictoryEvaluator` + `ConditionsForOwner` in Domain** (notes 1–4); `DomainPurity` stays green
- [ ] **T2 — Re-point the adapter** `VictoryConditionCheckState.OnStartStateServer`: build `owners` (fakes pre-filtered), call `new VictoryEvaluator().Evaluate(snapshot, owners)`, apply the decision; `read_console` clean
- [ ] **T3 — EditMode `VictoryEvaluatorTests`** (note 5) with stub conditions
- [ ] **T4 — Prove**: `run_tests category: VictoryEvaluator` (EditMode) + full regression (PlayMode + EditMode) green; console clean

## Dev Notes

**Created:** `Assets/Scripts/Domain/VictoryEvaluator.cs` (+ `ConditionsForOwner`); `Assets/Scripts/Tests/Editor/VictoryEvaluatorTests.cs`.
**Modified:** `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs` (delegate the loop to `VictoryEvaluator`).
**Must NOT change:** the conditions, the builder, the goldens/oracle.

### Adapter shape (post-extraction)

```
GameSnapshot snapshot = GameSnapshotBuilder.FromLiveState(gameManager);
var owners = new List<ConditionsForOwner>();
foreach (var c in gameManager.characterManager.GetCharacters(false)) {
    if (c.isFake) continue;                                  // isFake set at the source
    owners.Add(new ConditionsForOwner(c.ownerClientId.Value, c.role.winningConditions));
}
var winningTeams = new VictoryEvaluator().Evaluate(snapshot, owners);
if (winningTeams.Count == 0) { gameManager.NextGameState(); return; }
// SetWinnersServer + SetGameState (unchanged)
```
`List<WinningCondition>` flows into `IEnumerable<IWinningCondition>` by covariance (`WinningCondition : IWinningCondition`).

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.8] (lines 310–322)
- [Source: _bmad-output/refactor-architecture-poco.md] (line 60 — VictoryEvaluator role; NOT the conditions)
- [Source: Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs] — the loop being extracted (post-2.7 re-point)
- [Source: Assets/Scripts/Domain/IWinningCondition.cs] — the contract the evaluator calls

### Previous story intelligence

- 2.7 re-pointed the loop to the snapshot via `IWinningCondition`; 2.8 lifts the aggregation itself into a pure POCO. The adapter keeps NetworkVariable/transition ownership (NFR4).
- `Domain` is `autoReferenced`; no asmdef edits. EditMode tests already reference `Domain` (Story 1.4).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Extracted the win-team aggregation into the pure Domain POCO `VictoryEvaluator` (+ `ConditionsForOwner`); the adapter `VictoryConditionCheckState` now maps live state in (fakes filtered at the source), calls `Evaluate(snapshot, owners)`, and applies the decision (NFR4 — no transition inside the POCO). `WinningTeam` needed `using Characters.WinningConditions;` in the Domain file (relocated namespace).
- 7 fast EditMode `VictoryEvaluatorTests` with stub `IWinningCondition`s pin the aggregation (empty, single true, false excluded, two owners same team, keyed-by-team, mixed multi-owner, same-owner-multi-team).
- Behavior-preserving: `DomainPurity` green (Domain still pure); regression 90/90 PlayMode + 23/23 EditMode green.

### File List

- **Added:** `Assets/Scripts/Domain/VictoryEvaluator.cs` (`VictoryEvaluator` + `ConditionsForOwner`), `Assets/Scripts/Tests/Editor/VictoryEvaluatorTests.cs`
- **Modified:** `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs` (delegate the loop to `VictoryEvaluator`)

### Change Log

- 2026-06-10 — Extracted `VictoryEvaluator` (the win-team loop) into Domain; adapter applies the decision; loop now EditMode-tested. Story 2.8 → review.
