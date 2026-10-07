# Story 2.5: Migrate `WChosenChainedAllAnomaly`

Status: done

## Story

As a developer (Poyo),
I want `WChosenChainedAllAnomaly` switched to the snapshot signature,
so that the chaining-dependent condition is migrated with its chaining reads proven lossless.

## Acceptance Criteria

**Given** the regime of 2.3 and the `ChainingResolver` goldens of Story 1.5
**When** the condition is migrated in a single commit
**Then** the field-read trace confirms its chaining/derived reads are all covered by losslessness tests; any newly discovered field reopens 2.1's inventory rather than being silently defaulted
**And** the per-condition sentinel and full differential are green
**And** the commit is shippable on its own

### Acceptance reading notes

1. **Override mirrors the pull** (`WChosenChainedAllAnomaly.cs:19–36`): non-fake characters; non-anomaly faction → `continue`; an anomaly that is not chained → `false`; else `true` (vacuously true with zero anomalies).
   ```csharp
   public override bool CheckCondition(GameSnapshot snapshot)
   {
       foreach (var c in snapshot.Characters)
       {
           if (c.IsFake) continue;
           if (c.FactionType != FactionType.anomaly) continue;
           if (!c.IsChained) return false;
       }
       return true;
   }
   ```
2. **Field-read trace:** reads exactly `IsFake` (filter), `FactionType`, `IsChained`. All three covered by Story 2.1 losslessness — no newly discovered field, so 2.1's inventory is NOT reopened.
3. **Per-field sentinel (targeted):**
   - `IsChained` bites: one chained anomaly (verdict true) → corrupt `IsChained`→false → false.
   - `FactionType` bites: one NOT-chained anomaly (verdict false) → corrupt `FactionType`→chosen → it is `continue`d → true.
   - `IsFake` bites: one NOT-chained anomaly (verdict false) → corrupt `IsFake`→true → excluded → true.
   - inert: `IsCorrupted` (or `OwnerClientId`) leaves the verdict unchanged.
4. **Full-matrix differential:** single chained anomaly (true), single free anomaly (false), no-anomaly population (vacuously true), empty population (vacuously true) → `AssertAgrees` green. **Setup trap (Story 1.2):** every non-fake character MUST have a `role` assigned, or the pull NREs on `role.factionType`.
5. Shippable per commit; pull untouched. `[Category("Migration")]`.

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — Override `CheckCondition(GameSnapshot)`** on `WChosenChainedAllAnomaly` (note 1); pull untouched
- [ ] **T2 — Migration tests** `[Category("Migration")]`: full-matrix differential (note 4) + per-field sentinel (note 3) + trace header (note 2)
- [ ] **T3 — Prove**: `Migration` green; full regression green; console clean

## Dev Notes

**Modified (production):** `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs` — add the override only.
**Created:** `Assets/Scripts/Tests/PlayMode/Migration/WChosenMigrationTests.cs`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.5] (lines 254–266)
- [Source: Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:19–36] — the pull being mirrored
- [Source: Story 1.5 ChainingResolver goldens + 2.1 IsChained losslessness] — chaining reads backed
- [Source: Story 2.3/2.4 migration regime] — template

### Previous story intelligence

- Same regime as 2.3/2.4. Setup trap: assign `role` to every non-fake character (Story 1.2 reading-note 3) or the pull NREs. Chaining reads (`IsChained`) are backed by Story 1.5 + 2.1.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Overrode `WChosenChainedAllAnomaly.CheckCondition(GameSnapshot)` (reads `IsFake`/`FactionType`/`IsChained`; non-anomaly continue'd, free anomaly → false, vacuously true with zero anomalies). Pull untouched. No newly discovered field → 2.1 inventory not reopened.
- Full-matrix differential (chained-anomaly true / free-anomaly false / no-anomaly vacuous / empty vacuous) green. Sentinel: `IsChained`, `FactionType`, `IsFake` each bite under targeted scenarios; `IsCorrupted` inert.
- Migration 18/18; goldens 42/42 green. Shippable per commit.

### File List

- **Modified:** `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs` (added `CheckCondition(GameSnapshot)` override + `using CorruptionDuPortail.Domain;`)
- **Added:** `Assets/Scripts/Tests/PlayMode/Migration/WChosenMigrationTests.cs`

### Change Log

- 2026-06-10 — Migrated `WChosenChainedAllAnomaly` to the snapshot signature under the 2.3 regime. Story 2.5 → review.
