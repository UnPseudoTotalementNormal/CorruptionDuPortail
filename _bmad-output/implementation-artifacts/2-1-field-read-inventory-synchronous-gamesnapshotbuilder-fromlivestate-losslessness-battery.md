# Story 2.1: Field-read inventory + synchronous `GameSnapshotBuilder.FromLiveState` + losslessness battery

Status: done

## Story

As a developer (Poyo),
I want a server-side builder that captures a complete, synchronous snapshot of live state before any await,
so that every condition can be evaluated off an immutable snapshot whose fidelity to live state is proven, with no NetworkVariable tearing across a frame.

## Acceptance Criteria

**Given** the `GameSnapshotBuilder` contract fixed in Story 1.4 and the snapshot value objects from 1.4
**When** the builder is implemented
**Then** an **exhaustive field-read inventory** of all 4 winning conditions is produced first; the snapshot captures the **union** of those reads plus derived state (chaining, votes, anomaly) — the "≥7 fields" figure is a floor, not the target
**And** `FromLiveState` is **synchronous** and provably runs before any `await`: enforced structurally (a reflection/analyzer test asserting it is not `async` and does not return `UniTask`) **or** by a timing sentinel
**And** `hackedCharacterClientId` is read from the **live `POmniscience`** instance, not the serialized `Role`
**And** a losslessness battery proves the builder is lossless per inventoried field; identity reads (`GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100`) are preserved verbatim so simulated bots map identically to the runtime
**And** the builder is green standalone (snapshot-vs-live differential over Epic 1 goldens) before any condition is migrated

### Acceptance reading notes (binding)

1. **This is the first behavior-touching Epic 2 story, but it is still ADDITIVE — zero production behavior change.** The builder is a new class; the live victory loop (`VictoryConditionCheckState.OnStartStateServer`) keeps calling the old `CheckCondition()` pull. Nothing consumes the snapshot in prod yet (the dual signature is Story 2.2; migrations are 2.3–2.6). Do NOT modify `VictoryConditionCheckState`, the 4 evaluators, or `WinningCondition` here. The Epic 1 golden suite (42 PlayMode) must stay green.

2. **Field-read inventory (the deliverable that gates the field set) — verified against the live evaluators:**
   | Field (on `CharacterSnapshot`) | Live read | Conditions |
   |---|---|---|
   | `OwnerClientId` | `Character.ownerClientId.Value` | loop key; WMarginal/WOmniscience owner lookup |
   | `IsFake` | `Character.isFake` | all (`!isFake` filter / loop skip) |
   | `IsCorrupted` | `Character.isCorrupted.Value` | WAnomalyCorruption |
   | `IsChained` | `Character.isChained.Value` | WMarginal, WChosen, WOmniscience(target) |
   | `FactionType` | `Character.role.factionType` | WChosen, WOmniscience(target) |
   | `HackedByOmniscienceTarget` | `POmniscience.hackedCharacterClientId` in `role.powers` (plain ulong, live instance) | WOmniscience |
   `GameSnapshot.Day` / `CurrentStateIndex` are NOT read by any winning condition (they are GameLoop state, consumed in Story 2.11). Populate `CurrentStateIndex` from `gameManager.currentGameStateIndex.Value`; set `Day` from the live day source if one exists, else `0` with a code comment that it is a non-condition-read placeholder pending the GameLoop extraction. Losslessness is required for the 6 condition-read fields; `Day`/`CurrentStateIndex` get a best-effort capture, not a losslessness gate.

3. **The builder lives in `Game`, NOT `Domain`.** It reads live NGO state (`GameManager`, `Character` NetworkVariables, `POmniscience`) — it is the Humble-Object mapping/adapter. It *produces* a `CorruptionDuPortail.Domain.GameSnapshot`. Place it at `Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs`, namespace `GameLogic` (or `GameLogic.Snapshot`). Signature: `public static GameSnapshot FromLiveState(GameManager gameManager)` — **static, synchronous, returns `GameSnapshot`** (never `UniTask`/`async`).

4. **Fake-character safety (preserve current behavior).** Iterate `gameManager.characterManager.GetCharacters(false)` (the exact source the loop uses). Capture EVERY character with its `IsFake` flag set — do NOT pre-filter (the conditions filter on the snapshot later). BUT a fake character may have a `null` `role` → `c.role.factionType` / `c.role.powers` would NRE. Guard: for a character whose `role` is `null` (or `IsFake`), use `FactionType.unknown` and `HackedByOmniscienceTarget = POmniscience.HACKED_CHARACTER_DEFAULT` rather than dereferencing `role`. This matches the live behavior (fakes are skipped before any role read). Document the guard.

5. **`hackedCharacterClientId` trap (AC line 207).** Read it from the **live `POmniscience` instance** found in `character.role.powers` (`role.powers.Find(p => p.GetType() == typeof(POmniscience))`), exactly as `WOmniscienceHackedCharacter.cs:18` does — never from a serialized `Role`. If no `POmniscience` is present, default to `POmniscience.HACKED_CHARACTER_DEFAULT`. A **dedicated losslessness test** must set `hackedCharacterClientId` on a live `POmniscience` and assert the snapshot carries that exact value (and the default case when absent).

6. **Synchrony proof (AC line 206).** Add a reflection test asserting `FromLiveState`'s `MethodInfo` (a) is not decorated `AsyncStateMachineAttribute`, and (b) its `ReturnType` is `GameSnapshot` (not `UniTask`/`UniTask<>`/`Task`). This structurally forbids a future refactor from making the capture asynchronous (which would reintroduce NetworkVariable tearing across a frame).

7. **Losslessness battery + standalone differential (AC lines 208–209).** PlayMode, on the `VictoryConditionTests` host harness (spawn characters, assign roles/powers, set NetworkVariables). Per inventoried field: set a known live value, build, assert `snapshot.character.Field == liveValue`. The "snapshot-vs-live differential over Epic 1 goldens" = reconstruct representative golden scenarios (corrupted/chained/faction/hacked permutations) and assert every captured field equals its live source across the corpus — proving the mapping is lossless before any condition trusts it. Identity reads: a character with `clientId >= 100` (simulated bot) must map to a `CharacterSnapshot` with that same `OwnerClientId` and the same `IsFake` verdict as the runtime (`IsFakeClientId`), verbatim.

8. **New category** `[Category("SnapshotBuilder")]` (PlayMode), independently runnable, joins the Epic 1 set.

## Tasks / Subtasks

- [x] **T0 — Baseline green** → 42/42 PlayMode + 16/16 EditMode before change
- [x] **T1 — Field-read inventory** encoded in the `GameSnapshotBuilder.cs` header + note 2; confirmed against the 4 evaluators
- [x] **T2 — Implement `GameSnapshotBuilder.FromLiveState`** — static, synchronous, fake/null-role guard, live-`POmniscience` read; clean compile
- [x] **T3 — Synchrony reflection test** — asserts not-async + return type `GameSnapshot`
- [x] **T4 — Losslessness battery** — per-field + dedicated hack-target (set + default) + simulated-bot identity (`clientId >= 100`)
- [x] **T5 — Standalone golden-scenario differential** — corrupted/chained/hacked permutation, all 6 fields vs live
- [x] **T6 — Prove** — `SnapshotBuilder` 9/9; full regression 42/42 PlayMode + 16/16 EditMode; console clean

## Dev Notes

### What this story touches (and must NOT)

**Created:** `Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs`; `Assets/Scripts/Tests/PlayMode/GameSnapshotBuilderLosslessnessTests.cs` (+ the synchrony reflection test, may live in the same file or an EditMode file).
**Must NOT change:** `VictoryConditionCheckState.cs`, the 4 `W*` evaluators, `WinningCondition.cs`, `Character.cs`, `POmniscience.cs`, any asmdef. Pure addition.

### Key live sources

- Loop + character source: `VictoryConditionCheckState.cs:26` → `gameManager.characterManager.GetCharacters(false)`.
- `POmniscience.hackedCharacterClientId` (`POmniscience.cs:14`), default `HACKED_CHARACTER_DEFAULT` (`:15`).
- Power lookup pattern: `WOmniscienceHackedCharacter.cs:18` — `role.powers.Find(p => p.GetType() == typeof(POmniscience))`.
- `CurrentStateIndex` ← `gameManager.currentGameStateIndex.Value`.

### Testing standards summary

- PlayMode losslessness on the `VictoryConditionTests` host harness (reuse verbatim). Builder is in `Game`; `Tests.PlayMode` already references `Game` + `CorruptionDuPortail.Domain`.
- The builder must be **side-effect free** (pure read) — it must not call any RPC, mutate any NetworkVariable, or touch `ChainingManager`. A reading-only mapping.
- `[Category("SnapshotBuilder")]`, independently runnable.

### Project Context Rules

- Server-authority: `FromLiveState` runs server-side (the loop is server-only); it reads, never writes. [project-context.md critical patterns]
- `IsLocalOrSimulated` / `clientId >= 100` bot identity preserved verbatim in the mapping (AC line 208) — do not normalize bot ids.
- Async = UniTask elsewhere, but this method is **deliberately synchronous** (the whole point) — the synchrony test enforces it.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.1] (lines 195–209)
- [Source: _bmad-output/refactor-architecture-poco.md] (lines 67–82 snapshot shape + the trap; lines 97–100 §3a builder steps)
- [Source: _bmad-output/refactor-architecture-poco.md#Story 1.4 — GameSnapshotBuilder equivalence contract] — the contract this builder implements
- [Source: Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:26–45] — the loop + character source
- [Source: Assets/Scripts/Characters/Powers/POmniscience.cs:14–15,41] — hack target field
- [Source: Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs] — host harness to reuse

### Previous story intelligence

- Story 1.4 fixed the snapshot field set + the equivalence contract; this builder is its first implementation. `CharacterSnapshot`/`GameSnapshot` are immutable with value equality and a read-only character list.
- Story 1.5 PR B already drove a real server-side path on the host harness (vote tally) and registered SOs — the same harness mechanics (spawn characters, set NetworkVariables, assign `role`/`powers`) apply here.
- `NetworkList`/NGO collections lack LINQ `ToList` — iterate with `foreach` when reading (Story 1.5 PR A).
- Relocating types into Domain needs explicit test-asmdef refs ([[reference-domain-asmdef-autoref-tests]]) — N/A here (builder stays in `Game`, no new Domain types).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- `POmniscience : Power : NetworkBehaviour` cannot be `new`-ed — spawn via GameObject + NetworkObject + `Spawn()` (reused the `SpawnOmniscience` pattern from `WinningConditionGoldenMasterTests`), then `role.powers.Add(omni)`, set `omni.hackedCharacterClientId`.

### Completion Notes List

- Additive, behavior-preserving: `GameSnapshotBuilder` is new; the live victory loop still calls the old `CheckCondition()` pull. Nothing consumes the snapshot in prod yet (dual signature = Story 2.2). Epic 1 goldens unchanged (42/42).
- Builder in `Game` (Humble-Object mapping; reads live NGO, produces Domain `GameSnapshot`), `static`, **synchronous** — enforced by a reflection test (return type `GameSnapshot`, not async). Side-effect free (read-only; no RPC, no mutation).
- `hackedCharacterClientId` read from the **live `POmniscience`** in `role.powers` (the trap), default `HACKED_CHARACTER_DEFAULT` when absent — both paths covered by losslessness tests.
- Fake/null-role guard: characters with null role (fakes) map to `FactionType.unknown` + default hack target without dereferencing `role` (matches live skip-fakes behavior).
- Simulated-bot identity (`clientId >= 100`) mapped verbatim; snapshot `IsFake` equals the runtime `IsFakeClientId` verdict.
- `Day` is a non-condition-read placeholder (0) pending the GameLoop extraction (Story 2.11); `CurrentStateIndex` captured from the live index.
- **Verification:** `SnapshotBuilder` 9/9; regression 42/42 PlayMode + 16/16 EditMode; console clean.

### File List

- **Added:** `Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs`
- **Added:** `Assets/Scripts/Tests/PlayMode/GameSnapshotBuilderLosslessnessTests.cs`

### Change Log

- 2026-06-10 — Implemented the synchronous, server-side `GameSnapshotBuilder.FromLiveState` with the field-read inventory, fake-safe + live-`POmniscience` mapping, synchrony reflection guard, and the losslessness/identity/golden-differential battery. Additive, no production behavior change. Story 2.1 → review.
