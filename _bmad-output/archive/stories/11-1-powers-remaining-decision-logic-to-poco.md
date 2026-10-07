# Story 11.1: Powers — remaining decision logic to POCO

Status: review

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

- [x] **Task 1:** Decision-vs-glue inventory (Dev Agent Record below) — scopes Tasks 2-3.
- [x] **Task 2:** Extract the one clean decision candidate — base `Power.CanUse` eligibility chain → `PowerUsability` Domain POCO; adapter rebuilt. (Other candidates classified as already-POCO-backed / glue / Epic-11-follow-up — see inventory.)
- [x] **Task 3:** 17 EditMode tests for `PowerUsability` (every disqualifier + ordering/short-circuit boundaries); Domain purity guard green (POCO holds zero engine types).
- [x] **Task 4:** Gates (compile 0, EM 179/179 = 162 + 17, PM 148/148); sprint-status.

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

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors. EM 179/179 (162 baseline + 17 new `PowerUsability` tests, all discovered — new `.cs` created via MCP `create_script` per the silent-compile-exclusion rule, no exclusion). PM 148/148 (power behaviour preserved — `PowerTests` exercises `CanUse`, power goldens unchanged). Domain purity guard green (POCO uses only `System`).

### Implementation Plan / Inventory (Task 1)

**Decision-vs-glue pass over `Power.cs` + the concrete powers** (what Epic 4 left behind — Epic 4 already extracted effect *resolution* into `PowerResolver`/`EffectDescriptor`):

| Site | Classification | Action |
|---|---|---|
| `Power.CanUse` eligibility rule chain (8 ordered disqualifiers: components / passive / currently-used+ignore / chained / eliminated / awakening-gate / has-valid-target / uses-left) | **DECISION — pure boolean logic over plain values** | **EXTRACTED → `PowerUsability` Domain POCO** + 17 EditMode tests. |
| `Power.GetValidTargets` / `CheckIsTargetValid` (`characterManager.GetCharacters` + `targetValidator.Evaluate`) | Engine glue (reads CharacterManager) wrapping an already-POCO evaluator | STAYS in adapter; passed into the POCO as a **lazy `Func<bool>`** so the original short-circuit/exception position is preserved. |
| `targetValidator` infrastructure (`Validator<T>` in `GameLogic.Validation`) + the per-power `targetValidator.AddRule(...)` lambdas | **Already POCO-backed** — `Validator<T>` is a plain rule-evaluator; the lambdas delegate to `TargetUtils.IsTargetValid` (made POCO-friendly in 10.2) + cheap power state | NOT extracted (no duplication, per Dev Notes); recorded. |
| Use-count: `powerUseLeft.Value -= 1` (`OnUsedServer`), `powerUseLeft.Value <= 0` (in `CanUse`) | The `<= 0` *read* is part of the extracted chain; the `-= 1` is a NetworkVariable **mutation** | Mutation stays in the adapter (NFR4 — POCO decides, adapter applies). |
| Multi-pick selection progress (`PVisionOfTheImpossible` clicked-count vs `charactersToSelect`/`rolesToSelect`, `PEmbraceOfShadows`/`PDroolyHealing`/`PChainedByTheShadows` character-then-role flow) | Decision, but **entangled with the `SelectionFlowService` callback choreography** (golden-blind, just touched in 10.5) | Recorded as an **Epic-11 follow-up candidate** — out of scope for this focused, low-risk first extraction. |
| RPC bodies, `OnUsed`/`StartUse`/`StopUse`/`OnReparented`, `PowerEffectTrace.Record`, FMOD/`GameAudioManager` calls, the effect `_resolver` paths (Epic 4) | Lifecycle / RPC / dispatch glue | STAYS untouched (NFR4/NFR5; Epic 4 not redesigned). |

### Completion Notes List

**Extraction (Task 2) — `Power.CanUse` → `PowerUsability` (Domain).** The base eligibility chain is now a pure decision POCO (`CorruptionDuPortail.Domain.PowerUsability` + `PowerUsabilityContext` value struct). The adapter (`Power.CanUse`) keeps the owner-null guard **and its warning log** (logging glue), builds a plain-value snapshot from the owner Character's NetworkVariables + the power's own fields, and delegates the verdict. The one engine-coupled, expensive rule (`needTargetSelection && GetValidTargets().Count <= 0`) is supplied as a **lazy `Func<bool>`** invoked at the exact same position as the original — so a target-selecting power that an earlier rule already disqualifies never enumerates targets (preserving both the short-circuit and any exception behaviour of the per-power validator lambdas). `CanUse` stays `virtual`; the concrete overrides that call `base.CanUse(...)` are unaffected.

**Tests (Task 3) — 17 EditMode cases.** Every disqualifier individually; the `ignoreCurrentlyUsed` override; the awakening gate (required+asleep, required+awake, not-required); the use-count boundary (0/-1 false, 1 true); target present/absent; and two **ordering proofs** using a throwing delegate — the target check is NOT evaluated when the power doesn't select targets, nor when an earlier rule (passive) already disqualifies. These tests double as the characterization (the logic is pure boolean — no PlayMode golden needed; the existing Epic-4 power-golden traces + `PowerTests` + the full PM suite gate the adapter).

**NFR compliance.** NFR4: POCO returns the decision, adapter applies/owns state. NFR5: nothing RPC/bot-flow/`GetSafeRpcTarget` moved; `EffectDescriptor`/dispatch (Epic 4) untouched. Domain purity guard (`noEngineReferences`) green.

### File List

**New (production):**
- `Assets/Scripts/Domain/PowerUsability.cs` — `PowerUsability` decision POCO + `PowerUsabilityContext` value struct (zero engine types).

**New (tests):**
- `Assets/Scripts/Tests/Editor/PowerUsabilityTests.cs` — 17 EditMode boundary/ordering tests (`[Category("PowerUsability")]`).

**Modified (production):**
- `Assets/Scripts/Characters/Powers/Power.cs` — `_usability` field; `CanUse` rebuilt to snapshot engine state + delegate to the POCO with a lazy target delegate.

**Docs:** this story; `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 11.1 (Epic 11 / D5 start). Extracted the base `Power.CanUse` eligibility rule chain into a pure `PowerUsability` Domain POCO (+ `PowerUsabilityContext`); adapter builds a plain-value snapshot and delegates, with the engine-coupled target check supplied as a lazy `Func<bool>` to preserve the exact short-circuit. 17 EditMode tests (every disqualifier + ordering/short-circuit proofs). Inventory recorded the remaining sites (already-POCO target validation, mutation glue, entangled multi-pick selection → Epic-11 follow-up). NFR4/NFR5 honoured; Epic 4 untouched. Compile 0; EM 179/179 (162 + 17); PM 148/148; Domain purity guard green. |
