# Story 4.1: Migrate `PCursedVision` (simplest — proves the resolver+dispatch pattern)

Status: done

## Story

As a developer (Poyo),
I want `PCursedVision`'s effect arithmetic extracted into a Domain `PowerResolver` and its adapter re-pointed to dispatch the descriptors,
so that the lowest-variance power rods the extraction pattern before the harder powers.

## Acceptance Criteria

**Given** the 4.0 `EffectDescriptor`, observation seam, and global golden
**When** `PCursedVision`'s resolution moves into `PowerResolver` (decision-only, returns an ordered `IReadOnlyList<EffectDescriptor>`, NFR4)
**Then** the resolver references no `NetworkVariable`/`RpcParams`/`GetSafeRpcTarget`/FMOD/Focus/`NetworkObject`
**And** the adapter dispatches the descriptors via an exhaustive `switch`, synchronously (no yield between bricks), preserving the live order
**And** `PCursedVision`'s golden trace passes unchanged (bit-for-bit)
**And** the `PCursedVision` resolver tests run as EditMode

## Implementation notes (binding)

1. **`PowerResolver.ResolveCursedVision(ownerSlot, targetSlot, targetIsChosen, targetPseudo, cursedVisionCardEffectId, serverChatWindowId)`** → ordered list: `NewTargeting`, `CorruptPlayer(target)`, `RevealInfo(target,CorruptRevealed,Personal,owner,false)`, `AddCardEffect(cardId,target,hidden)`, `ChatLocal(verdict)`, `CorruptPlayer(owner)`, `RevealInfo(owner,CorruptRevealed,Personal,owner,false)`. Chosen → card flag `false` + "est un élu."; else → flag `true` + "n'est pas un élu." Engine ids (card id, chat window id) are PASSED IN as ints so the Domain holds no Game-enum magic constant.
2. **`PowerEffectDispatcher.Dispatch(effect)`** (Game): `PowerEffectTrace.Record(effect)` then an exhaustive `switch` executing the verbatim singleton/RPC effect (`NewTargeting`→`RoleTargetSystem`, `CorruptPlayer`→`GetCharacter(slot,false).CorruptPlayerServerRpc()`, `RevealInfo`→`Set/SendRevealLevelRpc` with `RevealField`→`nameof(CharacterInfoReveal.*)` + `RevealVisibility`→`RevealLevel`, `AddCardEffect`→`CardEffectManager` with the bool as `object _effectData`, `ChatLocal`→`AddMessageLocal`). `default` throws (runtime exhaustiveness guard; per-power bricks added by 4.2–4.4). The dispatch loop in `OnCharacterPicked` is synchronous.
3. **`PCursedVision.OnCharacterPicked`** re-pointed: `CheckIsTargetValid` gate unchanged → `_resolver.ResolveCursedVision(...)` → `foreach` dispatch → `OnUsed()` (base plumbing unchanged).

## Analyzed divergence (documented, low-risk)

The original corrupted the owner via `GetCharacter(owner)` with the DEFAULT `_triggerUpdate:true` (schedules an end-of-frame `OnCharactersListUpdated`), while the target used the picked `_character` ref (no trigger). The unified `CorruptPlayer` brick dispatches via `GetCharacter(slot, false)`; the owner-site's incidental `triggerUpdate:true` is dropped as **redundant** — `OnUsedServer`'s `AskForUpdateAllCharactersRpc` fires in the same synchronous turn and broadcasts a full refresh. No test exercises `PCursedVision` behaviorally; the golden + full suite stay green.

## Tasks / Subtasks

- [x] **T1 — `PowerResolver.ResolveCursedVision`** in Domain (note 1); `DomainPurity` green.
- [x] **T2 — `PowerEffectDispatcher`** in Game (note 2).
- [x] **T3 — Re-point `PCursedVision.OnCharacterPicked`** (note 3); inline effects + inline `Record`s removed.
- [x] **T4 — EditMode `PowerResolverTests`** `[Category("PowerResolver")]` — ordered trace, chosen/non-chosen branch, engine-id echo, target-then-owner.
- [x] **T5 — Prove** — `PowerGolden` (PCursedVision) unchanged; **144 EditMode + 138 PlayMode** green.

## Dev Agent Record

### Agent Model Used
claude-opus-4-8

### Completion Notes List
- `PowerResolver` (Domain, decision-only) + shared `PowerEffectDispatcher` (Game, exhaustive switch + Record + verbatim execute, synchronous). `PCursedVision.OnCharacterPicked` now: validate → resolve → dispatch loop → `OnUsed()`.
- Golden 4.0 `PCursedVision_Trace_IsPinned_NonChosenTarget` passes UNCHANGED — the resolver-produced + dispatched order is bit-for-bit identical to the former inline order (incl. the base tail from `OnUsed`).
- Owner-corrupt `triggerUpdate:true` drop documented above (redundant).
- Gate: 144 EditMode + 138 PlayMode green; behavior-preserving.

### File List
- **Added:** `Assets/Scripts/Domain/PowerResolver.cs`, `Assets/Scripts/Characters/Powers/PowerEffectDispatcher.cs`, `Assets/Scripts/Tests/Editor/PowerResolverTests.cs`
- **Modified:** `Assets/Scripts/Characters/Powers/PCursedVision.cs` (OnCharacterPicked → resolve+dispatch)

## Change Log
- 2026-06-11 — Story 4.1 implemented; PCursedVision migrated to `PowerResolver` + `PowerEffectDispatcher`; golden unchanged; 144 EM + 138 PM; status → done.
