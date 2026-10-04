# Story 2.7: Cut the old signature + promote `IWinningCondition` into Domain

Status: done

<!-- REVIEW-REQUIRED: cuts the old signature / re-points the production loop — point of no return. Run gds-code-review before merge. -->

## Story

As a developer (Poyo),
I want the legacy `CheckCondition()` retired from the production path and `IWinningCondition` promoted into the Domain assembly,
so that conditions become POCO-facing with no `using GameLogic` pull in the evaluation the game actually runs.

## Acceptance Criteria

**Given** all four conditions migrated and green (2.3–2.6)
**When** the cut commit lands
**Then** the old `CheckCondition()` signature is removed from the production call path (the victory loop now evaluates off the snapshot) and `IWinningCondition` is promoted into `Domain`
**And** the cut commit touches **no** `.golden` / expected fixture — only the call target; a hash of the golden suite (expected + assertions) is equal before and after the cut
**And** the legacy signature is feature-flagged / retained rather than hard-deleted in the same commit where the differential still needs it (preserve the proof one beat longer than the thing it proves)

### Acceptance reading notes (binding — this is the point of no return)

1. **The "cut" = re-point the production victory loop, NOT delete the pull.** `VictoryConditionCheckState.OnStartStateServer` currently calls `_cond.CheckCondition()` (the live pull). Re-point it to build the snapshot **once** (`GameSnapshotBuilder.FromLiveState(gameManager)`) and call `_cond.CheckCondition(snapshot)`. Production victory evaluation now runs off the immutable snapshot — POCO-facing, no per-condition `GameManager` pull. **Safety:** the per-condition differential (2.3–2.6) + builder losslessness (2.1) already prove `CheckCondition(snapshot) == CheckCondition()` for every condition, so the re-point is behavior-preserving by composition. No test drives this loop directly (verified), so nothing breaks; the goldens (which call the pull directly) and the differential (which needs the pull) are unaffected.

2. **Keep the pull — it is the differential oracle until Story 2.7b.** Do NOT delete or make abstract `WinningCondition.CheckCondition()`. The `SnapshotDifferential` harness still compares `pull` vs `snapshot`; 2.7b swaps the oracle to the frozen goldens and only then is the pull retired. Mark the pull as legacy/test-oracle-only in a doc comment ("retained for the differential until 2.7b; not the production path"). This satisfies the AC's "feature-flagged / retained rather than hard-deleted … preserve the proof one beat longer".

3. **Promote `IWinningCondition` into Domain.** Add `Assets/Scripts/Domain/IWinningCondition.cs`:
   ```csharp
   namespace CorruptionDuPortail.Domain
   {
       public interface IWinningCondition
       {
           WinningTeam GetWinningTeam();
           bool CheckCondition(GameSnapshot snapshot);
       }
   }
   ```
   It is pure (references only Domain types `WinningTeam` + `GameSnapshot`). Wire `WinningCondition` (Game) to implement it: `public abstract class WinningCondition : INetworkSerializable, IWinningCondition`. `GetWinningTeam()` and the `CheckCondition(GameSnapshot)` overload already satisfy it. The 1.3 placeholder `IWinningConditionEvaluator` is now superseded by `IWinningCondition` for the snapshot path — leave it in place (harmless, no behavior), a later cleanup may remove it.

4. **No golden/fixture changes.** Touch ZERO test file under the golden suites (`WinningConditionGoldenMasterTests`, `ChainingResolverGoldenMasterTests`, `VoteTallyGoldenMasterTests`, the harness-fidelity tests). The only new test is the loop-re-point equivalence check. The "golden hash equal before/after" AC is satisfied by not touching them.

5. **Re-point equivalence proof.** Add a PlayMode test that, over a multi-character multi-condition scenario, asserts the snapshot-based per-`(character, condition)` verdicts equal the pull-based verdicts — i.e., the aggregate the re-pointed loop produces is identical to the legacy loop's. (Driving `OnStartStateServer` directly triggers state transitions / `SetGameState`; instead validate the evaluation equivalence that the loop is composed of — the loop's aggregation logic is unchanged.)

6. **REVIEW-REQUIRED:** run `gds-code-review` on the branch before merge; address findings; then squash & merge.

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (full PlayMode + EditMode regression)
- [ ] **T1 — Promote `IWinningCondition` into Domain** (note 3); wire `WinningCondition : …, IWinningCondition`; `read_console` clean (Domain stays pure — `DomainPurity` green)
- [ ] **T2 — Re-point the production loop** in `VictoryConditionCheckState` (note 1): build snapshot once, call `CheckCondition(snapshot)`; mark the pull legacy (note 2); `read_console` clean
- [ ] **T3 — Re-point equivalence test** (note 5) `[Category("Migration")]`
- [ ] **T4 — Prove**: full regression (`HarnessFidelity,GoldenMaster,Determinism,SnapshotBuilder,Differential,Migration` PlayMode + `DomainPurity,DomainSnapshot` EditMode) green; goldens untouched; console clean
- [ ] **T5 — `gds-code-review`** on the branch; address findings
- [ ] **T6 — PR + squash-merge** to dev-refactor

## Dev Notes

**Created:** `Assets/Scripts/Domain/IWinningCondition.cs`; `Assets/Scripts/Tests/PlayMode/Migration/VictoryLoopRepointTests.cs`.
**Modified (production):** `Assets/Scripts/Characters/WinningConditions/WinningCondition.cs` (implement `IWinningCondition`; doc the legacy pull); `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs` (build snapshot once, call snapshot overload).
**Must NOT change:** any golden/fixture test, the 4 evaluators' bodies, the builder, asmdefs.

### Why this is safe (composition of proofs)

- 2.1 — builder losslessness: snapshot faithfully captures every field the conditions read.
- 2.3–2.6 — per-condition differential + sentinels: `CheckCondition(snapshot) == CheckCondition()` for each condition, and the snapshot path genuinely reads the fields.
- The loop's aggregation (`_winningTeams` dict build) is unchanged. Re-pointing the per-condition call is therefore behavior-preserving.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.7] (lines 282–294)
- [Source: _bmad-output/refactor-architecture-poco.md] (§3a — WinningCondition refonte; lines 97–101)
- [Source: Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:24–45] — the loop being re-pointed
- [Source: Assets/Scripts/Characters/WinningConditions/WinningCondition.cs] — the base to wire to `IWinningCondition`
- [Source: Assets/Scripts/Domain/IWinningConditionEvaluator.cs] — the 1.3 placeholder (superseded, left in place)

### Previous story intelligence

- The pull MUST stay (differential oracle) until 2.7b — do not delete it here.
- No test drives `VictoryConditionCheckState.OnStartStateServer` (grep-verified) → re-pointing the loop breaks nothing in the suite; equivalence is proven by composition + the new re-point test.
- Domain purity guard (`DomainPurity`) must stay green — `IWinningCondition` references only Domain types.

## Dev Agent Record

### Agent Model Used

### Completion Notes List

### File List

### Change Log

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Re-pointed `VictoryConditionCheckState.OnStartStateServer` to build the snapshot once and evaluate via `CheckCondition(snapshot)`. Promoted `IWinningCondition` into pure Domain; `WinningCondition` implements it. Legacy pull retained as the differential oracle until 2.7b (doc-marked). No golden/fixture touched.
- Behavior-preserving by composition (2.1 losslessness + 2.3–2.6 differentials); the loop aggregation is unchanged. New `VictoryLoopRepointTests` proves per-(character, condition) snapshot==pull over a 4-condition scenario.
- Full suite green: 83/83 PlayMode + 16/16 EditMode. `gds-code-review` (3 adversarial layers) ⇒ PASS, no blocking findings.

### File List

- **Added:** `Assets/Scripts/Domain/IWinningCondition.cs`, `Assets/Scripts/Tests/PlayMode/Migration/VictoryLoopRepointTests.cs`
- **Modified:** `Assets/Scripts/Characters/WinningConditions/WinningCondition.cs` (implement `IWinningCondition`; doc legacy pull), `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs` (snapshot re-point)

### Change Log

- 2026-06-10 — Cut the production loop over to the snapshot path; promoted `IWinningCondition` into Domain; pull retained as oracle. gds-code-review PASS. Story 2.7 → review.

### Senior Developer Review (AI)

**Outcome: Approve (no blocking findings).** Three adversarial layers run via gds-code-review on the `story/2-7` diff vs `dev-refactor`.

- **Blind Hunter — Med "Domain asmdef purity vs references":** FALSE POSITIVE. The reviewer had no project access; `WinningTeam` and `GameSnapshot` physically reside in the Domain assembly (relocated in Stories 1.3/1.4), so `references:[]` + `noEngineReferences:true` compiles. Verified: clean compile + `DomainPurity` 2/2 green.
- **Edge Case Hunter — Low "WOmniscience owner-present-null-role diverges (snapshot false vs pull NRE)":** Real in the abstract but **unreachable in the re-pointed loop** — the loop iterates `character.role.winningConditions` (role non-null) and the condition's `ownerClientId` is that character; owner-present-with-null-role cannot arise. The in-code comment is already scoped to "owner not found" (golden O1), which the override does mirror. No change.
- **Blind Hunter — Low "default-satisfies-interface allows silent regression":** Intentional — `CheckCondition(GameSnapshot)` becomes `abstract` once the pull is removed in Story 2.7b.
- **Acceptance Auditor — PASS on all 3 ACs:** loop genuinely re-pointed; `IWinningCondition` in the pure Domain assembly; legacy pull retained (not hard-deleted); zero golden/fixture files touched (diff = exactly the 4 prescribed source files).
