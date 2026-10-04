# Story 4.3: Migrate `PCorruptingMark` (Corruption)

Status: done

## Story

As a developer (Poyo),
I want `PCorruptingMark`'s server click-body resolution moved into the Domain `PowerResolver` and the adapter re-pointed to dispatch its descriptors,
so that the third power lands with its coupled effect choreography preserved.

## Acceptance Criteria

**Given** the regime of 4.1–4.2
**When** `PCorruptingMark`'s `OnCardClickedRpc` resolution moves into the POCO and the adapter dispatches its descriptors
**Then** all guards pass (resolver purity, exhaustive synchronous dispatch, golden trace unchanged incl. its `clientId >= 100` case, EditMode tests)
**And** the commit is shippable on its own
**And** a manual A/B human playtest (before/after) confirms no perceived FMOD+Focus timing shift *(deferred — see note)*

## Implementation notes (binding)

1. **`PowerResolver.ResolveCorruptingMarkClick(ownerSlot, targetSlot)`** → `[NewTargeting, StoreLastCorrupted(target), CorruptionSucceeded(target), CorruptPlayer(target)]`. Target validity is an adapter precondition (the invalid branch raises corruption-failed before resolving).
2. **Dispatcher unchanged** — `NewTargeting` + `CorruptPlayer` are already shared bricks (`CorruptPlayer` dispatches `GetCharacter(slot, false).CorruptPlayerServerRpc()`, matching the original `false` fetch here exactly).
3. **Power-LOCAL bricks via `ApplyLocalEffect`**: `StoreLastCorrupted` → `lastCorruptedCharacterId.Value` (private NV); `CorruptionSucceeded` → `InvokeOnCharacterCorruptionSuccessfulRpc` (the power's `[Rpc(Everyone)]` event raiser).
4. **`OnCardClickedRpc` re-pointed**: resolve → `foreach Dispatch(effect, ApplyLocalEffect)`.

## Scope / deferred

- Golden-covered path = `OnCardClickedRpc` body only. `OnCharacterPicked` (invalid→`CorruptionFailed`, post-click `RevealInfo`, `OnUsed`) and `StopUse`→`DestroyAllArrows` keep their 4.0 inline observation `Record`s (not golden-covered) → 4.5 cleanup. Consistent with 4.2.
- **Manual A/B playtest** (FMOD+Focus game-feel) is a human task for Poyo — cannot run autonomously. The intention-level golden + full suite are green; the timing guardrail (synchronous dispatch, no yield between bricks) is structurally preserved. Flagged for a waking-hours playtest.

## Tasks / Subtasks

- [x] **T1 — `ResolveCorruptingMarkClick`** in Domain; `DomainPurity` green.
- [x] **T2 — Re-point `PCorruptingMark.OnCardClickedRpc`** + `ApplyLocalEffect` (StoreLastCorrupted, CorruptionSucceeded).
- [x] **T3 — EditMode resolver test** (ordered click-body trace).
- [x] **T4 — Prove** — `PowerGolden` (PCorruptingMark) unchanged; **147 EditMode + 138 PlayMode** green.

## Dev Agent Record

### Agent Model Used
claude-opus-4-8

### Completion Notes List
- Corruption click body migrated; dispatcher needed no new shared cases. Power-local bricks (private NV write + event-raiser RPC) routed via the `powerLocal` callback established in 4.2.
- Golden 4.0 `PCorruptingMark_Trace_IsPinned` passes UNCHANGED (target `clientId == 100`).
- Gate: 147 EditMode + 138 PlayMode green; behavior-preserving.

### File List
- **Modified:** `Assets/Scripts/Domain/PowerResolver.cs` (ResolveCorruptingMarkClick), `Assets/Scripts/Characters/Powers/PCorruptingMark.cs` (OnCardClickedRpc → resolve+dispatch, ApplyLocalEffect), `Assets/Scripts/Tests/Editor/PowerResolverTests.cs` (Corruption test)

## Change Log
- 2026-06-11 — Story 4.3 implemented; PCorruptingMark click body migrated; golden unchanged; 147 EM + 138 PM; status → done. Manual A/B playtest deferred to waking hours.
