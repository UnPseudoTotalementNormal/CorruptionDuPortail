# Story 4.2: Migrate `PBoundByInk` (Entrapment)

Status: done

## Story

As a developer (Poyo),
I want `PBoundByInk`'s click-path resolution moved into the Domain `PowerResolver` and the adapter re-pointed to dispatch its descriptors,
so that the second power lands shippable under the same proof regime, exercising the power-LOCAL state-store brick.

## Acceptance Criteria

**Given** the regime established in 4.1
**When** `PBoundByInk`'s `OnCardClickedRpc` resolution moves into the POCO and the adapter dispatches its descriptors
**Then** all guards pass (no transport/engine type in the resolver, exhaustive synchronous dispatch, golden trace unchanged incl. its `clientId >= 100` case, EditMode tests)
**And** the commit is shippable on its own
**And** `GetSafeRpcTarget` / the `clientId >= 100` interception stay adapter-side (NFR5)

## Implementation notes (binding)

1. **`PowerResolver.ResolveBoundByInkClick(ownerSlot, targetSlot, chatId)`** → `[NewTargeting, DiscoverChat(chatId, "Lié par l'encre", Specific(target)), RegisterInkTarget(target)]`. The dedupe guard (already-targeted) is an adapter precondition checked BEFORE resolving. The chat id is passed in (the adapter owns the `NetworkVariable`).
2. **Dispatcher gains the `DiscoverChat` shared brick** → `ChatManager.DiscoverChatRpc(chatId, name, ResolveTarget(audience))` where `ResolveTarget(Specific(slot)) = CharacterManager.instance.GetSafeRpcTarget((ulong)slot)` — the `clientId >= 100` bot interception lives ONLY here (NFR5). Dispatcher also gains an optional `powerLocal` callback: bricks not handled by the shared `switch` are routed to it (else throw).
3. **`RegisterInkTarget` is power-LOCAL** (writes the private `currentTargets` / `alreadyTargetedClients` `NetworkList`s) → handled by `PBoundByInk.ApplyLocalEffect` via the `powerLocal` callback, keeping `PowerEffectTrace.Record` centralized in the dispatcher.
4. **`OnCardClickedRpc` re-pointed**: dedupe guard → `_resolver.ResolveBoundByInkClick(...)` → `foreach` `PowerEffectDispatcher.Dispatch(effect, ApplyLocalEffect)`.

## Scope note

Only the golden-covered **click path** (`OnCardClickedRpc`) is resolver-extracted. `AttributeBoundByInkChat` / the awakening `UndiscoverChat` (driven from `OnGameStartedServer`, not in the 4.0 golden) keep their 4.0 inline observation `Record`s — extracting them without a golden would be unverified. Folded into 4.5 cleanup scope.

## Tasks / Subtasks

- [x] **T1 — `ResolveBoundByInkClick`** in Domain; `DomainPurity` green.
- [x] **T2 — Dispatcher** `DiscoverChat` shared case + `ResolveTarget` (NFR5 GetSafeRpcTarget) + `powerLocal` callback.
- [x] **T3 — Re-point `PBoundByInk.OnCardClickedRpc`** + `ApplyLocalEffect` (RegisterInkTarget).
- [x] **T4 — EditMode resolver tests** (ordered trace, DiscoverChat targets the picked slot, chat-id echo).
- [x] **T5 — Prove** — `PowerGolden` (PBoundByInk) unchanged; **146 EditMode + 138 PlayMode** green.

## Dev Agent Record

### Agent Model Used
claude-opus-4-8

### Completion Notes List
- Entrapment click path migrated. New dispatcher pattern for power-LOCAL state bricks: the shared `switch` handles global bricks, `default → powerLocal(effect)` routes power-coupled bricks (here `RegisterInkTarget` → private NetworkList writes) to the power, with `Record` still centralized.
- `DiscoverChat` dispatch maps `Specific(slot) → GetSafeRpcTarget` — NFR5 interception stays adapter-side; the Domain descriptor carries only a logical slot.
- Golden 4.0 `PBoundByInk_Trace_IsPinned` passes UNCHANGED (target `clientId == 100` case).
- Gate: 146 EditMode + 138 PlayMode green; behavior-preserving.

### File List
- **Modified:** `Assets/Scripts/Domain/PowerResolver.cs` (ResolveBoundByInkClick), `Assets/Scripts/Characters/Powers/PowerEffectDispatcher.cs` (DiscoverChat + powerLocal + ResolveTarget), `Assets/Scripts/Characters/Powers/PBoundByInk.cs` (OnCardClickedRpc → resolve+dispatch, ApplyLocalEffect), `Assets/Scripts/Tests/Editor/PowerResolverTests.cs` (Entrapment tests)

## Change Log
- 2026-06-11 — Story 4.2 implemented; PBoundByInk click path migrated; dispatcher gains DiscoverChat + power-local callback; golden unchanged; 146 EM + 138 PM; status → done.
