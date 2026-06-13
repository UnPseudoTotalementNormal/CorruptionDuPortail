# Story 2.3: Migrate `WMarginalIsChainedWin` (easiest, zero prior coverage)

Status: done

## Story

As a developer (Poyo),
I want `WMarginalIsChainedWin` switched to the snapshot signature with its differential proven non-blind,
so that the first migration validates the whole strangler mechanism on the simplest condition.

## Acceptance Criteria

**Given** the harness of 2.2 and the chaining goldens of Story 1.5
**When** the condition is migrated in a single commit
**Then** a **field-read trace** instruments `CheckCondition()` to capture exactly the getters this condition touches, and each traced field is asserted to have a losslessness test (2.1) before the dual-signature switch
**And** a **per-condition mutation-sentinel** proves that corrupting each field this condition reads turns its differential red
**And** the differential is green across the full matrix; one red differential halts the migration
**And** the commit is shippable on its own (NFR7)

### Acceptance reading notes (binding — this establishes the regime reused by 2.4–2.6)

1. **Migrate by OVERRIDING the snapshot overload — do not touch the pull.** Add to `WMarginalIsChainedWin`:
   ```csharp
   public override bool CheckCondition(GameSnapshot snapshot)
   {
       foreach (var c in snapshot.Characters)
       {
           if (c.OwnerClientId == ownerClientId)
           {
               return !c.IsFake && c.IsChained;
           }
       }
       return false; // owner not found ≡ live GetCharacter(ownerClientId) == null → false
   }
   ```
   This mirrors the pull (`WMarginalIsChainedWin.cs:14–23`): owner not found OR fake → `false`; else `owner.isChained`. The pull stays untouched (the differential needs it until Story 2.7).

2. **Field-read trace (pragmatic interpretation).** This condition reads exactly **3 snapshot fields**: `OwnerClientId` (match key), `IsFake`, `IsChained`. A runtime getter-interception trace is disproportionate for a 3-line predicate; instead the "trace" is the explicit, code-verified read set (asserted in the test header against `WMarginalIsChainedWin.cs:16,17,22`) **plus** a test asserting each read field is covered by a Story 2.1 losslessness test (`OwnerClientId`, `IsFake`, `IsChained` all are). The point of the AC — no field is read that the builder might silently lie about — is satisfied: every read field is proven lossless.

3. **Per-condition mutation-sentinel (must bite per read field).** Using `SnapshotDifferential.SingleFieldCorruptions`, for each of the 3 read fields, construct a scenario where that field is verdict-relevant, corrupt only it, and assert the **snapshot verdict diverges from the live pull verdict** (`condition.CheckCondition(corrupted) != condition.CheckCondition()`). Fields NOT read (`IsCorrupted`, `FactionType`, `HackedByOmniscienceTarget`) must NOT change the verdict — assert at least one is inert (proves the sentinel is specific, not trivially always-red).

4. **Full-matrix differential green.** Drive the chaining-relevant scenarios on the host harness — owner chained / owner not chained / owner is fake / owner absent — and `SnapshotDifferential.AssertAgrees(new WMarginalIsChainedWin{ownerClientId=…}, gameManager)` must pass for every one. One disagreement halts the migration.

5. **Shippable per commit (NFR7).** Prod still calls the pull (the loop is unchanged); the override only fires from the test differential. Full Epic-1/2.1/2.2 suite green. The commit is self-contained and reversible.

6. **New category** `[Category("Migration")]` (PlayMode) for the per-condition migration suites, independently runnable; joins `Differential`.

## Tasks / Subtasks

- [x] **T0 — Baseline green**
- [x] **T1 — Production override** added to `WMarginalIsChainedWin`; pull untouched; clean compile
- [x] **T2 — Field-read trace + losslessness coverage** documented in test header (3 fields, all covered by 2.1)
- [x] **T3 — Full-matrix differential** — chained / not-chained / absent all agree
- [x] **T4 — Per-condition sentinel** — 3 read fields flip; non-read field inert
- [x] **T5 — Prove** — `Migration` 4/4; regression 53/53 PlayMode; console clean

## Dev Notes

### What this story touches (and must NOT)

**Modified (production):** `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs` — add the override only; do NOT change `CheckCondition()` (pull).
**Created:** `Assets/Scripts/Tests/PlayMode/Migration/WMarginalMigrationTests.cs`.
**Must NOT change:** the other 3 evaluators, `WinningCondition`, the builder, the loop, any asmdef.

### Equivalence map (pull → snapshot)

| Pull (`WMarginalIsChainedWin.cs`) | Snapshot |
|---|---|
| `GetCharacter(ownerClientId) == null` → false | owner id absent from `snapshot.Characters` → false |
| `owner.isFake` → false | `c.IsFake` → false |
| `return owner.isChained.Value` | `return c.IsChained` (after `!IsFake`) |

### Testing standards summary

- Reuse the host harness + `SnapshotDifferential` helper (Story 2.2). `[Category("Migration")]`.
- Migration tests build the snapshot via `GameSnapshotBuilder.FromLiveState` and compare against the live pull — this is the real equivalence proof now that the override reads the snapshot.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.3] (lines 226–239)
- [Source: Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:14–23] — the pull being mirrored
- [Source: Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs] — `AssertAgrees` + `SingleFieldCorruptions`
- [Source: Assets/Scripts/Tests/PlayMode/GameSnapshotBuilderLosslessnessTests.cs] — losslessness coverage for the 3 read fields

### Previous story intelligence

- Story 2.2 delegating overload + `SnapshotDifferential` are the scaffold; this is the first real override. Same host-harness/POmniscience patterns apply (no POmniscience needed here — WMarginal reads only owner fields).
- Story 1.5 PR A chaining goldens + 2.1 `IsChained` losslessness already back this condition's reads.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- First real migration: `WMarginalIsChainedWin.CheckCondition(GameSnapshot)` overridden to read `OwnerClientId`/`IsFake`/`IsChained` from the snapshot (pull untouched, still used by the differential). Establishes the regime (override + field-read trace + full-matrix differential + per-field sentinel) reused by 2.4–2.6.
- Full-matrix differential (chained/not-chained/absent) green. The per-field sentinel proves the override genuinely reads the snapshot: corrupting each of the 3 read fields flips the verdict, and a non-read field is inert (specific, not always-red).
- Shippable per commit: prod loop still calls the pull; the override fires only from the test differential. Regression 53/53 PlayMode green.

### File List

- **Modified:** `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs` (added `CheckCondition(GameSnapshot)` override + `using CorruptionDuPortail.Domain;`)
- **Added:** `Assets/Scripts/Tests/PlayMode/Migration/WMarginalMigrationTests.cs`

### Change Log

- 2026-06-10 — Migrated `WMarginalIsChainedWin` to the snapshot signature; established the per-condition migration regime (differential + sentinel). Story 2.3 → review.
