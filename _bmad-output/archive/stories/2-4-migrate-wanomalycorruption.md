# Story 2.4: Migrate `WAnomalyCorruption`

Status: done

## Story

As a developer (Poyo),
I want `WAnomalyCorruption` switched to the snapshot signature under the same proof regime,
so that the second-easiest condition lands shippable per commit.

## Acceptance Criteria

**Given** the regime established in 2.3 (field-read trace, per-field losslessness, per-condition sentinel, full differential)
**When** the condition is migrated in a single commit
**Then** all four guards (trace, losslessness, sentinel, green differential) pass for `WAnomalyCorruption`
**And** the commit is shippable on its own

### Acceptance reading notes

1. **Override mirrors the pull** (`WAnomalyCorruption.cs:19–29`): iterate non-fake characters; the first non-corrupted one → `false`; else `true` (vacuously true over an empty non-fake set).
   ```csharp
   public override bool CheckCondition(GameSnapshot snapshot)
   {
       foreach (var c in snapshot.Characters)
       {
           if (c.IsFake) continue;
           if (!c.IsCorrupted) return false;
       }
       return true;
   }
   ```
2. **Field-read trace:** reads exactly `IsFake` (filter) and `IsCorrupted`. Both covered by Story 2.1 losslessness. It is NOT owner-specific (no `OwnerClientId` read) — so `OwnerClientId` is a clean inert field for the sentinel.
3. **Per-field sentinel (targeted scenarios, since fields flip under different setups):**
   - `IsCorrupted` bites: one non-fake corrupted character (verdict true) → corrupt `IsCorrupted`→false → verdict false.
   - `IsFake` bites: char A corrupted + char B NOT corrupted (verdict false) → corrupt B's `IsFake`→true → B excluded → verdict true.
   - inert: corrupting `OwnerClientId` (a non-read field) leaves the verdict unchanged.
4. **Full-matrix differential:** all-corrupted (true), one-not-corrupted (false), empty non-fake population (vacuously true) → `AssertAgrees` green.
5. **Vacuous case** (empty non-fake population → true) tagged understanding from Story 1.2; assert it in the differential.
6. Shippable per commit; pull untouched; full regression green. `[Category("Migration")]`.

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — Override `CheckCondition(GameSnapshot)`** on `WAnomalyCorruption` (note 1); pull untouched
- [ ] **T2 — Migration tests** `[Category("Migration")]`: full-matrix differential (note 4) + per-field sentinel (note 3) + field-read trace header (note 2)
- [ ] **T3 — Prove**: `Migration` green; full regression green; console clean

## Dev Notes

**Modified (production):** `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs` — add the override only.
**Created:** `Assets/Scripts/Tests/PlayMode/Migration/WAnomalyMigrationTests.cs`.
**Must NOT change:** other evaluators, builder, loop, asmdefs.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.4] (lines 241–252)
- [Source: Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:19–29] — the pull being mirrored
- [Source: Story 2.3 regime] — `WMarginalMigrationTests` template; reuse host harness + `SnapshotDifferential`

### Previous story intelligence

- Story 2.3 established the override + differential + sentinel regime; this reuses it. WAnomaly is population-wide (no owner) — `OwnerClientId` is the inert field.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Overrode `WAnomalyCorruption.CheckCondition(GameSnapshot)` (reads `IsFake` filter + `IsCorrupted`; vacuously true over empty non-fake population). Pull untouched.
- Full-matrix differential (all-corrupted true / one-not-corrupted false / empty vacuous-true) green. Sentinel: `IsCorrupted` and `IsFake` each bite under targeted scenarios; `OwnerClientId` proven inert (population-wide condition reads no owner id).
- Migration 10/10 (WMarginal + WAnomaly); regression 42/42 goldens green. Shippable per commit.

### File List

- **Modified:** `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs` (added `CheckCondition(GameSnapshot)` override + `using CorruptionDuPortail.Domain;`)
- **Added:** `Assets/Scripts/Tests/PlayMode/Migration/WAnomalyMigrationTests.cs`

### Change Log

- 2026-06-10 — Migrated `WAnomalyCorruption` to the snapshot signature under the 2.3 regime. Story 2.4 → review.
