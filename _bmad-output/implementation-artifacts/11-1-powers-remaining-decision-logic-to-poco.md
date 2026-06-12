# Story 11.1: Powers — remaining decision logic to POCO

Status: ready-for-dev

## Story

As a developer,
I want the decision logic still inline in `Power`/concrete powers (beyond the already-extracted `PowerResolver`) moved into tested POCOs,
so that power rules are EditMode-testable without booting NGO.

## Acceptance Criteria

1. **Inventory first:** line-by-line pass over `Power.cs` + concrete powers classifying remaining logic: decision (extract) vs lifecycle/RPC/Unity glue (stays). Candidates: `CanUse` rule chains, target-validation rules (`targetValidator.AddRule` lambdas), use-count/cooldown arithmetic — exact set from the inventory (this story's §4.0-style foundation).
2. **Extraction per the Wave 1-4 pattern:** characterize-then-extract for anything subtle (golden first on current code), POCO into `Domain` where zero engine types (else `Game`-POCO, recorded), adapters call the core, NFR4 (POCO returns decisions, adapter applies).
3. **EditMode tests shipped per extracted core** (boundary cases, not just happy path); existing Epic 4 golden traces unchanged.
4. **NFR5 verbatim:** nothing RPC/bot-flow moves into a POCO; `EffectDescriptor`/dispatch architecture (Epic 4) untouched — this story extracts what Epic 4 left behind, it does not redesign Epic 4's work.
5. **Gated:** suite + fixture + goldens unchanged; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Decision-vs-glue inventory (paste into Dev Agent Record — it scopes Tasks 2-3).
- [ ] **Task 2:** Extract per candidate (one commit each, characterize first where behaviour is non-obvious).
- [ ] **Task 3:** EditMode tests per core; Domain purity guard green (noEngineReferences).
- [ ] **Task 4:** Gates; sprint-status; commits.

## Dev Notes

- Epic 4 already extracted effect RESOLUTION (PowerResolver + EffectDescriptor + dispatcher). What's left inline is typically: eligibility (`CanUse`), targeting predicates, state checks. Those are exactly the EditMode-testable rules Poyo wants real tests on.
- Target-validation lambdas (`ctx => TargetUtils.IsTargetValid(...)`) may collapse into a testable rule-set POCO — but TargetUtils interactions were reshaped in 10.2; build on that, don't duplicate.
- Domain asmdef test gotcha: explicit Domain ref needed in test asmdefs (CS0012 otherwise) — precedent recorded (`reference_domain_asmdef_autoref_tests`).
- Staleness: inventory-driven; powers were touched by 7.1/10.x.

### Project Structure Notes

- New: Domain POCOs + EditMode tests (path mirrors source). Modified: `Power.cs` + concrete powers (thinning). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Pure decision logic in non-Mono classes (project-context:158); no TEST_ForceState backdoors; NSubstitute via interfaces; tests mirror source paths; new-power test requirement now CHEAP (this is the payoff story).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.4, §8 Epic 11] / [epics.md#Story 11.1]
- [Source: _bmad-output/refactor-architecture-poco.md] — the Wave 1-4 extraction method this story continues.
- [Source: Assets/Scripts/Characters/Powers/Power.cs + PowerResolver/EffectDescriptor (Domain)] — the boundary between done (Epic 4) and remaining.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
