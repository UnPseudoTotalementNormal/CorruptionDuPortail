# Story 2.6: Migrate `WOmniscienceHackedCharacter` (hardest — 4-way conjunction)

Status: done

## Story

As a developer (Poyo),
I want `WOmniscienceHackedCharacter` switched to the snapshot signature with each conjunction term independently sentinel-guarded,
so that the highest-complexity condition cannot pass with dead terms.

## Acceptance Criteria

**Given** the regime of 2.3 and the dedicated `hackedCharacterClientId` losslessness test (2.1)
**When** the condition is migrated in a single commit
**Then** **four** mutation-sentinels are instantiated — one per term of the 4-way conjunction — each proving its differential goes red when its term is corrupted (no term may be silently dead)
**And** both branches of the double lookup (found / not-found) are exercised
**And** the full differential is green and the commit is shippable on its own

### Acceptance reading notes (binding)

1. **Override mirrors the pull** (`WOmniscienceHackedCharacter.cs:15–36`), **including the no-owner-guard NRE** (the Story 1.2 golden O1: owner not found → `NullReferenceException`). To preserve it, do NOT guard the owner lookup:
   ```csharp
   public override bool CheckCondition(GameSnapshot snapshot)
   {
       CharacterSnapshot owner = FindOwner(snapshot, ownerClientId); // null if absent
       ulong hackedId = owner.HackedByOmniscienceTarget;             // NO null guard — mirrors the pull's owner.role.powers NRE (golden O1)
       if (hackedId == POmniscience.HACKED_CHARACTER_DEFAULT) return false;
       foreach (var c in snapshot.Characters)
       {
           if (c.OwnerClientId == hackedId)
           {
               return c.IsChained && c.FactionType == FactionType.chosen;
           }
       }
       return false; // hacked target not found
   }
   ```
   **Why `hackedId == DEFAULT` covers both pull early-returns:** the pull returns false when (a) no `POmniscience` in powers, and (b) `POmniscience` present but `hackedCharacterClientId == DEFAULT`. The Story 2.1 builder maps *both* to `HackedByOmniscienceTarget == DEFAULT`, so the single snapshot guard is verdict-equivalent to both pull branches.

2. **The 4-way conjunction terms (the TRUE path) → 4 sentinels:**
   | Term | Snapshot read | Sentinel: corrupt → verdict flips true→false |
   |---|---|---|
   | 1. target was set | owner `HackedByOmniscienceTarget != DEFAULT` | set owner's hack target → `DEFAULT` |
   | 2. target exists | a character with `OwnerClientId == hackedId` | set owner's hack target → an absent id |
   | 3. target chained | hacked `IsChained` | corrupt hacked `IsChained` → false |
   | 4. target chosen | hacked `FactionType == chosen` | corrupt hacked `FactionType` → anomaly |
   Each must independently flip a base TRUE scenario → no term is silently dead.

3. **Double lookup found / not-found (AC line 279):** term 2 corruption (target → absent id) is the **not-found** branch (→ false); the base TRUE scenario is the **found** branch. Both exercised.

4. **Full-matrix differential (`AssertAgrees`)** for the non-throwing cases: base true; target == DEFAULT (false); no `POmniscience` on owner (false); target set but not found (false); hacked found but not chained (false); hacked found + chained but not chosen (false). **O1 (owner absent)** is handled SEPARATELY: both pull and snapshot must throw `NullReferenceException` (`Assert.Throws` on each) — `AssertAgrees` cannot be used since the call throws.

5. **Field-read trace:** owner `HackedByOmniscienceTarget`; hacked `IsChained` + `FactionType`; `OwnerClientId` as match key. All covered by Story 2.1 losslessness (incl. the dedicated live-`POmniscience` test). Inert fields: `IsFake`, `IsCorrupted`.

6. Shippable per commit; pull untouched. `[Category("Migration")]`. This is the last condition before the Story 2.7 cut (REVIEW-REQUIRED).

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — Override `CheckCondition(GameSnapshot)`** on `WOmniscienceHackedCharacter` (note 1); pull untouched
- [ ] **T2 — Migration tests** `[Category("Migration")]`: full-matrix differential (note 4) + O1 throw-parity + 4 conjunction-term sentinels (note 2) + found/not-found (note 3)
- [ ] **T3 — Prove**: `Migration` green; full regression green; console clean

## Dev Notes

**Modified (production):** `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs` — add the override only.
**Created:** `Assets/Scripts/Tests/PlayMode/Migration/WOmniscienceMigrationTests.cs`.

### Live-POmniscience setup (reuse)

`POmniscience : Power : NetworkBehaviour` cannot be `new`-ed — spawn via GameObject + NetworkObject + `Spawn()` (`SpawnOmniscience` pattern), `owner.role.powers.Add(omni)`, set `omni.hackedCharacterClientId` (Story 2.1 / `WinningConditionGoldenMasterTests`).

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.6] (lines 268–280)
- [Source: Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:15–36] — the pull being mirrored (incl. the no-owner-guard NRE)
- [Source: Assets/Scripts/Characters/Powers/POmniscience.cs:14–15] — `hackedCharacterClientId` + `HACKED_CHARACTER_DEFAULT`
- [Source: Story 1.2 golden O1] — owner-not-found throws NRE (captured behavior to preserve)
- [Source: Story 2.1 GameSnapshotBuilderLosslessnessTests] — live-POmniscience losslessness + spawn pattern

### Previous story intelligence

- Same regime as 2.3–2.5, plus the O1 throw-parity and the 4-term conjunction sentinels. The builder collapses "no omni" and "omni-but-default" into `HackedByOmniscienceTarget == DEFAULT` — verdict-equivalent to both pull early-returns (proven by the differential).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Hardest migration: overrode `WOmniscienceHackedCharacter.CheckCondition(GameSnapshot)` mirroring the 4-way conjunction + double lookup AND the golden O1 no-owner-guard NRE. The single `hackedId == DEFAULT` guard is verdict-equivalent to both pull early-returns (no POmniscience / POmniscience-but-default), since the 2.1 builder maps both to `HACKED_CHARACTER_DEFAULT`.
- Full differential (base true / target-default / no-omni / target-not-found / hacked-not-chained / hacked-not-chosen) green. O1 owner-absent → both pull and snapshot throw `NullReferenceException` (parity asserted separately). Four conjunction-term sentinels each turn the base TRUE scenario false; the not-found lookup branch is exercised via term 2.
- Migration 29/29 (all 4 conditions); goldens 42/42 green. Shippable per commit. **All 4 winning conditions now migrated — Story 2.7 (the cut, REVIEW-REQUIRED) is unblocked.**

### File List

- **Modified:** `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs` (added `CheckCondition(GameSnapshot)` override + `using CorruptionDuPortail.Domain;`)
- **Added:** `Assets/Scripts/Tests/PlayMode/Migration/WOmniscienceMigrationTests.cs`

### Change Log

- 2026-06-10 — Migrated `WOmniscienceHackedCharacter` (4-way conjunction, double lookup, O1 NRE parity, 4 term sentinels). Completes the Wave-1 condition migrations. Story 2.6 → review.
