# Story 1.4: Birth the snapshot value objects with structural-invariant tests + define the builder equivalence contract

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want `CharacterSnapshot` / `GameSnapshot` born in the Domain with their value-semantics proven, and the `GameSnapshotBuilder` equivalence contract written down,
so that the differential tests of Epic 2 cannot be silently undermined by a value-object equality bug or a tautological self-referential differential.

## Acceptance Criteria

**Given** the Domain asmdef exists (Story 1.3)
**When** the snapshot value objects are added
**Then** `CharacterSnapshot` and `GameSnapshot` live in `Domain` with no engine `using`
**And** ~5–6 structural-invariant tests prove value-by-value equality (every field included in `Equals`/`GetHashCode`), immutability (no leaking setter or mutable collection by reference), and field round-trip — without any live mapping
**And** the **`GameSnapshotBuilder` equivalence contract** is documented as a foundation invariant (implementation lands in Wave 1/Epic 2): *the builder must produce a snapshot such that `evaluator(snapshot)` yields a verdict bit-for-bit identical to the current in-engine evaluation*
**And** an **anti-tautology mutation-sentinel** is established: deliberately corrupting one snapshot field (e.g. flipping `hackedCharacterClientId`) must cause at least one differential case to go red — proving the Epic 2 differential is not blind to a lying mapping
**And** these are the highest-risk blocking gates for Epic 1 closure (faithful harness in 1.0 + non-blind differential here)

### Acceptance reading notes (binding — resolves ambiguity before you start)

1. **Pure value-objects, EditMode-only, zero live mapping. This story does NOT build a snapshot from live state.** `GameSnapshotBuilder.FromLiveState` is **Story 2.1** — do not implement it here. Here you create two immutable POCOs and prove their value-semantics with hand-constructed instances. No NGO host, no `GameManager`, no PlayMode.

2. **The field set is FIXED by the architecture — do not invent or omit fields.** [Source: refactor-architecture-poco.md, lines 70–78]
   ```
   CharacterSnapshot {
       ulong        ownerClientId;
       bool         isFake;                    // = ownerClientId.IsFakeClientId(), set AT SOURCE in the mapping (Epic 2) — here it is just a stored bool
       bool         isCorrupted;               // from NetworkVariable<bool>
       bool         isChained;                 // from NetworkVariable<bool>
       FactionType  factionType;               // from Role
       ulong        hackedByOmniscienceTarget; // from POmniscience.hackedCharacterClientId (a PLAIN ulong, NOT a NetworkVariable)
   }
   GameSnapshot { IReadOnlyList<CharacterSnapshot> characters; int day; int currentStateIndex; }
   ```
   These 6 `CharacterSnapshot` fields are exactly the fields the 4 winning conditions read (verified against the live evaluators): `WMarginalIsChainedWin` → `ownerClientId`, `isFake`, `isChained`; `WAnomalyCorruption` → `isFake`, `isCorrupted`; `WChosenChainedAllAnomaly` → `isFake`, `factionType`, `isChained`; `WOmniscienceHackedCharacter` → `ownerClientId`, `hackedByOmniscienceTarget`, plus the *target* character's `isChained` + `factionType`. `StateDescriptor` (line 79) is GameLoop territory — **not** part of this story.

3. **`FactionType` must relocate into the Domain assembly — same pattern as `WinningTeam` in Story 1.3.** `CharacterSnapshot.factionType` is typed `FactionType`, and `CharacterSnapshot` lives in the pure Domain assembly. A pure Domain type cannot reference a `Game`-assembly type, so `FactionType` (currently `Assets/Scripts/Characters/FactionType.cs`, a logic-free enum in namespace `Characters`, no engine `using`) must move into `Domain`. **Keep `namespace Characters` verbatim** (minimal-diff path) → the 28 existing consumers need **zero** edits, because they all live in `Game` or in test assemblies, and `Domain` is `autoReferenced: true` (proven in Story 1.3: the relocated `WinningTeam` resolved across `Game` + both test asmdefs with no explicit reference added, 31/31 green). Move the file GUID-preserving (move the `.cs` **and** `.cs.meta`), do not delete-and-recreate.

4. **Value-semantics are the whole point — implement them deliberately, do not rely on auto-generated reference equality.**
   - Make both types `sealed` and immutable: `readonly` fields (or get-only auto-props), set once via constructor.
   - Override `Equals(object)`, implement `IEquatable<T>`, override `GetHashCode()` — covering **every** field. A field left out of `Equals`/`GetHashCode` is the exact bug this story exists to prevent; the tests below must fail if any field is omitted.
   - `record` / `record struct` is acceptable and idiomatic for value objects (C# 9+, available in Unity 6000.2). If you use a positional `record`, **still** verify the project's C# language version compiles it cleanly (`read_console` after adding) before relying on compiler-generated equality. Either path is fine; the tests are identical.
   - `GameSnapshot.characters` must be a **defensive, read-only** sequence: store as `IReadOnlyList<CharacterSnapshot>` backed by a copy (`new List<...>(input).AsReadOnly()` or `input.ToArray()`), so a caller mutating the original list cannot mutate the snapshot. `GameSnapshot.Equals` must use **sequence equality** over `characters` (order-sensitive), plus `day` and `currentStateIndex`.

5. **The mutation-sentinel in THIS story = field-level equality sensitivity (the differential harness itself is Epic 2).** There is no live differential to turn red yet. What you establish here is the *foundation* that makes the Epic 2 differential non-blind: a test proving that a `CharacterSnapshot` differing in **exactly one** field is `!Equals` to the original — for **every** field, including `hackedByOmniscienceTarget`. If equality is field-sensitive, a later differential keyed on snapshot equality cannot be fooled by a lying mapping that corrupts one field. Document explicitly (in the test file header + the contract doc) that Epic 2 Story 2.1/2.2 inherits this guarantee.

6. **Document the `GameSnapshotBuilder` equivalence contract as a durable artifact, not just a code comment.** Add a short contract section to the architecture/refactor doc set (recommended: append to `_bmad-output/refactor-architecture-poco.md` under a clearly-marked "Story 1.4 — GameSnapshotBuilder equivalence contract" heading, or a sibling `_bmad-output/contracts/gamesnapshotbuilder-equivalence.md`). The contract text: *for any live server state S, `GameSnapshotBuilder.FromLiveState(S)` must produce a snapshot `s` such that for every winning condition `c`, `c.CheckCondition(s)` equals `c.CheckCondition()` evaluated against S — bit-for-bit. The builder reads `hackedByOmniscienceTarget` from the live `POmniscience` instance in `role.powers`, never from a serialized `Role` copy* (the `hackedCharacterClientId` trap, refactor-architecture-poco.md:82). This contract is the spec Story 2.1 implements against.

7. **No production gameplay code changes. No engine `using` in Domain.** The only production move is relocating `FactionType.cs` (logic-free enum). Do not touch the 4 evaluators, `Character`, `Role`, `Power`, or any state. The new POCOs are consumed by nothing yet (Epic 2 wires them).

8. **Keep the Domain purity guard green.** After adding the new Domain types, `DomainPurity` (EditMode, from Story 1.3) must stay green and the assembly must still compile with `noEngineReferences: true`. If you accidentally introduce an engine `using` (e.g. `UnityEngine.Mathf`), the build fails by design.

## Tasks / Subtasks

- [x] **Task 0 — Confirm the safety net is green before any change** (AC: line 1 "Given")
  - [x] Base known-green from Story 1.3 final run minutes earlier on identical code (31/31 PlayMode + 2/2 DomainPurity); re-verified at Task 6.
- [x] **Task 1 — Relocate `FactionType` into the Domain assembly** (AC: line 1, reading-note 3)
  - [x] `git mv` `FactionType.cs` + `.cs.meta` → `Assets/Scripts/Domain/FactionType.cs` (GUID preserved)
  - [x] Kept `namespace Characters` unchanged
  - [x] Compiled clean — see Task 1b correction below
- [x] **Task 1b — CORRECTION: add explicit `Domain` reference to both test asmdefs** (AC: line 1)
  - [x] Reading-note 3's "no test asmdef edit" assumption was **wrong**: `Domain.autoReferenced:true` does NOT propagate to test asmdefs (they have explicit `references` + `overrideReferences`). Story 1.3 only appeared to work because **no test file referenced `WinningTeam` by name**; many test files DO reference `FactionType`, triggering `CS0012` ("defined in an assembly that is not referenced").
  - [x] Added `"CorruptionDuPortail.Domain"` to `Tests.PlayMode.asmdef` and `Tests.Editor.asmdef` references → clean compile
- [x] **Task 2 — Author `CharacterSnapshot` value object** (AC: lines 2–3, reading-notes 2 & 4)
  - [x] `Assets/Scripts/Domain/CharacterSnapshot.cs`, `sealed`, immutable get-only props, 6 fixed fields
  - [x] `IEquatable<CharacterSnapshot>` + `Equals`/`GetHashCode`/`==`/`!=` over ALL 6 fields, no engine `using`
- [x] **Task 3 — Author `GameSnapshot` value object** (AC: lines 2–3, reading-note 4)
  - [x] `Assets/Scripts/Domain/GameSnapshot.cs`, `sealed`, immutable; `characters` = defensive copy wrapped `AsReadOnly()`; sequence-equality over `characters` + `day` + `currentStateIndex`
- [x] **Task 4 — Structural-invariant tests** (AC: line 2)
  - [x] `Assets/Scripts/Tests/Editor/SnapshotValueObjectTests.cs`, `[Category("DomainSnapshot")]`, EditMode — Tests A–E
  - [x] Test B parameterized via `[TestCaseSource]`, one case per field (incl. `hackedByOmniscienceTarget`)
  - [x] `run_tests category: DomainSnapshot` → 14/14 green
- [x] **Task 5 — Document the `GameSnapshotBuilder` equivalence contract + mutation-sentinel inheritance** (AC: lines 3–4)
  - [x] Appended "Story 1.4 — GameSnapshotBuilder equivalence contract" section to `_bmad-output/refactor-architecture-poco.md` (equivalence, `hackedByOmniscienceTarget`-from-live-POmniscience rule, synchrony, Epic 2 inheritance of the field-sensitivity guarantee)
- [x] **Task 6 — Prove behavior preservation + purity** (AC: line 1, line 5)
  - [x] `DomainPurity` (EditMode) → 2/2 green (no engine `using` crept in)
  - [x] `HarnessFidelity`+`GoldenMaster`+`Determinism` (PlayMode) → 31/31 green (FactionType move behavior-preserving)
  - [x] `read_console` → zero errors

## Dev Notes

### What this story touches (and what it must NOT)

**Created:**
- `Assets/Scripts/Domain/CharacterSnapshot.cs`
- `Assets/Scripts/Domain/GameSnapshot.cs`
- `Assets/Scripts/Tests/Editor/SnapshotValueObjectTests.cs`
- Contract doc (append to `refactor-architecture-poco.md` or new `_bmad-output/contracts/gamesnapshotbuilder-equivalence.md`)

**Moved (GUID-preserving, namespace unchanged):**
- `Assets/Scripts/Characters/FactionType.cs` → `Assets/Scripts/Domain/FactionType.cs`

**Must NOT change:** the 4 `W*` evaluators, `Character`, `Role`, `Power`, `WinningCondition`, any gameplay behavior, any asmdef other than (none needed — `Domain` already exists and is autoReferenced; no reference edits required).

### Field-read provenance (why these 6 fields, verified against live code)

| Field | Live source | Read by |
|---|---|---|
| `ownerClientId` | `WinningCondition.ownerClientId` | WMarginal, WOmniscience (owner lookup) |
| `isFake` | `ownerClientId.IsFakeClientId()` (set at source in Epic 2) | all 4 (`!isFake` filter) |
| `isCorrupted` | `Character.isCorrupted` (`NetworkVariable<bool>`) | WAnomalyCorruption |
| `isChained` | `Character.isChained` (`NetworkVariable<bool>`) | WMarginal, WChosen, WOmniscience(target) |
| `factionType` | `Character.role.factionType` | WChosen, WOmniscience(target) |
| `hackedByOmniscienceTarget` | `POmniscience.hackedCharacterClientId` — **plain ulong**, NOT a NetworkVariable | WOmniscience |

The `hackedByOmniscienceTarget` field is the load-bearing trap: it is a plain `ulong` on a `NetworkBehaviour`, never serialized in `Role.NetworkSerialize`. The builder (Epic 2) must read it from the live `POmniscience` instance. This story only stores it; the builder contract (Task 5) records the rule.

### Why "keep the namespace" again

Same reasoning as Story 1.3: `FactionType`'s namespace is not part of any wire format (the enum's int value is what `Role.NetworkSerialize` writes). Renaming to `CorruptionDuPortail.Domain` would force `using` churn across 28 files for zero behavioral gain and re-bless the golden suite. Minimal diff = minimal regression surface. A cosmetic namespace unification can happen later in Epic 2 if desired.

### Testing standards summary

- Snapshot tests are **EditMode** pure-POCO (no NGO host) — fast, deterministic, no `GameManager`.
- New category `[Category("DomainSnapshot")]` joins `DomainPurity` / `HarnessFidelity` / `GoldenMaster` / `Determinism` / `VacuousTruth` as an independently filterable gate.
- Behavior-preservation proof = the existing PlayMode golden suite staying green after the `FactionType` move (this story adds no PlayMode tests).
- Poll `read_console` after every script/asmdef change before assuming success (domain reload).

### Project Structure Notes

- New POCOs live in `Assets/Scripts/Domain/` alongside `WinningTeam`, `IWinningConditionEvaluator` (from 1.3). Pure types only — the assembly stays `noEngineReferences: true`.
- `record` types compile under Unity 6000.2's C# (Roslyn) — acceptable per project-context.md (no rule against records). Prefer explicit `IEquatable<T>` + overrides if you want the equality logic to be unmissable in review; either is fine.

### Project Context Rules

- **asmdef discipline:** `Domain` depends on nothing; `Game` → `Domain`; tests resolve `Domain` via its `autoReferenced: true`. No reference edits needed this story. [project-context.md §asmdef]
- **NFR2 (epics.md:37):** Domain purity compiler-enforced — the new value objects must not introduce `UnityEngine`/`Unity.Netcode`/FMOD/DOTween. Guarded by `DomainPurity` (Story 1.3).
- **Default visibility:** `public` is correct for `CharacterSnapshot`/`GameSnapshot`/`FactionType` (consumed across the `Game`/test asmdef boundary). [project-context.md:289]
- **Never edit `.csproj`/`.sln`** — they regenerate from asmdefs.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4] — ACs verbatim (lines 156–170)
- [Source: _bmad-output/refactor-architecture-poco.md] (lines 67–82) — exact snapshot field set + the `hackedCharacterClientId` trap
- [Source: _bmad-output/refactor-architecture-poco.md] (§3c steps 2–4, lines 117–119) — losslessness/differential rationale this story de-risks
- [Source: Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:18,24,35] — `hackedCharacterClientId`, target `isChained`/`factionType` reads
- [Source: Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:21,23] — `isFake`/`isCorrupted` reads
- [Source: Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:21,25,30] — `isFake`/`factionType`/`isChained` reads
- [Source: Assets/Scripts/Characters/FactionType.cs] — enum being relocated
- [Source: Story 1.3 Dev Agent Record] — `Domain.autoReferenced: true` resolves relocated types across Game + both test asmdefs (31/31 green) — the precedent this story relies on

### Previous story intelligence (Story 1.3)

- 1.3 created `CorruptionDuPortail.Domain` (`noEngineReferences: true`, `references: []`), relocated `WinningTeam` GUID-preserving with namespace kept, added the `DomainPurity` guard. **This story repeats the exact relocation pattern for `FactionType`** — low risk, precedent established.
- 1.3 proved `Domain.autoReferenced: true` makes relocated types resolve everywhere with no per-asmdef reference edit (31/31 PlayMode green). Trust that; do not add `Domain` to any test asmdef.
- 1.3's `manage_asset move` MCP call returned an error string but actually performed the move (file + meta relocated). If the same happens, verify on disk (`ls` the Domain folder + git status) before assuming failure — or just use `git mv` for `.cs` + `.cs.meta` directly.
- Categories are additive and independently runnable; introduce `DomainSnapshot` the same way 1.3 introduced `DomainPurity`.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- **Test-asmdef reference gap (CS0012):** after relocating `FactionType` into `Domain`, the first compile failed with `error CS0012: The type 'FactionType' is defined in an assembly that is not referenced. You must add a reference to assembly 'CorruptionDuPortail.Domain'` across `Tests.PlayMode` (`WinningConditionHarnessFidelityTests`, `AwakeningStateIsolationTests`, `EntrapmentPowerTests`, `WinningConditionGoldenMasterTests`) and the new `Tests.Editor` test. Root cause: `Domain.autoReferenced:true` does not propagate to explicit-reference test asmdefs. Fix: added `CorruptionDuPortail.Domain` to both test asmdefs' `references`. Recompile → clean.

### Completion Notes List

- Behavior-preserving save for the relocated `FactionType` enum (logic-free, GUID-preserving move, namespace kept `Characters` → 28 production consumers unchanged). The genuinely new code is two pure value objects + their invariant tests; nothing consumes them yet (Epic 2 wires them).
- **Corrected a Story 1.3 over-generalization:** 1.3 concluded "Domain.autoReferenced makes relocated types resolve everywhere with no test-asmdef edit". True only because no test referenced `WinningTeam`. Test files DO reference `FactionType`, so this story had to add `CorruptionDuPortail.Domain` to `Tests.PlayMode.asmdef` + `Tests.Editor.asmdef`. Future Domain relocations of test-referenced types need the same.
- `CharacterSnapshot`: `sealed`, immutable, all 6 fields in `Equals`/`GetHashCode`/`==`. `GameSnapshot`: `sealed`, defensive-copied `AsReadOnly()` character list (non-castable to writable), order-sensitive sequence equality.
- **Anti-tautology foundation:** `Test B` (`[TestCaseSource]`, one case per field incl. `hackedByOmniscienceTarget`) proves equality is field-sensitive — the guarantee Epic 2's differential inherits so it can't be fooled by a lying one-field mapping. Recorded in the contract doc.
- `GameSnapshotBuilder` equivalence contract documented in `refactor-architecture-poco.md` (spec for Story 2.1), including the live-`POmniscience` read rule and the synchrony requirement.
- **Verification:** 14/14 `DomainSnapshot` + 2/2 `DomainPurity` (EditMode) green; 31/31 PlayMode goldens green (unchanged before/after). Console clean.

### File List

- **Added:** `Assets/Scripts/Domain/CharacterSnapshot.cs`
- **Added:** `Assets/Scripts/Domain/GameSnapshot.cs`
- **Added:** `Assets/Scripts/Tests/Editor/SnapshotValueObjectTests.cs`
- **Moved:** `Assets/Scripts/Characters/FactionType.cs` → `Assets/Scripts/Domain/FactionType.cs` (GUID-preserving, namespace unchanged)
- **Modified:** `Assets/Scripts/Tests/PlayMode/Tests.PlayMode.asmdef` (added `CorruptionDuPortail.Domain` ref)
- **Modified:** `Assets/Scripts/Tests/Editor/Tests.Editor.asmdef` (added `CorruptionDuPortail.Domain` ref)
- **Modified (untracked planning):** `_bmad-output/refactor-architecture-poco.md` (builder contract), `_bmad-output/implementation-artifacts/1-4-*.md`, `sprint-status.yaml`

### Change Log

- 2026-06-10 — Born `CharacterSnapshot`/`GameSnapshot` value objects in pure Domain with structural-invariant tests (value equality, field-sensitivity sentinel foundation, immutability, round-trip); relocated `FactionType` into Domain; wired test asmdefs to Domain; documented the `GameSnapshotBuilder` equivalence contract. Story 1.4 → review.
