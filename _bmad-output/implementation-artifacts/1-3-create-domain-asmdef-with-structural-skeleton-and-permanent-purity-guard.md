# Story 1.3: Create `CorruptionDuPortail.Domain.asmdef` with the structural skeleton + a permanent purity guard

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want a pure `Domain` assembly the compiler structurally forbids from referencing Unity/Netcode/FMOD/DOTween, guarded permanently by an automated check,
so that the core cannot silently re-acquire an engine `using` now or in any future PR.

## Acceptance Criteria

**Given** the single `Game.asmdef` today
**When** the Domain assembly is created
**Then** `CorruptionDuPortail.Domain.asmdef` exists with `noEngineReferences: true` and `references: []`
**And** it contains only the structural skeleton: `WinningTeam` (enum) and `IWinningConditionEvaluator` (contract whose signature is already satisfied by the 4 existing evaluators) — the `CharacterSnapshot` / `GameSnapshot` value objects are introduced in Story 1.4
**And** `Game.asmdef` references `Domain`, never the reverse
**And** the `noEngineReferences` guard is proven to actually fail the build when a `using UnityEngine` is added to a Domain file (the guard is tested once, not assumed)
**And** a **CI guard** fails any PR that adds an engine reference to `Domain.asmdef` or removes `noEngineReferences: true` — Domain purity is a permanent invariant, not an initial state

### Acceptance reading notes (binding — resolves ambiguity before you start)

1. **This is the first production-structural story of the refactor. It is behavior-preserving by construction.** No gameplay logic changes. You are (a) creating a new pure assembly, (b) relocating one zero-dependency enum into it, (c) adding one new interface, (d) pointing `Game` at `Domain`, (e) proving + locking the purity guard. The golden/harness suites from Stories 1.0–1.2 are the safety net: **all PlayMode goldens (`HarnessFidelity`, `GoldenMaster`, `Determinism`) MUST stay green** after this change — that is the behavior-preservation proof.

2. **`WinningTeam` relocates into the Domain assembly but KEEPS its current namespace `Characters.WinningConditions` (recommended — minimal-diff path).** Asmdef membership and C# namespace are independent. Move the *file* `WinningTeam.cs` into the Domain asmdef folder; do **not** change `namespace Characters.WinningConditions`. Consequence: the 8 existing consumers (`WinningCondition.cs`, the 4 `W*` evaluators, `VictoryConditionCheckState.cs`, `TakeDownThePortalState.cs`, `GameEndingState.cs`) need **zero** edits — they already `using`/share that namespace, and `Game` resolves the type through its new reference to `Domain`. This avoids touching 8 files and avoids any risk to `INetworkSerializable` round-trips.
   - **Alternative (NOT this story):** renaming the namespace to `CorruptionDuPortail.Domain` is cleaner long-term but forces a `using` edit across 8 files for no behavioral gain now. Defer it to Epic 2 (Story 2.7 already cuts/promotes the winning-condition contracts and is the natural place). If you rename here anyway, you own re-running the full golden suite to prove serialization parity — not recommended.

3. **`IWinningConditionEvaluator` is BORN here but NOT wired yet.** Create the interface in namespace `CorruptionDuPortail.Domain`:
   ```csharp
   namespace CorruptionDuPortail.Domain
   {
       public interface IWinningConditionEvaluator
       {
           WinningTeam GetWinningTeam();
           bool CheckCondition();
       }
   }
   ```
   Its signature **already matches** what the 4 evaluators expose (`WinningCondition.cs:15,17`). Do **NOT** add `: IWinningConditionEvaluator` to `WinningCondition` in this story — wiring the abstract base to the contract (and cutting the old signature) is **Story 2.7's** job (`# REVIEW-REQUIRED`). Here the interface exists, compiles in pure Domain, and is structurally satisfiable — nothing more. This keeps 1.3 a zero-risk structural story.
   - Note: `WinningTeam` must be visible from the interface. Since the interface is in `CorruptionDuPortail.Domain` and the enum is in `Characters.WinningConditions` (both in the Domain assembly), add `using Characters.WinningConditions;` to `IWinningConditionEvaluator.cs`. Both types live in the same assembly, so this is a pure intra-assembly `using` — no engine reference.

4. **`noEngineReferences: true` AND `references: []` are both required and both load-bearing.** `references: []` means Domain references no other assembly. `noEngineReferences: true` additionally strips the implicit `UnityEngine`/`UnityEditor` references every asmdef gets by default — without it, a Domain file could still `using UnityEngine;` and compile. Both must be set or the purity guarantee is hollow.

5. **The purity guard has TWO parts — do not conflate them:**
   - **One-time build-fail proof (AC line 4):** manually add `using UnityEngine;` to a temporary Domain file (or the enum file), trigger compile, confirm the Editor reports a compile error (`The type or namespace name 'UnityEngine' could not be found` / `CS0246`), capture the exact error text in the Dev Agent Record, then revert. This proves the compiler boundary is real, not assumed. Do this via `mcp__UnityMCP__read_console` after the edit.
   - **Permanent automated CI guard (AC line 5):** an **EditMode** test (`[Category("DomainPurity")]`) that parses `CorruptionDuPortail.Domain.asmdef` as JSON and asserts `noEngineReferences == true` AND `references` is empty. This runs in `run_tests` and in CI (`Build.yml`), so any future PR that flips `noEngineReferences` to `false` or adds a reference fails the suite. This is the portable "CI guard" — a JSON-asserting EditMode test is the Unity-idiomatic form of the grep-guard the architecture demands, but stronger (it reads the actual compiled manifest, not a text pattern).

6. **Do NOT touch `Game.Editor.asmdef`, the test asmdefs, `Rendering`, or any `Plugins/` asmdef.** Only two asmdef files change: the new `CorruptionDuPortail.Domain.asmdef` (created) and `Game.asmdef` (one line added to `references`). Touching others is off-scope.

7. **Domain folder location:** create `Assets/Scripts/Domain/` and place `CorruptionDuPortail.Domain.asmdef`, the relocated `WinningTeam.cs`, and the new `IWinningConditionEvaluator.cs` there. This mirrors the existing `Assets/Scripts/<System>/` + sub-asmdef convention (project-context.md §"One concern per folder … its own sub-asmdef").

## Tasks / Subtasks

- [x] **Task 0 — Confirm the safety net is green before any structural change** (AC: line 1 "Given")
  - [x] `mcp__UnityMCP__run_tests` `category: HarnessFidelity` (PlayMode) → green
  - [x] `mcp__UnityMCP__run_tests` `category: GoldenMaster` (PlayMode) → green
  - [x] `mcp__UnityMCP__run_tests` `category: Determinism` (PlayMode) → green
  - [x] Baseline: 31/31 PlayMode green before any change.
- [x] **Task 1 — Create the Domain assembly** (AC: lines 2–3)
  - [x] Create folder `Assets/Scripts/Domain/`
  - [x] Create `Assets/Scripts/Domain/CorruptionDuPortail.Domain.asmdef` with `references: []`, `noEngineReferences: true`, `autoReferenced: true`, minimal shape.
- [x] **Task 2 — Relocate `WinningTeam` into Domain (namespace unchanged)** (AC: line 3)
  - [x] Moved `WinningTeam.cs` + `.meta` (GUID preserved) → `Assets/Scripts/Domain/WinningTeam.cs`
  - [x] Kept `namespace Characters.WinningConditions` verbatim
  - [x] `read_console` → zero compile errors (8 consumers resolve `WinningTeam` via Game→Domain reference)
- [x] **Task 3 — Point `Game` at `Domain`** (AC: line 4)
  - [x] Added `"CorruptionDuPortail.Domain"` to `Game.asmdef` `references`
  - [x] `read_console` → zero errors
- [x] **Task 4 — Birth `IWinningConditionEvaluator` (unwired)** (AC: line 3)
  - [x] Created `Assets/Scripts/Domain/IWinningConditionEvaluator.cs` (namespace `CorruptionDuPortail.Domain`, `using Characters.WinningConditions;`)
  - [x] Did NOT add `: IWinningConditionEvaluator` to `WinningCondition` (deferred to Story 2.7)
  - [x] `read_console` → zero errors
- [x] **Task 5 — Prove the build-fail guard once** (AC: line 5, part 1)
  - [x] Added `using UnityEngine;` to `WinningTeam.cs` → captured `CS0246` (see Debug Log)
  - [x] Reverted; `read_console` → green again
- [x] **Task 6 — Lock the guard permanently (automated CI guard)** (AC: line 5, part 2)
  - [x] Added EditMode `DomainPurityGuardTests` `[Category("DomainPurity")]` reading the asmdef via `AssetDatabase.FindAssets(... t:AssemblyDefinitionAsset)` + `JsonUtility`, asserting `noEngineReferences == true` and `references` empty
  - [x] `run_tests category: DomainPurity` (EditMode) → 2/2 green
- [x] **Task 7 — Prove behavior preservation** (AC: line 1)
  - [x] Re-ran `HarnessFidelity` + `GoldenMaster` + `Determinism` (PlayMode) → 31/31 green
  - [x] Re-ran `DomainPurity` (EditMode) → 2/2 green
  - [x] `read_console` → zero errors

## Dev Notes

### What this story touches (and what it must NOT)

**Created:**
- `Assets/Scripts/Domain/CorruptionDuPortail.Domain.asmdef`
- `Assets/Scripts/Domain/IWinningConditionEvaluator.cs`
- EditMode test `DomainPurityGuardTests.cs` (in `Assets/Scripts/Tests/Editor/`, `Tests.Editor.asmdef`)

**Moved (GUID-preserving):**
- `Assets/Scripts/Characters/WinningConditions/WinningTeam.cs` → `Assets/Scripts/Domain/WinningTeam.cs` (namespace unchanged)

**Modified (one line):**
- `Assets/Scripts/Game.asmdef` — add `"CorruptionDuPortail.Domain"` to `references`

**Must NOT change:** `WinningCondition.cs`, the 4 `W*` evaluators, any `GameStates/*.cs`, `Game.Editor.asmdef`, `Game.Rendering.asmdef`, any `Plugins/` asmdef, and any gameplay behavior.

### Current state of the files in play

- `WinningTeam.cs` — pure enum `{ chosen, anomaly, marginal, alone }`, namespace `Characters.WinningConditions`, zero engine `using`. Trivially relocatable.
- `WinningCondition.cs:11` — `abstract class WinningCondition : INetworkSerializable` with `abstract WinningTeam GetWinningTeam()` (line 15) and `abstract bool CheckCondition()` (line 17). Stays in `Game` (it is `INetworkSerializable`). Its method signatures are exactly `IWinningConditionEvaluator`'s — but wiring is Epic 2.
- `Game.asmdef` — today `noEngineReferences: false`, references 25 assemblies. We append one. Leave everything else untouched.
- 8 consumers of `WinningTeam`: `WinningCondition.cs`, `WOmniscienceHackedCharacter.cs`, `WMarginalIsChainedWin.cs`, `WChosenChainedAllAnomaly.cs`, `WAnomalyCorruption.cs`, `VictoryConditionCheckState.cs`, `TakeDownThePortalState.cs`, `GameEndingState.cs` — all in the `Game` assembly, all keep working unchanged because the namespace is preserved and `Game` now references `Domain`.

### Why "keep the namespace" is the right call

`INetworkSerializable` round-trips `WinningCondition` over the wire. The enum's namespace is not part of the wire format (only the underlying int value is serialized), so a namespace rename is *technically* wire-safe — but it is gratuitous churn across 8 files that the golden suite would then have to re-bless. The epic AC requires the **assembly boundary**, not a specific namespace string. Minimal diff = minimal regression surface. Epic 2 (Story 2.7) is the sanctioned place for the cosmetic rename.

### Testing standards summary

- New asmdef must compile clean: poll `read_console` after every asmdef/script change before assuming success (project-context.md: domain reload + `isCompiling`).
- The purity guard is an **EditMode** test (no NGO host needed — it reads a JSON asset). Lives in `Tests.Editor.asmdef` (carries `defineConstraints: ["UNITY_INCLUDE_TESTS"]`).
- The behavior-preservation proof is the existing **PlayMode** golden suite staying green — this story adds no new PlayMode tests.
- New category `[Category("DomainPurity")]` joins `HarnessFidelity` / `GoldenMaster` / `Determinism` / `VacuousTruth` as an independently filterable gate.

### Project Structure Notes

- `Assets/Scripts/Domain/` is a new sub-asmdef under the existing `Assets/Scripts/<System>/` convention (cf. `Rendering/` → `Game.Rendering.asmdef`). Aligns with project-context.md §"One concern per folder … its own sub-asmdef" and the dependency table (`Game` may depend on `Domain`; `Domain` depends on nothing).
- Edit the `.asmdef` JSON directly (project-context.md: "never edit auto-generated `.csproj`"; `.csproj`/`.sln` regenerate from asmdefs).

### Project Context Rules

- **Server authority / async / audio patterns** — not exercised by this story (pure structural move, no gameplay/NGO/FMOD code touched).
- **asmdef discipline (project-context.md §asmdef):** Tests asmdefs depend on `Game`, never inverse; `Game` may depend on `Domain`, never the reverse — this story establishes exactly that edge. Default visibility `internal`/`private`, promote to `public` only across an asmdef boundary — `WinningTeam` and `IWinningConditionEvaluator` are `public` because `Game` (a different assembly) consumes them. Correct.
- **NFR2 (epics.md:37):** "Domain asmdef purity — no `UnityEngine`/`Unity.Netcode`/FMOD/DOTween in the core; enforced by the compiler via `noEngineReferences`. `Game` references `Domain`, never the reverse." This story is the literal implementation of NFR2.
- **Never edit auto-generated `.csproj`/`.sln`** — only `.asmdef` JSON / Inspector.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.3] — ACs verbatim
- [Source: _bmad-output/planning-artifacts/epics.md#NFR2] — compiler-enforced Domain purity, `Game`→`Domain` direction
- [Source: _bmad-output/refactor-architecture-poco.md#Assembly boundary enforces purity] (§ lines 45–52) — what goes in Domain vs stays in Game; `WinningCondition` stays (it is `INetworkSerializable`)
- [Source: _bmad-output/refactor-architecture-poco.md] (Phase 0-b, line 134) — Domain asmdef created at start of extraction containing only zero-dependency types `WinningTeam`, `CharacterSnapshot`, `GameSnapshot`, `IWinningConditionEvaluator` (snapshots deferred to Story 1.4 per epic AC)
- [Source: Assets/Scripts/Characters/WinningConditions/WinningCondition.cs:11,15,17] — contract signature the interface mirrors
- [Source: Assets/Scripts/Characters/WinningConditions/WinningTeam.cs] — enum being relocated
- [Source: Assets/Scripts/Game.asmdef:38] — `noEngineReferences: false` today; the `references` array to append to
- [Source: _bmad-output/project-context.md §asmdef] (lines 107–118, 289, 306) — asmdef dependency rules, visibility, never edit `.csproj`

### Previous story intelligence (Story 1.2)

- Story 1.2 explicitly fenced this work: *"do NOT create the `Domain` asmdef, do NOT add `CharacterSnapshot`/`GameSnapshot`… Those are Stories 1.3 / 1.4."* — 1.3 is now sanctioned to do exactly the Domain-asmdef half.
- 1.2 established the filterable-category discipline (`GoldenMaster`, `VacuousTruth` joined `HarnessFidelity`, `Determinism`). Follow the same pattern: `DomainPurity` is a new independently-runnable category.
- The golden suite 1.2 authored is the regression net this story leans on — keep it green, do not modify any `WinningConditions/` production file (other than relocating the standalone `WinningTeam.cs`, which has no logic).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- **Purity guard build-fail proof (Task 5):** adding `using UnityEngine;` to `Assets/Scripts/Domain/WinningTeam.cs` produced, after compile:
  `Assets\Scripts\Domain\WinningTeam.cs(1,7): error CS0246: The type or namespace name 'UnityEngine' could not be found (are you missing a using directive or an assembly reference?)`
  The `noEngineReferences: true` boundary is therefore real and compiler-enforced, not assumed. The `using` was reverted; recompile returned a clean console.

### Completion Notes List

- Behavior-preserving structural story. No gameplay/NGO/FMOD code modified; the only relocated file (`WinningTeam.cs`) is a logic-free enum, moved with its `.meta` so the GUID — and thus every existing reference — is preserved. Namespace kept as `Characters.WinningConditions` (minimal-diff path per reading-note 2), so the 8 consumers needed zero edits.
- `Game.asmdef` now references `CorruptionDuPortail.Domain`; the reverse edge does not exist (`Domain` references `[]`). NFR2 direction satisfied.
- `IWinningConditionEvaluator` born in pure Domain, intentionally **unwired** — `WinningCondition` is NOT yet declared to implement it (that promotion + old-signature cut is Story 2.7, `# REVIEW-REQUIRED`).
- Purity is locked two ways: (1) one-time CS0246 build-fail proof above; (2) permanent `DomainPurityGuardTests` EditMode guard `[Category("DomainPurity")]` that reads the compiled asmdef manifest and fails any future PR flipping `noEngineReferences` or adding a reference.
- **Verification:** baseline 31/31 PlayMode green → after change 31/31 PlayMode green (`HarnessFidelity`+`GoldenMaster`+`Determinism`) + 2/2 EditMode `DomainPurity` green. Console clean, no new warnings.

### File List

- **Added:** `Assets/Scripts/Domain/CorruptionDuPortail.Domain.asmdef`
- **Added:** `Assets/Scripts/Domain/IWinningConditionEvaluator.cs`
- **Added:** `Assets/Scripts/Tests/Editor/DomainPurityGuardTests.cs`
- **Moved:** `Assets/Scripts/Characters/WinningConditions/WinningTeam.cs` → `Assets/Scripts/Domain/WinningTeam.cs` (GUID-preserving, namespace unchanged)
- **Modified:** `Assets/Scripts/Game.asmdef` (added `CorruptionDuPortail.Domain` to `references`)
- **Modified (tracking):** `_bmad-output/implementation-artifacts/1-3-*.md`, `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-10 — Created pure `CorruptionDuPortail.Domain` assembly (`noEngineReferences:true`, `references:[]`), relocated `WinningTeam`, added unwired `IWinningConditionEvaluator`, pointed `Game`→`Domain`, proved + permanently locked the purity guard. Story 1.3 → review.
