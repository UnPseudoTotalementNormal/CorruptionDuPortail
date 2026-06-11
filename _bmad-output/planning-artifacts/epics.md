---
stepsCompleted: ['step-01-validate-prerequisites', 'step-02-design-epics', 'step-03-create-stories']
inputDocuments:
  - '_bmad-output/refactor-architecture-poco.md'
  - '_bmad-output/refactor-architecture-desingleton.md'
  - '_bmad-output/project-context.md'
---

# Corruption Du Portail - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for the **POCO core extraction (behavior-preserving) refactor** of Corruption Du Portail. Source of truth: `_bmad-output/refactor-architecture-poco.md`. This is a structural refactor, **not** a redesign — no mechanic changes. Every extraction is validated against characterization/golden tests captured *before* the change.

> Note: a prior `REFACTORING_PLAN.md` (T01–T18) tracked partial progress but was never committed and is lost. Progress is re-anchored to the waves below.

## Requirements Inventory

### Functional Requirements

FR1: Phase 0-a — Build the characterization + golden-master test harness (~23 boundary cases across the 4 `WinningCondition`s), pinning current behavior including the §3b A/C non-determinism sources, driving the existing NGO PlayMode host.
FR2: Phase 0-b — Create `CorruptionDuPortail.Domain.asmdef` (`noEngineReferences: true`, `references: []`) containing only zero-dependency types: `WinningTeam`, `CharacterSnapshot`, `GameSnapshot`, `IWinningConditionEvaluator`.
FR3: Wave 1 — Refonte `WinningCondition` via strangler (§3a): add `GameSnapshot`/`CharacterSnapshot`, `GameSnapshotBuilder.FromLiveState`, dual-signature `CheckCondition(snapshot)`, migrate the 4 concrete conditions one commit each (easy→hard), then cut the old signature.
FR4: Wave 1 — Extract `VictoryEvaluator` (the win-team loop only, not the conditions) into `Domain`.
FR5: Wave 1 — Extract `VoteTally` (vote count → outcome) into `Domain`.
FR6: Wave 1 — Extract `ChainingResolver` (accusation/chain resolution) into `Domain`.
FR7: Wave 1 — Extract `GameLoopMachine` arithmetic, preserving the load-bearing `OnEndStateClient → write currentGameStateIndex.Value → OnStartStateClient` ordering.
FR8: Wave 1 — Convert the now-POCO victory/chaining tests from PlayMode to EditMode.
FR9: Wave 2 — Add `IRandomProvider` (seedable) and freeze the role-pool iteration ordering (§3b A).
FR10: Wave 2 — Extract `RoleDistributor` (deterministic via `IRandomProvider` + fixed pool order) with golden tests on assignment.
FR11: Wave 3 — Extract `PowerResolver` (effect arithmetic only) returning `EffectDescriptor` value objects; all FMOD/Focus/RPC side-effects and NGO ownership stay in the adapter.
FR12: Wave 4 (HELD) — `GameLoopMachine` fully owns the state index while the adapter mirrors it into `currentGameStateIndex`.
FR13: Wave 4 (HELD) — Add a **defensive wire-format guard** over the reflection RPC dispatch (`CallStateMethodRpc` / `CallMethodAfterRpc`): pin the `{GameState type → FixedString64Bytes, method → string}` mapping table as a versioned EditMode golden so any rename/move breaks a test instead of a player. *(Descoped from a full dispatch refonte after a party-mode review + a confirmed Steam P2P single-build deployment model: there is no host/client version boundary, so the in-flight-packet fragility is theoretical. The full reflection-dispatch refonte is moved to a separate, out-of-this-refactor `feat(net): versioned state-dispatch protocol` effort.)*

### NonFunctional Requirements

NFR1: Behavior-preserving — gameplay must stay bit-for-bit identical; every extraction validated against golden/differential tests captured before the change. Reviewer rejects any behavior change not backed by an unchanged golden/differential test.
NFR2: Domain asmdef purity — no `UnityEngine` / `Unity.Netcode` / FMOD / DOTween in the core; enforced by the compiler via `noEngineReferences`. `Game` references `Domain`, never the reverse.
NFR3: Determinism — `IRandomProvider` plus freeze role-pool order (§3b A), pin vote-insertion order (§3b C), and quarantine `AwakeningState` frame-timed randomness (§3b B, out of scope for Phase 0 / Wave 1).
NFR4: POCO never triggers a state transition — Waves 1–3 cores return a *decision*; the adapter calls `NextGameState`/`SetGameState`.
NFR5: Preserve `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` simulated-gateway semantics verbatim (Wave 4 do-not-touch).
NFR6: Test gate between waves — full EditMode + PlayMode suite green before the next wave begins.
NFR7: Strangler-fig, not big-bang — shippable at every commit; keep the static `instance` façade temporarily, remove only once nothing depends on it.

### Additional Requirements

- Ports the core needs (Unity implements): `IRandomProvider` (`Next(int max)`, seedable), `IGameEventBus` / adapter-side dispatch (emit `onGameStarted` / `onNewDayPassed` equivalents), `IGameClock` (only for frame-timed `AwakeningState`, out of Wave 1 scope).
- The core never references `NetworkVariable`, `RpcParams`, `GetSafeRpcTarget` — those stay 100% in the adapter.
- Stays in `Game` (implement Domain interfaces, do not move): `WinningCondition` (`INetworkSerializable`), `Role` (`INetworkSerializable` + FMOD `EventReference`), `Character`, `Power` (all `NetworkBehaviour`).
- Definition of done per system (§6): POCO with no Unity `using` in `Domain`; golden + differential parity proven; adapter reduced to map-in / call core / apply-out; state-transition ordering + simulated-client gateway verified unchanged; full suite green.

### UX Design Requirements

N/A — internal structural refactor, no player-facing UI change (NFR1 forbids observable behavior change).

### FR Coverage Map

- FR1, FR2 → Epic 1 (Phase 0 — harness + Domain asmdef)
- FR3, FR4, FR5, FR6, FR7, FR8 → Epic 2 (Wave 1 — victory + loop + vote + chaining)
- FR9, FR10 → Epic 3 (Wave 2 — determinism + role distribution)
- FR11 → Epic 4 (Wave 3 — power resolution)
- FR12, FR13 → Epic 5 (Wave 4 HELD — network-critical)

13/13 FRs mapped, no orphan. Dependencies strictly sequential: each epic is gated by a full green EditMode+PlayMode suite (NFR6) before the next begins. Epic 1 is a hard prerequisite of all others.

## Epic List

> Epic boundaries deliberately follow the extraction **waves** from `refactor-architecture-poco.md` §4. Each wave is a genuine risk boundary with a mandatory test gate (NFR6) where early findings can change the direction of later waves — the documented exception to "don't organize epics by technical layer". A party-mode audit already hardened the wave ordering; a pre-mortem on this epic breakdown surfaced the refinements folded in below.

### Epic 1: Foundations — characterization harness & Domain boundary
Stand up the safety net before any extraction: capture current behavior as golden masters and erect the compiler boundary that structurally forbids Unity in the core. Nothing is extracted yet, but no later extraction is safe without this. **Pre-mortem refinements:** (a) the wave gate is a **coverage** check, not just "tests green" — the ~23 boundary cases across the 4 conditions and the ≥7 losslessness fields (§3c) must exist as acceptance criteria here; (b) include an explicit **quarantine + flag** of the `AwakeningState` frame-timed randomness (§3b B) so no later golden accidentally captures frame-dependent behavior and turns the gate flaky; (c) if any role-attribution behavior is golden-tested in this epic, the role-pool ordering must be frozen here too (§3b A), otherwise those goldens rot by Epic 3.
**FRs covered:** FR1, FR2

### Epic 2: Wave 1 — extract victory + loop + vote + chaining
Strangler refonte of `WinningCondition` (shippable at every commit), then carve the pure cores `VictoryEvaluator`, `VoteTally`, `ChainingResolver`, and the `GameLoopMachine` arithmetic out into `Domain`. After this epic the victory/vote/chaining/loop rules run in fast EditMode with parity proven. **Pre-mortem refinements:** (a) FR3 is decomposed at story level into **one story per concrete condition migrated** (`WMarginalIsChainedWin` → `WAnomalyCorruption` → `WChosenChainedAllAnomaly` → `WOmniscienceHackedCharacter`), preserving the shippable-per-commit property and fitting a single dev-agent context; (b) `GameLoopMachine` is **intentionally 2-phase** — only the arithmetic + the load-bearing `OnEnd → write NV → OnStart` adapter ordering land here; full index ownership is HELD to Epic 5. Add a **holding test** that pins the Wave 1 boundary so the Wave 2/3 work cannot silently break the half-migrated machine.
**FRs covered:** FR3, FR4, FR5, FR6, FR7, FR8

### Epic 3: Wave 2 — determinism & role distribution
Introduce `IRandomProvider` and freeze the role-pool iteration order (§3b A), then extract a deterministic `RoleDistributor` with golden tests on assignment. After this epic role attribution is reproducible and golden-locked.
**FRs covered:** FR9, FR10

### Epic 4: Wave 3 — power resolution
Extract `PowerResolver` (effect arithmetic only) returning `EffectDescriptor` value objects; all FMOD/Focus/RPC side-effects and NGO ownership stay in the adapter. After this epic the power arithmetic is POCO-testable.
**FRs covered:** FR11

### Epic 5: Wave 4 (HELD) — network-critical
Held for last by design (not blocked): completing the `GameLoopMachine` index-ownership move plus the defensive wire-format guard. Smallest diffs, maximum review; `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` gateway stay verbatim (NFR5). **Restructured by a party-mode review** into 5 stories: the full reflection-dispatch refonte was descoped (Steam P2P single-build → no version boundary), the real blocking prerequisite is a host+real-client PlayMode fixture (StartHost is blind to replication skew), and façade removal was split — leaf-façade reconciliation is **non-deferrable salubrity**, only the index façade waits on the ownership move. **Scope decision (resolved):** Epic 5 stays in-scope; if ever deferred, Story 5.2 (leaf-façade reconciliation) still ships to avoid leaving both a POCO core and the singleton graph as permanent debt (NFR7).
**FRs covered:** FR12, FR13

---

## Epic 1: Foundations — characterization harness & Domain boundary

Stand up the safety net and the compiler boundary before any extraction. After a party-mode review (Game Architect / Game Dev / Test Architect), Epic 1 grew from 3 to 5 stories: the wave gate became coverage-based (not a case count), the non-determinism pinning was moved *ahead* of golden capture, a harness-fidelity kill-test and an anti-tautology sentinel were added, and the value objects kept in Phase 0 are now backed by structural-invariant tests. Story order reflects execution order: determinism is pinned before goldens are trusted.

### Story 1.0: Prove the PlayMode harness is faithful before trusting any golden

As a developer (Poyo),
I want a one-shot mutation kill-test proving the existing `StartHost()` harness actually exercises the production `WinningCondition` code path,
So that golden masters are captured on a faithful harness instead of certifying a blurry photo as ground truth.

**Acceptance Criteria:**

**Given** the current PlayMode harness (`VictoryConditionTests`, `NetworkTestHelper`), known to be thin and to contain tests that pass for the wrong reasons (e.g. a `GameManager` added without a `NetworkObject`)
**When** one `WinningCondition`'s verdict is deliberately inverted at the source (e.g. flip a `>` to `<`)
**Then** the harness-driven test for that condition **must turn red**
**And** if it stays green, the harness does not traverse the code under test and Epic 1 is blocked until the harness is fixed
**And** this kill-test is a documented, repeatable gate, not a throwaway check

### Story 1.1: Pin and quarantine the non-`Random` non-determinism sources (§3b)

As a developer,
I want the non-`Random` non-determinism sources pinned or quarantined before any golden is captured,
So that golden masters are reproducible and no later wave can turn the gate flaky.

**Acceptance Criteria:**

**Given** §3b lists three sources beyond `Random`
**When** this story lands
**Then** the vote-insertion order (`VoteState.cs:162-166`, §3b C) is pinned by a test fixing snapshot construction order
**And** the `AwakeningState` frame-timed randomness (`AwakeningState.cs:250`, §3b B) is explicitly flagged and left in PlayMode, ungoldened, with a test/assertion proving **no golden case in Story 1.2 depends on `AwakeningState`** (isolation, not just a note)
**And** the role-pool iteration order (§3b A) is **frozen now** with an explicit, deterministic ordering — the earlier conditional "freeze IF role-attribution is golden-tested later" AC is removed as undecidable at close; freezing now costs almost nothing and removes the latent debt

### Story 1.2: Golden masters of the 4 WinningConditions, derived from a boundary matrix

As a developer,
I want every `WinningCondition`'s current verdict pinned as golden-master tests derived from a branch×condition matrix,
So that any later extraction that changes behavior fails loudly, with traceability to the exact missing branch.

**Acceptance Criteria:**

**Given** the harness is proven faithful (Story 1.0) and the substrate is deterministic (Story 1.1)
**When** the golden suite is authored
**Then** the case set is **derived from a boundary matrix** annexed to the story (per condition: each boolean clause × {true,false}, each cardinality boundary {0,1,n}, each non-NV field read, each early-return) — the matrix is the gate artifact; "~23" is a footnote estimate, not a target
**And** `WOmniscienceHackedCharacter` (4-way conjunction + double lookup + non-NV `hackedCharacterClientId`) and `WMarginalIsChainedWin` (zero existing coverage) get the tightest, explicitly-enumerated coverage
**And** each case encodes the **current** verdict as an `Assert`, tagged `[Category("GoldenMaster")]`
**And** vacuously-true empty-list cases are captured as-is but tagged `[Category("VacuousTruth")]`, kept distinct so a zero-cardinality regression in Epic 2 is immediately legible
**And** each PlayMode case is fully isolated (state reconstructed per case, no shared host state)
**And** the wave gate is a **branch-coverage** check over the 4 conditions (every conditional branch exercised by ≥1 golden), not a green-suite or case-count check

### Story 1.3: Create `CorruptionDuPortail.Domain.asmdef` with the structural skeleton + a permanent purity guard

As a developer,
I want a pure Domain assembly the compiler forbids from referencing Unity/Netcode, guarded permanently,
So that the core cannot silently re-acquire an engine `using` now or in any future PR.

**Acceptance Criteria:**

**Given** the single `Game.asmdef` today
**When** the Domain assembly is created
**Then** `CorruptionDuPortail.Domain.asmdef` exists with `noEngineReferences: true` and `references: []`
**And** it contains only the structural skeleton: `WinningTeam` (enum) and `IWinningConditionEvaluator` (contract whose signature is already satisfied by the 4 existing evaluators) — the `CharacterSnapshot` / `GameSnapshot` value objects are introduced in Story 1.4
**And** `Game.asmdef` references `Domain`, never the reverse
**And** the `noEngineReferences` guard is proven to actually fail the build when a `using UnityEngine` is added to a Domain file (the guard is tested once, not assumed)
**And** a **CI guard** fails any PR that adds an engine reference to `Domain.asmdef` or removes `noEngineReferences: true` — Domain purity is a permanent invariant, not an initial state

### Story 1.4: Birth the snapshot value objects with structural-invariant tests + define the builder equivalence contract

As a developer,
I want `CharacterSnapshot` / `GameSnapshot` born in the Domain with their value-semantics proven, and the `GameSnapshotBuilder` equivalence contract written down,
So that the differential tests of Epic 2 cannot be silently undermined by a value-object equality bug or a tautological self-referential differential.

**Acceptance Criteria:**

**Given** the Domain asmdef exists (Story 1.3)
**When** the snapshot value objects are added
**Then** `CharacterSnapshot` and `GameSnapshot` live in `Domain` with no engine `using`
**And** ~5–6 structural-invariant tests prove value-by-value equality (every field included in `Equals`/`GetHashCode`), immutability (no leaking setter or mutable collection by reference), and field round-trip — without any live mapping
**And** the **`GameSnapshotBuilder` equivalence contract** is documented as a foundation invariant (implementation lands in Wave 1/Epic 2): *the builder must produce a snapshot such that `evaluator(snapshot)` yields a verdict bit-for-bit identical to the current in-engine evaluation*
**And** an **anti-tautology mutation-sentinel** is established: deliberately corrupting one snapshot field (e.g. flipping `hackedCharacterClientId`) must cause at least one differential case to go red — proving the Epic 2 differential is not blind to a lying mapping
**And** these are the highest-risk blocking gates for Epic 1 closure (faithful harness in 1.0 + non-blind differential here)

### Story 1.5: Characterization golden masters for `VoteTally` and `ChainingResolver`

As a developer,
I want `VoteTally` and `ChainingResolver` current behavior pinned as golden masters before Epic 2 extracts them,
So that those Wave 1 extractions are behavior-preserving by proof, not by hope — especially `ChainingResolver`, whose output several winning conditions depend on.

**Acceptance Criteria:**

**Given** the second party-mode review flagged that Epic 2 would otherwise extract `VoteTally` (FR5) and `ChainingResolver` (FR6) with **no characterization tests** — the Test Architect rated `ChainingResolver` the single highest-risk extraction of the whole refactor (zero existing coverage, ordering-sensitive, upstream of multiple conditions)
**When** this story lands (alongside the WinningCondition goldens of Story 1.2, on the faithful harness of Story 1.0)
**Then** `VoteTally` current vote-count → outcome verdicts are pinned as golden masters over a boundary corpus (tie, unanimous, abstention, single voter, zero voters), tagging vacuous cases `[Category("VacuousTruth")]`
**And** `ChainingResolver` is golden-mastered over a corpus that **varies the input order**, not just the input sets — ordering bugs are the dominant failure mode and single-shot goldens miss them
**And** a property-style note records which input permutations *must* be indifferent and which *must* matter, so Epic 2's extraction has an explicit ordering contract to preserve
**And** these goldens become the oracle that lets Epic 2 keep the `ChainingResolver`-vs-live differential running until conditions 2.3–2.6 are all green

**Epic 1 summary:** 6 stories (1.0 → 1.5), all FR1/FR2 coverage plus pre-emptive characterization for FR5/FR6. Blocking gates: harness fidelity (1.0) and anti-tautology sentinel (1.4). Snapshot ownership resolved in favor of Phase-0 birth + invariant tests (Test Architect position) over deferral to Wave 1 (Architect position), to avoid reopening a closed epic while still covering the equality-bug risk. `GameSnapshotBuilder` implementation explicitly deferred to Epic 2; only its contract is fixed here. Story 1.5 was added in the second party-mode review to close a cross-epic characterization gap surfaced while reviewing Epic 2.

---

## Epic 2: Wave 1 — extract victory + loop + vote + chaining

The Wave 1 extraction, hardened by a party-mode review (Game Architect / Game Dev / Test Architect) into 14 stories. Story order is execution order and dependency-safe. Core principles surfaced by the review: the snapshot is **immutable and passed by argument, never stored** (so per-condition migrations stay independent); the differential harness is **test-only / flag-OFF in release** (never doubles prod cost); the anti-tautology sentinel and losslessness are **proven per-condition** (not once upfront); EditMode conversion is **incremental per extraction** (not a big-bang at the end); and `2.1` is the hidden boss — budget it ×2.

### Story 2.1: Field-read inventory + synchronous `GameSnapshotBuilder.FromLiveState` + losslessness battery

As a developer,
I want a server-side builder that captures a complete, synchronous snapshot of live state before any await,
So that every condition can be evaluated off an immutable snapshot whose fidelity to live state is proven, with no NetworkVariable tearing across a frame.

**Acceptance Criteria:**

**Given** the `GameSnapshotBuilder` contract fixed in Story 1.4 and the snapshot value objects from 1.4
**When** the builder is implemented
**Then** an **exhaustive field-read inventory** of all 4 winning conditions is produced first; the snapshot captures the **union** of those reads plus derived state (chaining, votes, anomaly) — the "≥7 fields" figure is a floor, not the target
**And** `FromLiveState` is **synchronous** and provably runs before any `await`: enforced structurally (a reflection/analyzer test asserting it is not `async` and does not return `UniTask`) **or** by a timing sentinel (a counter proving no yield/await occurred between server entry and capture)
**And** `hackedCharacterClientId` is read from the **live `POmniscience`** instance, not the serialized `Role`
**And** a losslessness battery proves the builder is lossless per inventoried field; identity reads (`GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100`) are preserved verbatim so simulated bots map identically to the runtime
**And** the builder is green standalone (snapshot-vs-live differential over Epic 1 goldens) before any condition is migrated

### Story 2.2: Dual-signature `CheckCondition(snapshot)` + stateless differential harness

As a developer,
I want both `CheckCondition()` and `CheckCondition(snapshot)` to coexist behind a test-only differential harness,
So that each condition can be migrated and proven equivalent without changing prod behavior or cost.

**Acceptance Criteria:**

**Given** the builder of 2.1
**When** the dual signature is introduced
**Then** `virtual bool CheckCondition(GameSnapshot s) => CheckCondition();` is added to `WinningCondition` (default delegates to the old pull)
**And** the differential harness (`Assert.AreEqual(condition.CheckCondition(), condition.CheckCondition(snapshot))`) is **stateless**: the snapshot is immutable, passed by argument, never cached in a shared/`static` field — so migrating one condition cannot move state under another
**And** the differential runs **in the test harness only**; if any in-prod canary exists it is behind a flag **OFF by default**, with a test asserting the flag is OFF in release builds (no double-evaluation shipped to players)
**And** the harness exposes a **per-field-parameterized mutation-sentinel** mechanism (from Story 1.4) ready to be instantiated per condition in 2.3–2.6

### Story 2.3: Migrate `WMarginalIsChainedWin` (easiest, zero prior coverage)

As a developer,
I want `WMarginalIsChainedWin` switched to the snapshot signature with its differential proven non-blind,
So that the first migration validates the whole strangler mechanism on the simplest condition.

**Acceptance Criteria:**

**Given** the harness of 2.2 and the chaining goldens of Story 1.5
**When** the condition is migrated in a single commit
**Then** a **field-read trace** instruments `CheckCondition()` to capture exactly the getters this condition touches, and each traced field is asserted to have a losslessness test (2.1) before the dual-signature switch
**And** a **per-condition mutation-sentinel** proves that corrupting each field this condition reads turns its differential red
**And** the differential is green across the full matrix; one red differential halts the migration
**And** the commit is shippable on its own (NFR7)

### Story 2.4: Migrate `WAnomalyCorruption`

As a developer,
I want `WAnomalyCorruption` switched to the snapshot signature under the same proof regime,
So that the second-easiest condition lands shippable per commit.

**Acceptance Criteria:**

**Given** the regime established in 2.3 (field-read trace, per-field losslessness, per-condition sentinel, full differential)
**When** the condition is migrated in a single commit
**Then** all four guards (trace, losslessness, sentinel, green differential) pass for `WAnomalyCorruption`
**And** the commit is shippable on its own

### Story 2.5: Migrate `WChosenChainedAllAnomaly`

As a developer,
I want `WChosenChainedAllAnomaly` switched to the snapshot signature,
So that the chaining-dependent condition is migrated with its chaining reads proven lossless.

**Acceptance Criteria:**

**Given** the regime of 2.3 and the `ChainingResolver` goldens of Story 1.5
**When** the condition is migrated in a single commit
**Then** the field-read trace confirms its chaining/derived reads are all covered by losslessness tests; any newly discovered field reopens 2.1's inventory rather than being silently defaulted
**And** the per-condition sentinel and full differential are green
**And** the commit is shippable on its own

### Story 2.6: Migrate `WOmniscienceHackedCharacter` (hardest — 4-way conjunction)

As a developer,
I want `WOmniscienceHackedCharacter` switched to the snapshot signature with each conjunction term independently sentinel-guarded,
So that the highest-complexity condition cannot pass with dead terms.

**Acceptance Criteria:**

**Given** the regime of 2.3 and the dedicated `hackedCharacterClientId` losslessness test (2.1)
**When** the condition is migrated in a single commit
**Then** **four** mutation-sentinels are instantiated — one per term of the 4-way conjunction — each proving its differential goes red when its term is corrupted (no term may be silently dead)
**And** both branches of the double lookup (found / not-found) are exercised
**And** the full differential is green and the commit is shippable on its own

### Story 2.7: Cut the old signature + promote `IWinningCondition` into Domain

As a developer,
I want the legacy `CheckCondition()` removed and `IWinningCondition` promoted into the Domain assembly,
So that conditions become POCO-facing with no `using GameLogic` pull.

**Acceptance Criteria:**

**Given** all four conditions migrated and green (2.3–2.6)
**When** the cut commit lands
**Then** the old `CheckCondition()` signature is removed (or made abstract) and `IWinningCondition` is promoted into `Domain`
**And** the cut commit touches **no** `.golden` / expected fixture — only the call target; a hash of the golden suite (expected + assertions) is equal before and after the cut
**And** the legacy signature is feature-flagged rather than hard-deleted in the same commit where the differential still needs it (preserve the proof one beat longer than the thing it proves)

### Story 2.7b: Swap the differential oracle to the frozen golden + re-point the sentinel

As a developer,
I want the now-orphaned dual-source differential replaced by the frozen Phase-0 goldens as the oracle,
So that the safety net keeps comparing against a real reference instead of self-confirming after the cut.

**Acceptance Criteria:**

**Given** the cut of 2.7 removed the old pull that the differential compared against
**When** this story lands
**Then** the dual-source differential is retired and the frozen golden vectors (Epic 1) become the standing oracle for the snapshot signature
**And** the anti-tautology mutation-sentinel is **re-pointed** at this new oracle so it does not become decorative
**And** a check proves the post-cut suite still fails on a deliberate verdict mutation (the net still bites)

### Story 2.8: Extract `VictoryEvaluator` (the loop) + migrate its tests to EditMode

As a developer,
I want the win-team loop extracted as a POCO `VictoryEvaluator` over snapshots,
So that victory evaluation runs as fast EditMode tests.

**Acceptance Criteria:**

**Given** the cut is complete (2.7/2.7b) and conditions are POCO-facing
**When** `VictoryEvaluator.Evaluate(IReadOnlyList<CharacterSnapshot>)` is extracted into `Domain`
**Then** it returns the win-team mapping by calling `IWinningCondition.CheckCondition(snapshot)`; it never triggers a state transition (NFR4 — returns a decision the adapter applies)
**And** `isFake` is set at the mapping source, not recomputed in the POCO (bots must not be mis-filtered)
**And** the victory tests are migrated PlayMode → EditMode **in this same commit** (incremental, not deferred to 2.12)

### Story 2.9: Extract `VoteTally` + migrate its tests to EditMode

As a developer,
I want vote counting extracted as a POCO `VoteTally`,
So that vote-outcome logic is fast EditMode-testable.

**Acceptance Criteria:**

**Given** the `VoteTally` goldens of Story 1.5 and the pinned vote-insertion order (Story 1.1, §3b C)
**When** `VoteTally` is extracted into `Domain`
**Then** it reproduces the golden vote-count → outcome verdicts bit-for-bit, respecting the pinned insertion order as its contract
**And** it returns a decision only (NFR4); no transition, no RPC
**And** its tests are migrated PlayMode → EditMode in this same commit

### Story 2.10: Extract `ChainingResolver` + ordering property tests + migrate to EditMode

As a developer,
I want chaining resolution extracted as a POCO `ChainingResolver` with its ordering contract enforced,
So that the highest-risk extraction is proven order-correct before downstream conditions rely on it.

**Acceptance Criteria:**

**Given** the order-varied `ChainingResolver` goldens and the permutation contract from Story 1.5
**When** `ChainingResolver` is extracted into `Domain`
**Then** it reproduces the goldens across the order-varied corpus
**And** property tests assert invariance for permutations that *must* be indifferent and sensitivity for permutations that *must* matter (per the 1.5 contract)
**And** the `ChainingResolver`-vs-live differential is kept running until conditions 2.3–2.6 are all green, then retired
**And** its tests are migrated PlayMode → EditMode in this same commit

### Story 2.11a: Characterize the `GameLoopMachine` state-transition effect ordering (PlayMode golden, before refactor)

As a developer,
I want the load-bearing transition ordering pinned as a PlayMode golden on the *current* code before any extraction,
So that the extraction has a faithful sequence net rather than one written alongside the change it guards.

**Acceptance Criteria:**

**Given** client listeners react to `currentGameStateIndex.OnValueChanged` directly (`BoardCameraManager.cs:53`, `RoomFog.cs:37`, `LightManager.cs:14`)
**When** the characterization test is authored against the current `GameManager`
**Then** it captures an **ordered event journal** asserting `OnEndStateClient(stateN)` is fully drained **before** the `currentGameStateIndex.Value` write, that the write fires `OnValueChanged`, and that `OnStartStateClient(stateN+1)` runs after — asserting total order, not just index values
**And** this golden is green on the current (pre-refactor) code

### Story 2.11b: Extract `GameLoopMachine` arithmetic under the sequence net (2-phase; remainder HELD to Epic 5)

As a developer,
I want only the `GameLoopMachine` advance/rewind arithmetic extracted as a POCO, leaving the NetworkVariable ownership in the adapter,
So that the easy math becomes EditMode-testable without opening the network-critical index ownership (HELD to Epic 5).

**Acceptance Criteria:**

**Given** the sequence golden of 2.11a is green
**When** the arithmetic (`NextGameState` / `PreviousGameState` / `SwitchGameState` index math, day-pass / first-loop detection) is extracted into `Domain`
**Then** `ignoreGameLoop` becomes a call parameter / internal state, not a global mutable field
**And** the adapter integration preserves the `OnEnd → write currentGameStateIndex.Value → OnStart` ordering, re-verified green against the 2.11a journal
**And** the POCO returns a decision; the adapter performs the write and fires events (NFR4)
**And** this is explicitly the 2-phase split: full index ownership + reflection RPC dispatch remain HELD to Epic 5

### Story 2.12: Mop-up — convert remaining victory/chaining PlayMode tests still coupled to the adapter

As a developer,
I want any victory/chaining tests that could not migrate earlier (still coupled to the 2-phase `GameLoopMachine`) converted where now possible,
So that EditMode coverage is maximized and only genuinely network-observable tests remain in PlayMode.

**Acceptance Criteria:**

**Given** extractions 2.8–2.11b are complete and most tests already migrated incrementally
**When** the mop-up runs
**Then** only the leftover tests blocked by the 2-phase machine are converted, with no big-bang rewrite
**And** tests that are genuinely network-observable (transition ordering, simulated-client gateway) remain in PlayMode by design
**And** the full EditMode + PlayMode suite is green (NFR6 gate for entering Wave 2 / Epic 3)

**Epic 2 summary:** 14 stories (2.1, 2.2, 2.3–2.6, 2.7, 2.7b, 2.8, 2.9, 2.10, 2.11a, 2.11b, 2.12), covering FR3–FR8. Strangler order: build (2.1) → dual-signature harness (2.2) → migrate easy→hard one commit each (2.3–2.6) → cut + oracle swap (2.7/2.7b) → extract cores with incremental EditMode (2.8–2.10) → sequence-golden-then-extract GameLoopMachine (2.11a/2.11b) → mop-up gate (2.12). Hidden boss: 2.1 (budget ×2). Highest-risk extraction: 2.10 `ChainingResolver` (mitigated by Story 1.5 order-varied goldens + property tests). `GameLoopMachine` is intentionally 2-phase; full ownership + RPC dispatch HELD to Epic 5.

---

## Epic 3: Wave 2 — determinism & role distribution

Introduce the seedable randomness port and extract a deterministic `RoleDistributor`. The role-pool iteration order was already frozen in Story 1.1 (§3b A) so Phase-0 goldens stayed stable; this epic consumes that frozen order and formalizes the production distributor. Because the current assignment uses `UnityEngine.Random`, behavior-preservation requires capturing the current assignment under an injected seed before extraction.

### Story 3.1: Add the `IRandomProvider` port with Unity-backed and seeded implementations

As a developer,
I want a seedable `IRandomProvider` port the core depends on instead of `UnityEngine.Random`,
So that role distribution becomes reproducible and the core stays engine-free.

**Acceptance Criteria:**

**Given** `CharacterManager` currently calls `UnityEngine.Random` directly
**When** the port is added
**Then** `IRandomProvider` (`Next(int max)`, seedable) lives in `Domain`
**And** a Unity-backed implementation lives in the adapter (injected in prod) and a deterministic seeded implementation is available to tests
**And** no `Domain` type references `UnityEngine.Random` (NFR2)

### Story 3.2: Characterize current role assignment under a fixed seed (golden master)

As a developer,
I want the current role-assignment behavior pinned as a golden master under an injected seed,
So that the extracted `RoleDistributor` can be proven behavior-preserving despite the logic being randomized.

**Acceptance Criteria:**

**Given** the frozen role-pool ordering (Story 1.1) and the `IRandomProvider` seam (3.1)
**When** the characterization runs with a fixed seed over representative player-count / settings combinations
**Then** the resulting role→player assignments are captured as golden masters, tagged `[Category("GoldenMaster")]`
**And** the corpus covers boundary cases: minimum players, maximum players, settings that over/under-subscribe the pool
**And** these goldens are stable across runs (proving the frozen order + seeded RNG fully determinize the assignment)

### Story 3.3: Extract the deterministic `RoleDistributor` + migrate tests to EditMode

As a developer,
I want role assignment extracted as a POCO `RoleDistributor` driven by `IRandomProvider` and the frozen pool order,
So that distribution is deterministic, fast-testable, and engine-free.

**Acceptance Criteria:**

**Given** the goldens of 3.2
**When** `RoleDistributor` is extracted into `Domain`
**Then** it assigns roles from settings + player list using `IRandomProvider` and the explicit frozen pool ordering (no `Dictionary.Keys.ToList()` drift)
**And** it reproduces the 3.2 goldens bit-for-bit under the same seed
**And** it returns the assignment as data; the adapter applies it (NFR4)
**And** the assignment tests are migrated PlayMode → EditMode in this commit
**And** the full suite is green (NFR6 gate before Wave 3)

---

## Epic 4: Wave 3 — power resolution

Extract the power **effect arithmetic** as a POCO `PowerResolver` returning an ordered list of `EffectDescriptor` value objects. Every side-effect — FMOD, Focus, RPC dispatch, NGO ownership (`NetworkObject.TrySetParent`, `ownerClientId`) — stays in the adapter. The dominant risk is `GetSafeRpcTarget` leaking into the "pure" resolver (12+ call sites including `Power.cs:158`, `ChatManager.cs:174`, `LobbyPlayerInfoHolder.cs:79`, all concrete powers); the resolver must return a descriptor and let the adapter dispatch.

> **Restructured by a party-mode review** (Game Architect / Test Architect / Game Dev / Game Designer). Two decisions reshaped it: (1) **decompose per-power, not per-layer** — the powers (`POmniscience` hack, `PVision`, `PEntrapment`, `PCorruption`) are heterogeneous, so a single "extract THE PowerResolver" story would be a big-bang switch tested in one block. Instead: one foundation story that designs the vocabulary against ALL powers up front, then one story per concrete power (easy→hard), then cleanup — mirroring the per-condition decomposition of Epic 2. (2) **The `EffectDescriptor` vocabulary is forged on the hardest power (Omniscience) but implemented on the easiest first (Vision)** — the foundation story (4.0) inventories all four powers and freezes the descriptor on their union *before* any extraction, so the extraction order carries no design risk (R1 absorbed by 4.0); only the execution risk (R2) is ordered, and that says easy→hard.

**Cross-cutting design invariants (all stories):**
- `EffectDescriptor` is a **closed, discriminated value type** (`sealed record` / type-sum — e.g. `PlaySound(soundId)`, `UnfocusAll`, `NotifyClients(targetAudience, payload)`, `DecrementUses(n)`, `TransferOwnership(netObjId, ownerSlot)`, `RequestCharacterRefresh`), **not** a god-object with nullable fields. A power resolves to an **ordered `IReadOnlyList<EffectDescriptor>`**; the adapter dispatches by **exhaustive `switch`** (the compiler flags an undispatched variant). The order is carried by the list, never by `if`-ordering in the adapter (else decision logic leaks back into the adapter, breaking NFR4).
- **Zero transport/engine types in `Domain`:** no `clientId` / `GetSafeRpcTarget` / `RpcParams` / FMOD `EventReference` / `NetworkObject`. The resolver emits *intentions* (`NotifyClients(All / Owner / Specific(logicalSlot))`, `PlaySound(soundId)`); the adapter translates them verbatim — `GetSafeRpcTarget` + the `clientId >= 100` bot-interception live **only** there (NFR5). A single `clientId` mention in `Domain` is a revert.
- `powerUseLeft.Value -= 1` and `AskForUpdateAllCharactersRpc` are **common invocation plumbing**, not per-power resolution — they stay in the adapter `OnUsedServer` (no `NetworkVariable` in the POCO). Pinned in the golden trace as effects *of the adapter*.
- **Characterization mordent à l'étage intention+dispatch via spies** (`IPowerAudio` / `IFocusSink` / `INetObjectGraph` / `IRpcDispatch`), **never the real FMOD effect** (native black-box → flaky). The golden is an ordered `List<RecordedEffect>` trace, pinned via the discovery-run technique. **The ORDER of bricks is asserted explicitly**, not just set membership.
- **NFR5 proof gate:** at least one golden per RPC-emitting power must carry a `clientId >= 100` target and assert the **interception path** (`intercepted-as-bot` vs `sent-over-wire`) — else NFR5 is hoped, not proven.

**Explicitly out of scope → deferred to Epic 5 (HELD), marked in the relevant ACs:**
- **Ownership-replication parity** (`TrySetParent` / `ownerClientId` as seen on a remote client) — `StartHost` is blind to it (host==server, RTT=0).
- **The hack target's PERCEPTION** (what the victim of `POmniscience` sees/hears = deduction information) — needs the host+real-client fixture (Story 5.0).

**Game-feel guardrails (Game Designer):**
- The adapter dispatch loop is **SYNCHRONOUS** — no `await`/yield between bricks (the imperative→list refactor must not shift perceived timing). Asserted explicitly.
- A manual **A/B human playtest** (before/after build) on `PCorruption` + `PEntrapment` (coupled FMOD+Focus choreography), since an intention-level golden is blind to a frame-delta the ear catches.

### Story 4.0: Inventory + define `EffectDescriptor` (union of all powers) + seams + global golden

As a developer,
I want the per-power side-effect inventory done, the `EffectDescriptor` vocabulary frozen on the union of all four powers, the four observation seams in place, and a global golden trace captured on the current code,
So that the vocabulary is designed for maximum diversity before any extraction and every later per-power story has a faithful oracle.

**Acceptance Criteria:**

**Given** effect logic lives inside `Power` / `PowerManager` and the concrete powers, mixing arithmetic with FMOD / Focus / RPC / ownership side-effects
**When** the foundation lands
**Then** a **line-by-line side-effect inventory** of `Power.cs` (base) + the four concrete powers is produced; each effect is classified *decision (the what)* vs *transport/IO (the how)* and mapped to a descriptor brick — the inventory IS the spec of `EffectDescriptor`
**And** `EffectDescriptor` (closed discriminated value type, immutable, value-equatable, **no** `EventReference` / `RpcParams` / `clientId` / `NetworkObject`) is defined in `Domain` on the **union of all four powers** (Omniscience's hack analyzed up front, so a later power cannot force a mid-wave value-object refactor) with structural-invariant tests (value equality, immutability)
**And** the four observation seams (`IPowerAudio` / `IFocusSink` / `INetObjectGraph` / `IRpcDispatch`) are introduced in the adapter with their **default behavior verbatim** (no behavior change — the full PlayMode suite stays green)
**And** a **global golden trace** (ordered intention+dispatch `RecordedEffect` list per power, incl. at least one `clientId >= 100` case per RPC-emitting power) is captured on the CURRENT code as the standing oracle — no `PowerResolver` exists yet

### Story 4.1: Migrate `PVision` (simplest — proves the golden+spy pattern end-to-end)

As a developer,
I want `PVision`'s effect arithmetic extracted into `PowerResolver` and its adapter re-pointed to dispatch the descriptors,
So that the lowest-variance power rods the extraction pattern before the harder powers.

**Acceptance Criteria:**

**Given** the 4.0 `EffectDescriptor`, seams, and global golden
**When** `PVision`'s resolution is moved into the `PowerResolver` POCO (decision-only, returns an ordered `IReadOnlyList<EffectDescriptor>`, NFR4)
**Then** the resolver references **no** `NetworkVariable` / `RpcParams` / `GetSafeRpcTarget` / FMOD / Focus / `NetworkObject`
**And** the adapter dispatches the descriptors via an exhaustive `switch`, **synchronously** (no yield between bricks), preserving the live order
**And** `PVision`'s golden trace passes **unchanged** (bit-for-bit), and the focus returns to the correct state after use
**And** the `PVision` resolver tests run as **EditMode**

### Story 4.2: Migrate `PEntrapment`

As a developer,
I want `PEntrapment` switched to the resolver+dispatch pattern under the same proof regime,
So that the second power lands shippable per commit.

**Acceptance Criteria:**

**Given** the regime established in 4.1
**When** `PEntrapment`'s resolution moves into the POCO and the adapter dispatches its descriptors
**Then** all guards pass (no transport/engine type in the resolver, exhaustive synchronous dispatch, golden trace unchanged incl. its `clientId >= 100` case, EditMode tests)
**And** the commit is shippable on its own
**And** a manual A/B human playtest (before/after) confirms no perceived FMOD+Focus timing shift

### Story 4.3: Migrate `PCorruption`

As a developer,
I want `PCorruption` switched to the resolver+dispatch pattern,
So that the third power lands with its coupled FMOD+Focus choreography preserved.

**Acceptance Criteria:**

**Given** the regime of 4.1
**When** `PCorruption`'s resolution moves into the POCO and the adapter dispatches its descriptors
**Then** all guards pass (resolver purity, exhaustive synchronous dispatch, golden trace unchanged incl. its `clientId >= 100` case, EditMode tests)
**And** the commit is shippable on its own
**And** a manual A/B human playtest (before/after) confirms no perceived FMOD+Focus timing shift

### Story 4.4: Migrate `POmniscience` (hardest — the hack, last; double golden) `# REVIEW-REQUIRED`

As a developer,
I want `POmniscience` (the hack) switched to the resolver+dispatch pattern last, with the vocabulary already proven on three powers,
So that the highest-complexity power is extracted with maximum accumulated information and the hack's emitter+target effects are both pinned.

**Acceptance Criteria:**

**Given** the regime proven on 4.1–4.3 and the union vocabulary of 4.0
**When** `POmniscience`'s resolution moves into the POCO and the adapter dispatches its descriptors
**Then** all guards pass (resolver purity, exhaustive synchronous dispatch, EditMode tests) and **no** descriptor variant is left undispatched
**And** the golden is a **double trace**: the emitter-side effects AND the `NotifyClients`-to-target intention (what the hack tells the target's client) are both pinned bit-for-bit
**And** the `hackCharacterClientId` ownership/target reads stay adapter-side; `GetSafeRpcTarget` / `clientId >= 100` verbatim (NFR5)
**And** it is explicit that the **target's on-client PERCEPTION** parity (and any ownership-replication parity) is **out of scope, deferred to Epic 5 (HELD)** — the StartHost net proves emitter intention+dispatch only

### Story 4.5: Cleanup — remove the legacy inline effect path

As a developer,
I want the now-dead legacy inline effect code in `Power.OnUsed` / concrete powers removed once all four are re-pointed,
So that no power carries both the POCO resolver and the old inline branch (NFR7).

**Acceptance Criteria:**

**Given** all four powers re-pointed (4.1–4.4)
**When** the cleanup lands
**Then** the legacy inline effect-resolution code is removed by destructive deletion; the compiler enumerates any remaining call site
**And** no orphan side-effect remains (a static/grep absence proof of the old inline effect symbols)
**And** the full golden/trace suite passes **unchanged** and a boot smoke-test (start→use a power→finish without exception) is green
**And** the full EditMode + PlayMode suite is green (NFR6 gate before Wave 4)

**Epic 3 & 4 summary:** Epic 3 = 3 stories (3.1 port, 3.2 seeded characterization, 3.3 deterministic extraction), FR9–FR10. Epic 4 = **6 stories** (4.0 foundation: inventory + union `EffectDescriptor` + seams + global golden; 4.1 `PVision`; 4.2 `PEntrapment`; 4.3 `PCorruption`; 4.4 `POmniscience` [hardest, last, double golden, REVIEW-REQUIRED]; 4.5 cleanup), FR11. Restructured by a party-mode review into a per-power decomposition (vocabulary forged on the hard case in 4.0, implemented easy→hard). Same proven pattern: characterize-then-extract, decision-only POCO (NFR4), side-effects + simulated-client gateway verbatim in the adapter (NFR5); ownership-replication + hack-target perception deferred to Epic 5 (HELD).

---

## Epic 5: Wave 4 (HELD) — network-critical

Restructured by a party-mode review (Game Architect / Game Dev / Test Architect). Two decisions reshaped it: (1) the full reflection-dispatch refonte was **descoped** — the confirmed Steam **P2P single-build** model means host and clients run the same downloaded build, so there is no version boundary and the in-flight-packet fragility is theoretical; only a defensive wire-format guard remains in-scope, the real refonte is a separate `feat(net): versioned state-dispatch protocol` effort. (2) The real blocking prerequisite is a **host + real-client PlayMode fixture** — `StartHost` has RTT=0 and host==server, so it is structurally blind to the replication skew that the index-ownership move (5.3) risks. Story 5.3 is the single highest-risk story of the entire refactor (silent multiplayer desync: host advances, a client freezes in the old state with no exception). `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` gateway are preserved verbatim throughout (NFR5).

**Unblocking restructure (2026-06-11, issue #50):** the autonomous night run found 5.0 blocked by the process-global singleton graph (`GameManager.instance`, `CharacterManager.instance`, `NetworkManager.Singleton`): a real second in-process client's replicated managers are *destroyed at Awake* by the duplicate guard, and `NetworkAction` cross-wires both clients' handlers on the shared `CustomMessagingManager`. Stories **5.0a–5.0e** below de-singletonise the three targets (per-`NetworkManager` registry + primary façade, behavior-preserving by construction since production has exactly one `NetworkManager`). Source of truth: `_bmad-output/refactor-architecture-desingleton.md`. Scope fence: the 8 other NetworkBehaviour singletons follow the established pattern later, on demand, when the fixture proves one blocking.

### Story 5.0a: Mechanical `NetworkManager` resolution (zero observable change)

As a developer,
I want every `NetworkManager.Singleton` read inside NetworkBehaviours, GameStates, and the wire-reference wrapper replaced by the locally-resolvable `NetworkManager`,
So that the trivially-injectable population is migrated first, at zero behavioral risk.

**Acceptance Criteria:**

**Given** `NetworkBehaviour.NetworkManager` falls back to `NetworkManager.Singleton` when the object has no owner (strictly identical in production)
**When** the ~14 mechanical sites are migrated
**Then** NetworkBehaviours use the inherited `NetworkManager` (`GameManager.cs:400`, `CharacterManager.cs:342,358`, `PVisionOfTheImpossible.cs:109`)
**And** GameStates use the already-injected `gameManager.NetworkManager` (`AwakeningState.cs:184,325`, `TakeDownThePortalState.cs:123,171,241,258`, `LobbyState.cs:53,58,62`)
**And** `NetworkBehaviourReferenceWrapper.TryGet` gains an optional `NetworkManager` parameter (default `Singleton` — unchanged for existing callers)
**And** pre-game bootstrap (MainMenu, lobby, transports), dev scripts, and the scene-local Mono `PowerManager` are explicitly **not** touched
**And** the full EditMode + PlayMode suite passes unchanged, console clean

### Story 5.0b: `NetworkAction` NetworkManager injection (the cross-wiring fix)

As a developer,
I want `NetworkAction` to register/invoke/receive through an injected `NetworkManager` instead of 36 hardwired `Singleton` reads,
So that two in-process clients can no longer overwrite each other's named-message handlers.

**Acceptance Criteria:**

**Given** the `NetworkBehaviour`-bound constructor already receives the context needed to derive the owning manager (`_networkBehaviour.NetworkManager`)
**When** the plugin is migrated
**Then** all internal `Singleton` reads route through one private resolved field; the bound ctor derives it, the unbound ctor keeps the `Singleton` fallback (current behavior preserved verbatim)
**And** `GameManager`'s two global field-initializer actions (`onGameStarted`, `onNewDayPassed`) are only rebound in a **separate commit** that first characterizes current registration timing; if timing cannot be proven identical, they keep the fallback form
**And** the full suite passes unchanged, console clean

### Story 5.0c: `CharacterManager` per-NetworkManager registry + primary façade

As a developer,
I want `CharacterManager` resolvable via `CharacterManager.For(NetworkManager)` with `instance` reduced to a primary-NM façade,
So that a second client's replica coexists instead of being destroyed at Awake — without changing the bot flow by one byte.

**Acceptance Criteria:**

**Given** the Awake guard currently destroys any duplicate and the statics carry `GetSafeRpcTarget` / `IsLocalOrSimulated` access for the whole bot flow
**When** the registry pattern is applied
**Then** a **probe test first** proves whether `NetworkObject.NetworkManager` is resolvable in `Awake()` of a replicated object (fallback design documented in the architecture doc §3.1 if not)
**And** same-NM duplicates are still destroyed (today's semantics); foreign-NM replicas are registry-only — never destroyed, never touching `instance`
**And** the ~78 call sites migrate in batches of ~10 files (powers → `Character` → POCO power objects via `_ownerPower` → GameLogic), one gated commit per batch; UI call sites stay on the façade
**And** `GetSafeRpcTarget` / `IsLocalOrSimulated` bodies are byte-identical (NFR5) — only callers' access paths change
**And** the full suite + boot smoke-test pass unchanged after every batch

### Story 5.0d: `GameManager` per-NetworkManager registry + primary façade (the monster)

As a developer,
I want the same registry + façade pattern on `GameManager` and its ~95 gameplay call sites migrated,
So that the largest singleton (161 usages / 67 files) no longer pins the whole game graph to one process-global instance.

**Acceptance Criteria:**

**Given** the pattern is proven on `CharacterManager` (5.0c)
**When** `GameManager` is migrated
**Then** the registry + NM-aware Awake guard + façade land first as one commit, observably no-op in production
**And** gameplay call sites (Powers/Characters/Board/GameLogic) migrate in gated batches; GameStates keep using their injected `gameManager`
**And** the ~66 UI/presentation usages across 28 files **stay on the façade** (documented follow-up: a dedicated UI-injection pass, out of this epic)
**And** the 2.11a sequence golden and all Waves 1–3 goldens pass **unchanged**
**And** the full suite + boot smoke-test pass unchanged after every batch

### Story 5.0e: Coexistence gate — two in-process clients without clobber

As a developer,
I want a minimal PlayMode probe spinning up two `NetworkManager`s with replicated `GameManager` + `CharacterManager`,
So that the de-singletonisation is proven sufficient before investing in the full 5.0 fixture API.

**Acceptance Criteria:**

**Given** stories 5.0a–5.0d are merged
**When** the probe runs two in-process `NetworkManager`s and spawns the second client's manager replicas
**Then** no replica is destroyed at Awake
**And** the `instance` façades still point at the primary's objects
**And** `For(nm2)` resolves the second client's instances
**And** a `NetworkAction` bound to the second manager does not hijack the primary's named-message handler
**And** green here is the explicit unblock signal for Story 5.0

### Story 5.0: Build the host + real-client (+ simulated-bot) multi-client PlayMode fixture

As a developer,
I want a PlayMode test fixture with a host, a real in-process remote client, and a simulated bot,
So that replication-skew and `OnValueChanged`-propagation bugs — invisible to `StartHost` — can be caught before the index-ownership move.

**Acceptance Criteria:**

**Given** the current harness is `StartHost`-only (host==server, RTT=0, blind to wire-level replication) and the simulated bot (`clientId >= 100`) never crosses the wire because the host intercepts it
**When** the fixture is built
**Then** it spins up a host + at least one **real in-process client** over a loopback transport, so `NetworkVariable.OnValueChanged` traverses real serialization and a real tick
**And** it can also include a simulated bot to cover the intercepted-dispatch path, but the real client is the load-bearing addition
**And** it exposes a way to record the ordered sequence of `currentGameStateIndex.OnValueChanged` observations **on the remote client**
**And** this fixture should be built early (in parallel with Waves 1–3) — deferring it to Epic 5 turns the HELD into pure risk-postponement, because the index move cannot be safely gated without it

### Story 5.1: Defensive wire-format guard over the RPC dispatch table (EditMode)

As a developer,
I want the `{GameState type → FixedString64Bytes, method → string}` dispatch mapping pinned as a versioned EditMode golden,
So that any future rename/move of a `GameState` class breaks a test instead of silently changing the wire format.

**Acceptance Criteria:**

**Given** `CallStateMethodRpc` / `CallMethodAfterRpc` (`GameManager.cs:304-376`) serialize type/method names as `FixedString64Bytes`
**When** the guard is added
**Then** the current mapping table is snapshotted into a versioned fixture and a breaking-change EditMode test fails if any entry changes
**And** this story performs **no refonte** of the dispatch itself — it is purely defensive (the full reflection-dispatch rewrite is explicitly out of scope, moved to a separate protocol effort)
**And** the test is EditMode (no NGO needed) and runs in CI

### Story 5.2: Reconcile the leaf façades from Waves 1–3 (non-deferrable salubrity)

As a developer,
I want every temporary `instance` façade whose POCO core is already fully extracted removed and its callers rerouted to the core,
So that the codebase is never left carrying both a POCO core and the singleton graph (NFR7), even if the rest of Epic 5 is deferred.

**Acceptance Criteria:**

**Given** the leaf cores extracted in Waves 1–3 (`VictoryEvaluator`, `VoteTally`, `ChainingResolver`, `RoleDistributor`, `PowerResolver`) whose façades no longer carry behavior
**When** each leaf façade is removed
**Then** removal is done by **destructive deletion**: the symbol is deleted and the compiler enumerates every real call site (CS0117/CS0103), which are rerouted to the core — grep is not relied upon
**And** a **static absence proof** (an EditMode assembly-scan test) fails if a removed façade symbol is still referenced anywhere outside the deleting commit
**And** the full golden/differential suite (Waves 1–3) passes **unchanged** — any golden that moves means the façade carried hidden behavior
**And** a boot smoke-test (PlayMode "start and finish a full game without exception") guards the init/lifecycle-order risk that a façade may have hidden
**And** this story is **not deferred** with the rest of Epic 5; it ships as soon as its leaf cores are extracted

### Story 5.3: Complete `GameLoopMachine` index ownership (the hot story)

As a developer,
I want `GameLoopMachine` to fully own the state index while the adapter mirrors it synchronously into `currentGameStateIndex.Value`,
So that the 2-phase split from Story 2.11b is completed without ever desyncing a client.

**Acceptance Criteria:**

**Given** the partial extraction of 2.11b (arithmetic in Domain, NetworkVariable ownership still in the adapter) and the multi-client fixture (5.0)
**When** full ownership moves to the POCO and the adapter mirrors it
**Then** the mirror is **synchronous** — no suspension point (`await`/yield) between "the machine settled the index" and "the adapter writes `currentGameStateIndex.Value`"; the write stays in the same frame and the same order as the 2.11a journal
**And** the PlayMode sequence golden 2.11a passes **unchanged** (not adapted — unchanged); if it must be edited to pass, the invariant is broken
**And** the 2.11a golden is widened into a **parameterized suite** covering: normal N→N+1, **skip/jump** transitions (e.g. a vote forcing an early end), **late-join** (a joining client reads `currentGameStateIndex.Value` and must read the same value the machine owns — the read-time invariant of commit `c72b8d1`), and the terminal transition
**And** on the **remote client** (5.0 fixture), every transition fires `OnValueChanged` exactly once with the correct value — asserted as a full ordered trace, not just the final value (catches the machine jumping two states while the mirror writes once → a missed `OnStartStateClient`)
**And** this is an **atomic** commit (ownership swap cannot be half-done) with maximum review; `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` untouched (NFR5)

### Story 5.4: Remove the final `GameLoopMachine` / index façade

As a developer,
I want the last strangler façade — the one still load-bearing while the adapter mirrored the index — removed now that ownership is complete,
So that no strangler façade remains anywhere (NFR7 DoD).

**Acceptance Criteria:**

**Given** index ownership is fully moved (5.3)
**When** the index/`GameLoopMachine` façade is removed
**Then** it is removed by destructive deletion + static absence proof, like 5.2
**And** the full suite (including the 5.3 parameterized client-trace suite and the 2.11a golden) passes unchanged
**And** the boot smoke-test passes
**And** the NFR7 definition of done is met: **no strangler façade remains; the codebase carries the POCO core only, not the singleton graph**

**Epic 5 summary:** 10 stories (5.0a–5.0e de-singletonisation prerequisites, 5.0 fixture, 5.1 wire-format guard, 5.2 leaf-façade reconciliation, 5.3 index ownership, 5.4 final façade removal), covering FR12 (5.3/5.4) and the descoped FR13 (5.1). Execution order: 5.1 + 5.2 already shipped (#51/#52) → 5.0a → 5.0b → 5.0c → 5.0d → 5.0e (coexistence gate) → 5.0 → 5.3 (hot, atomic, max review) → 5.4. Highest-risk story of the whole refactor: 5.3 (silent desync), gated by the 5.0 real-client fixture + the parameterized client-trace suite. Full reflection-dispatch refonte explicitly out of scope (separate protocol effort). Final gate: full EditMode + PlayMode suite green, no façade remains, boot smoke-test green.
