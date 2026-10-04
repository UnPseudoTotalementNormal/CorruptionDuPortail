# Story 4.5: Cleanup — remove the legacy inline effect path

Status: done

## Story

As a developer (Poyo),
I want the now-dead legacy inline effect scaffolding removed once all four powers are re-pointed,
so that no power carries both the POCO resolver and old inline observation (NFR7).

## Acceptance Criteria

**Given** all four powers re-pointed (4.1–4.4)
**When** the cleanup lands
**Then** the legacy inline effect-resolution code is removed by destructive deletion; the compiler enumerates any remaining call site
**And** no orphan side-effect remains (a static/grep absence proof of the old inline effect symbols)
**And** the full golden/trace suite passes unchanged and a boot smoke-test (start→use a power→finish without exception) is green
**And** the full EditMode + PlayMode suite is green (NFR6 gate before Wave 4)

## Design note (option-ii consequence)

The 4.0 sub-fork chose an **observation** seam (not an executing one), so the per-power migrations (4.1–4.4) **replaced** each golden-covered path's inline effects with resolver+dispatch — they never created a parallel inline branch. NFR7 ("no power carries both the POCO resolver and the old inline branch") is therefore **satisfied by construction** for every migrated path. The only "legacy inline" left was the **vestigial 4.0 observation `Record` calls** in concrete-power paths that no golden ended up driving (Entrapment's `AttributeBoundByInkChat` + awakening undiscover; Corruption's `OnCharacterPicked` invalid/reveal branch, `StopUse` arrows, `OnConcentratedEffectServer`). Those `Record`s are no-ops in production and observed by no golden → dead scaffolding. This story removes them.

## What changed

- **Removed 7 vestigial inline `PowerEffectTrace.Record(...)`** calls — 3 in `PBoundByInk`, 4 in `PCorruptingMark` — leaving the real (un-golden-scoped) effect calls untouched.
- **Kept** the base `Power.cs` plumbing observation `Record`s (load-bearing: the goldens observe the `OnUsed`→StopUse/OnUsedServer base tail) and the single `PowerEffectDispatcher` dispatch `Record`.

## NFR7 absence proof (grep)

`grep -rn "PowerEffectTrace.Record" Assets/Scripts/Characters/Powers/` now returns **only**:
- `Power.cs` (base plumbing observation — by design, the adapter-effect bricks),
- `PowerEffectDispatcher.cs` (the one dispatch-time `Record`),
- `PowerEffectTrace.cs` (the seam's doc comment).

**Zero** inline effect `Record` in any concrete power (`PCursedVision` / `PBoundByInk` / `PCorruptingMark` / `POmniscience`) — every golden-covered effect flows through the resolver → dispatcher.

## Boot smoke-test

The PlayMode suite spawns powers and drives them end-to-end without exception — `PowerTests` (CanUse), `VisionPowerTests` (Omniscience/CorruptionInsight reveal), `EntrapmentPowerTests` (TruthChains/Legacy), `PowerGoldenTraceTests` (all four migrated powers' server bodies). The full green PlayMode run is the start→use→finish smoke coverage.

## Tasks / Subtasks

- [x] **T1 — Remove vestigial inline `Record`s** (PBoundByInk ×3, PCorruptingMark ×4) by destructive deletion; real effect calls preserved.
- [x] **T2 — Grep absence proof** — no inline effect `Record` remains in any concrete power.
- [x] **T3 — Prove** — full golden suite UNCHANGED; **149 EditMode + 138 PlayMode** green (NFR6 gate before Wave 4); console clean.

## Dev Agent Record

### Agent Model Used
claude-opus-4-8

### Completion Notes List
- Option-ii made NFR7 mostly satisfied-by-construction; the cleanup removed the 7 dead observation `Record`s left in non-golden concrete-power paths. Behavior-preserving (removing a no-op).
- The `EffectDescriptor` variants now unused by production (`CorruptionFailed`, `AssignChatId`, `UndiscoverChat`) stay in the Domain union vocabulary — they remain part of the modelled surface (and `CorruptionFailed` is still covered by `EffectDescriptorTests`); a future story (or Epic 5) can dispatch them when those paths are extracted.
- Gate: golden unchanged; 149 EditMode + 138 PlayMode green; PlayMode suite = boot smoke coverage.

### File List
- **Modified:** `Assets/Scripts/Characters/Powers/PBoundByInk.cs` (−3 Records), `Assets/Scripts/Characters/Powers/PCorruptingMark.cs` (−4 Records)

## Change Log
- 2026-06-11 — Story 4.5 implemented; removed 7 vestigial inline observation Records; grep-absence proof; golden unchanged; 149 EM + 138 PM; status → done. **Epic 4 COMPLETE.**
