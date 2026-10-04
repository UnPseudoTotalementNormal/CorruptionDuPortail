# Story 4.0: Inventory + define `EffectDescriptor` (union of all powers) + seams + global golden

Status: done

## Story

As a developer (Poyo),
I want the per-power side-effect inventory done, the `EffectDescriptor` vocabulary frozen on the union of all four powers, the observation seams in place, and a global golden trace captured on the current code,
so that the vocabulary is designed for maximum diversity before any extraction and every later per-power story has a faithful oracle.

## Acceptance Criteria

**Given** effect logic lives inside `Power` / `PowerManager` and the concrete powers, mixing arithmetic with FMOD / Focus / RPC / ownership side-effects
**When** the foundation lands
**Then** a line-by-line side-effect inventory of `Power.cs` (base) + the four concrete powers is produced; each effect is classified *decision (the what)* vs *transport/IO (the how)* and mapped to a descriptor brick — the inventory IS the spec of `EffectDescriptor`
**And** `EffectDescriptor` (closed discriminated value type, immutable, value-equatable, **no** `EventReference` / `RpcParams` / `clientId` / `NetworkObject`) is defined in `Domain` on the union of all four powers with structural-invariant tests (value equality, immutability)
**And** the observation seams are introduced in the adapter with their default behavior verbatim (no behavior change — the full PlayMode suite stays green)
**And** a global golden trace (ordered intention+dispatch `RecordedEffect` list per power, incl. at least one `clientId >= 100` case per RPC-emitting power) is captured on the CURRENT code as the standing oracle — no `PowerResolver` exists yet

## Recorded architecture decision (night-mode fork, converged in autonomy)

> **Fork.** The epic sketched `EffectDescriptor` against a small transport-only brick set (`PlaySound` / `UnfocusAll` / `NotifyClients` / `DecrementUses` / `TransferOwnership` / `RequestCharacterRefresh`) observed through four seams (`IPowerAudio` / `IFocusSink` / `INetObjectGraph` / `IRpcDispatch`). The line-by-line inventory below shows the **real** effect surface of the concrete powers is dominated by **game-domain** effects routed through gameplay singletons — `RoleTargetSystem.NewTargeting`, `GameInfoRevealer.Set/SendRevealLevel`, `Character.CorruptPlayerServerRpc`, `CardEffectManager.AddCardEffect`, `ChatManager.AddMessageLocal/Discover/Undiscover/ReceiveChat`, `ArrowManager.DestroyAllArrows` — **none** of which the four sketched seams cover. So the sketched vocabulary is materially incomplete.
>
> **Considered.**
> - **Shape A — thin seams, model only the four transport categories.** `EffectDescriptor` stays the small transport set; the game-domain effects remain inline in each adapter handler. *Rejected:* for these four powers there is almost no transport-arithmetic to extract — the meat is the game-domain sequence — so `PowerResolver` would be near-empty and the epic's FR11 goal ("extract the power effect arithmetic") evaporates.
> - **Shape B — model the full game-effect union; expand the seam set (CHOSEN).** `EffectDescriptor` enumerates every distinct domain effect-kind across the four powers (the ~15 below). A power resolves to an ordered `IReadOnlyList<EffectDescriptor>` of **intentions**; the adapter dispatches each via an exhaustive `switch` to the real singleton/RPC, with `GetSafeRpcTarget` + the `clientId >= 100` bot-interception living **only** in the adapter. This is the faithful realization of the epic's explicit mandate — *"the inventory IS the spec of `EffectDescriptor`"*, *"union of all four powers"*, *"closed discriminated value type … exhaustive switch"*. The four named seams were `e.g.` sketches; the inventory supersedes them.
>
> **Why not party-mode.** The epic already fixes the design *in principle* (union vocabulary, exhaustive switch, decision-only NFR4, transport/engine types adapter-only NFR5); the inventory fixes the *specifics*. A multi-agent deliberation would re-derive that cold. The fork is resolved here, with alternatives recorded, per the night protocol's "converge in autonomy, record the consensus."
>
> **Power mapping** (epic logical name → concrete class; a refactor-scoping call, not game design — Poyo's mechanics are characterized verbatim):
> - `PVision` (4.1, simplest) → **`PCursedVision`** (single-target picked-power; richest leaf union: corrupt + reveal + card-effect + branch-chat, on both target and owner)
> - `PEntrapment` (4.2) → **`PBoundByInk`** (network-heavy: `DiscoverChat`/`UndiscoverChat` + `NetworkList` dedupe + `GetSafeRpcTarget`)
> - `PCorruption` (4.3) → **`PCorruptingMark`** (`ICorrupterPower` + concentrated effect + arrows + fail/success events)
> - `POmniscience` (4.4, hardest/hack) → **`POmniscience`** (role reveal + `hackedCharacterClientId` store)
>
> **Seam set (revised, Shape B).** A single dispatch seam **`IPowerEffectSink`** (Game-side interface, *not* Domain) with one method per descriptor brick; the **default** impl performs today's singleton/RPC calls **verbatim**. The four epic-named concerns survive as *brick families* inside it (audio bricks, focus bricks, net/refresh bricks, notify/RPC bricks) rather than four separate interfaces — one sink keeps the dispatch ORDER in one exhaustive `switch`, which is the invariant that matters.
>
> **Sub-fork — does the 4.0 seam EXECUTE or only OBSERVE?**
> - *(i) Executing sink:* the seam owns the dispatch `switch` and the inline effect calls are removed; perfect oracle fidelity (the descriptor IS the source of the effect, cannot drift) but it forces clientId→`int`-slot narrowing on the LIVE path and a big switch now — i.e. it introduces production risk into a behavior-preserving foundation story.
> - **(ii) Observation seam (CHOSEN):** the seam is a lightweight ambient trace — `PowerEffectTrace.Record(descriptor)`, default observer is a **no-op**, so the verbatim inline effects are **untouched** (behavior trivially preserved, ZERO production risk; "default behavior verbatim" is literally true). AC3 itself names these *observation* seams. The recording golden swaps in a spy observer, drives each power, asserts the ordered descriptor list, resets in teardown. The dispatch `switch` + slot mapping (and any clientId narrowing) are deferred to the per-power stories 4.1–4.4, where each is scoped small and gated individually against THIS golden. The recorded descriptor and the adjacent inline call are written together at each site (drift-mitigation); the existing behavioral tests (`VisionPowerTests`/`EntrapmentPowerTests`) keep asserting the real singleton outcomes as a cross-check.
>
> *Rationale:* a foundation story's prime directive is "no behavior change" (NFR6); option (i)'s production risk on a story whose only job is to build a trustworthy oracle is the wrong trade. A caught behavior regression (gate) is cheaper than a silent live-path narrowing bug. 4.1 converts PVision from "inline effect + adjacent `Record`" to "`PowerResolver` returns the list + adapter dispatch switch executes" and proves the dispatch reproduces this golden's exact intention order.

## Line-by-line side-effect inventory

Legend — **D** = decision (the *what*, belongs in the resolver) · **IO/T** = transport/IO (the *how*, stays in the adapter) · **P** = common invocation plumbing (adapter, pinned as adapter effects).

### `Power.cs` (base — common plumbing)

| Line | Call | Class | Descriptor brick |
|---|---|---|---|
| 120 | `GameAudioManager.PlayEventInstance(canalisationSound, CANALISATION_SOUND_KEY)` | IO/T (FMOD) | `PlayLoopingSound(soundId, loopKey)` |
| 121 | `onStartUse?.Invoke()` | P (event) | not modelled (adapter event) |
| 149 | `OnUsedServerRpc()` (client→server hop) | IO/T | not modelled (invocation transport) |
| 155 | `onPowerUsed?.Invoke()` (NetworkAction) | P | not modelled |
| 156–159 | `OnUsedClientRpc(GetSafeRpcTarget(ownerClientId))` if owner≠server | IO/T (**NFR5 site**) | `NotifyOwnerUsed(ownerSlot)` |
| 164 | `powerUseLeft.Value -= 1` | P (NV write) | `DecrementUses` *(stays in adapter per invariant; pinned as adapter effect)* |
| 165 | `onPowerUsedServer?.Invoke()` | P | not modelled |
| 166 | `characterManager.AskForUpdateAllCharactersRpc()` | P (RPC) | `RequestCharacterRefresh` *(stays in adapter; pinned)* |
| 182–184 | `RuntimeManager.PlayOneShot(onUsedSound)` (if path) | IO/T (FMOD) | `PlayOneShotSound(soundId)` |
| 186–189 | `FocusManager.UnfocusAll()` (if `isCurrentlyUsed`) | IO/T (Focus) | `UnfocusAll` |
| 191–193 | `GameAudioManager.StopEventInstance(CANALISATION_SOUND_KEY)` | IO/T (FMOD) | `StopLoopingSound(loopKey)` |
| 196 | `onStopUse?.Invoke()` | P | not modelled |
| 212–242 | `OnReparentedServer/ClientRpc` (ownership + reparent + powers list) | IO/T (NGO ownership) | **OUT OF SCOPE → Epic 5** (ownership-replication parity) |

### `POmniscience` (4.4 — the hack)

| Line | Call | Class | Descriptor brick |
|---|---|---|---|
| 29 | `RoleTargetSystem.NewTargeting(owner, target)` | D→IO/T | `NewTargeting(ownerSlot, targetSlot)` |
| 30 | `OnCardClickedServerRpc(target)` | IO/T | (transport hop to the server body below) |
| 31 | `OnUsed()` | P | base plumbing |
| 40 | `RoleTargetSystem.NewTargeting(owner, target)` (server echo) | D→IO/T | `NewTargeting` |
| 41 | `hackedCharacterClientId = target` | D (field store) | `StoreHackTarget(targetSlot)` |
| 42–43 | `gameInfoRevealer.SendRevealLevelRpc(target, isRoleRevealed, Personal, owner, true)` | D→IO/T | `RevealInfo(targetSlot, RoleRevealed, Personal, viewerSlot=ownerSlot, broadcast:true)` |
| 44 | `AskForUpdateAllCharactersRpc()` | P | `RequestCharacterRefresh` |

### `PCursedVision` (4.1 — Vision)

| Line | Call | Class | Descriptor brick |
|---|---|---|---|
| 28 | `RoleTargetSystem.NewTargeting(owner, target)` | D→IO/T | `NewTargeting` |
| 29 | `target.CorruptPlayerServerRpc()` | D→IO/T | `CorruptPlayer(targetSlot)` |
| 30–31 | `gameInfoRevealer.SetRevealLevel(target, isCorruptRevealed, Personal, owner)` | D→IO/T | `RevealInfo(targetSlot, CorruptRevealed, Personal, ownerSlot, broadcast:false)` |
| 32–41 | branch `role.factionType == chosen` → `CardEffectManager.AddCardEffect(CursedVision, target, false)` + `ChatManager.AddMessageLocal("est un élu", SERVER, Server)` / else `AddCardEffect(…, true)` + `AddMessageLocal("n'est pas un élu", …)` | **D (branch)** + IO/T | `AddCardEffect(CursedVision, targetSlot, hideForChosen)` + `ChatLocal(messageKey, SERVER, Server)` |
| 42 | `owner.CorruptPlayerServerRpc()` (self) | D→IO/T | `CorruptPlayer(ownerSlot)` |
| 43 | `gameInfoRevealer.SetRevealLevel(owner, isCorruptRevealed, Personal, owner)` | D→IO/T | `RevealInfo(ownerSlot, CorruptRevealed, Personal, ownerSlot, false)` |
| 44 | `OnUsed()` | P | base plumbing |

### `PCorruptingMark` (4.3 — Corruption)

| Line | Call | Class | Descriptor brick |
|---|---|---|---|
| 48 | invalid → `InvokeOnCharacterCorruptionFailedRpc(target)` | D (branch) | `CorruptionFailed(targetSlot)` |
| 51 | `OnCardClickedRpc(target)` | IO/T | transport hop to server body |
| 52–53 | `gameInfoRevealer.SetRevealLevel(target, isCorruptRevealed, Personal, owner)` | D→IO/T | `RevealInfo(targetSlot, CorruptRevealed, Personal, ownerSlot, false)` |
| 54 | `OnUsed()` | P | base plumbing |
| 60 | `RoleTargetSystem.NewTargeting(owner, target)` | D→IO/T | `NewTargeting` |
| 61 | `lastCorruptedCharacterId.Value = target` | D (NV store) | `StoreLastCorrupted(targetSlot)` |
| 62 | `InvokeOnCharacterCorruptionSuccessfulRpc(target)` | D→IO/T | `CorruptionSucceeded(targetSlot)` |
| 64 | `clickedCharacter.CorruptPlayerServerRpc()` | D→IO/T | `CorruptPlayer(targetSlot)` |
| 111 | `ArrowManager.DestroyAllArrows()` (in StopUse) | IO/T | `DestroyAllArrows` |
| 116–117 | `OnConcentratedEffectServer`: `SendRevealLevelRpc(lastCorrupted, isRoleRevealed, Personal, owner, true)` | D→IO/T | `RevealInfo(lastCorruptedSlot, RoleRevealed, Personal, ownerSlot, broadcast:true)` |

### `PBoundByInk` (4.2 — Entrapment)

| Line | Call | Class | Descriptor brick |
|---|---|---|---|
| 56 | `OnCardClickedRpc(target)` | IO/T | transport hop |
| 63–66 | dedupe guard (`alreadyTargetedClients`/`currentTargets`) | D (guard) | resolver precondition (no brick) |
| 68 | `RoleTargetSystem.NewTargeting(owner, target)` | D→IO/T | `NewTargeting` |
| 69–70 | `ChatManager.DiscoverChatRpc(powerChatId, "Lié par l'encre", GetSafeRpcTarget(target))` | D→IO/T (**NFR5**) | `DiscoverChat(chatId, chatName, audience=Specific(targetSlot))` |
| 72–73 | `currentTargets.Add` / `alreadyTargetedClients.Add` (NetworkList) | D (state) | `RegisterInkTarget(targetSlot)` |
| 87 | `ChatManager.UndiscoverChatRpc(powerChatId, GetSafeRpcTarget(target))` (awakening) | D→IO/T (**NFR5**) | `UndiscoverChat(chatId, audience=Specific(targetSlot))` |
| 115–117 | `AttributeBoundByInkChat`: assign `powerChatId` + `DiscoverChatRpc(chatId, …, GetSafeRpcTarget(owner))` | D→IO/T | `AssignChatId(chatId)` + `DiscoverChat(chatId, chatName, audience=Specific(ownerSlot))` |

### Union of descriptor bricks (the `EffectDescriptor` closed set)

Audio: `PlayLoopingSound` · `StopLoopingSound` · `PlayOneShotSound`. Focus: `UnfocusAll`. Plumbing (pinned adapter): `DecrementUses` · `RequestCharacterRefresh` · `NotifyOwnerUsed`. Targeting: `NewTargeting`. Reveal: `RevealInfo`. Corruption: `CorruptPlayer` · `CorruptionSucceeded` · `CorruptionFailed`. Card: `AddCardEffect`. Chat: `ChatLocal` · `DiscoverChat` · `UndiscoverChat` · `AssignChatId`. Arrows: `DestroyAllArrows`. State stores: `StoreHackTarget` · `StoreLastCorrupted` · `RegisterInkTarget`.

**Audience model** (Domain-safe, no `clientId`): `PowerEffectAudience = All | Owner | Specific(int logicalSlot)`. The adapter maps `logicalSlot → ulong clientId → GetSafeRpcTarget` (NFR5). `RevealInfo.field` is a Domain enum (`CorruptRevealed | RoleRevealed`) mapped to `nameof(CharacterInfoReveal.*)` in the adapter. `RevealLevel` already exists; if engine-coupled, mirror as a Domain enum.

**OUT OF SCOPE → Epic 5 (HELD):** `OnReparented*` ownership-replication parity; the hack-target's on-client perception (`StartHost` is blind, host==server RTT=0).

## Tasks / Subtasks

- [x] **T0 — Inventory** (this doc) — line-by-line of base + 4 powers; classify D/IO-T/P; map to bricks (= the `EffectDescriptor` spec).
- [x] **T1 — `EffectDescriptor` + `PowerEffectAudience` (+ Domain enums) in `Domain`** — closed discriminated `ValueObject` hierarchy (no records → no `IsExternalInit` polyfill on .NET Standard 2.1), immutable, value-equatable; no `EventReference`/`RpcParams`/`clientId`/`NetworkObject`. `DomainPurity` green.
- [x] **T2 — EditMode structural-invariant tests** `[Category("PowerEffect")]` — 10 tests: value equality, payload inequality, type-driven inequality (Succeeded≠Failed), 5-component `RevealInfo`, singletons, audience semantics, null/wrong-type, ToString stability, list-order.
- [x] **T3 — `PowerEffectTrace` observation seam + no-op default** in the Game adapter; instrumented `Power.cs` base + the 4 concrete powers — each effect EMITS a brick (`PowerEffectTrace.Record`) alongside the untouched verbatim call. No `PowerResolver` yet. `read_console` clean.
- [x] **T4 — Global golden trace** PlayMode `[Category("PowerGolden")]` — recording observer, drive each power's server effect body (Omniscience/Corruption/Entrapment via `OnCardClickedRpc`, Vision via `OnCharacterPicked`), assert the ordered brick list; each RPC power driven with target `clientId == 100` (bot range) → bot-target intention pinned.
- [x] **T5 — Prove** — **140 EditMode + 138 PlayMode** green (NFR6 gate); console clean; all pre-existing tests unchanged (no-op seam = behavior-preserving).

## Dev Notes

**Created:** `Assets/Scripts/Domain/EffectDescriptor.cs`, `Assets/Scripts/Tests/Editor/EffectDescriptorTests.cs`, `Assets/Scripts/Characters/Powers/IPowerEffectSink.cs` (+ default impl), `Assets/Scripts/Tests/PlayMode/PowerGoldenTraceTests.cs`.
**Modified:** `Power.cs` + `PCursedVision.cs` / `PBoundByInk.cs` / `PCorruptingMark.cs` / `POmniscience.cs` (route effects through the sink, verbatim).
**Must NOT change:** the live effect ORDER; `GetSafeRpcTarget` / `clientId >= 100` stay adapter-side; `powerUseLeft -= 1` + `AskForUpdateAllCharactersRpc` stay in the adapter.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.0] (lines 470–483) + Epic 4 cross-cutting invariants (455–468)
- [Source: Assets/Scripts/Characters/Powers/Power.cs] — base plumbing inventory
- [Source: POmniscience.cs / PCursedVision.cs / PCorruptingMark.cs / PBoundByInk.cs] — the four concrete powers
- [Source: Tests/PlayMode/GameLoopTransitionOrderingTests.cs] — the ordered-journal golden pattern to mirror

### Previous story intelligence

- 2.11a/2.11b: ordered-journal goldens use a top-level static journal + distinct top-level recording types; host dispatches its own ClientsAndHost RPC synchronously.
- Domain asmdef: `noEngineReferences:true`, `references:[]` (DomainPurity guard). Relocating a type into Domain needs an explicit Domain ref on test asmdefs ([[reference-domain-asmdef-autoref-tests]]).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- **`EffectDescriptor` union (Domain):** 22 sealed variants over a `ValueObject` base (`abstract class` + `EqualityComponents` → exact-type + ordered-payload value equality), chosen over C# records to avoid the `IsExternalInit` polyfill records' `init` need on Unity's .NET Standard 2.1 profile, and to match the existing Domain style. `PowerEffectAudience` (All/Owner/Specific(slot)) + Domain enums `RevealVisibility` (mirror of `RevealLevel`) / `RevealField`. No transport/engine type in Domain — `DomainPurity` green.
- **Observation seam (option ii):** `PowerEffectTrace.Record(descriptor)` with a no-op default observer → ZERO behavior change in production; the real singleton/RPC effects run untouched. Instrumented `Power.cs` (StartUse/OnUsed/OnUsedServer/StopUse plumbing) + `POmniscience`/`PCursedVision`/`PCorruptingMark`/`PBoundByInk` effect sites, each `Record` adjacent to its verbatim call (drift-mitigation). The dispatch `switch` + slot mapping are deferred to 4.1–4.4.
- **Global golden (4 powers):** `PowerGoldenTraceTests [Category("PowerGolden")]` drives each power's server effect body and pins the exact ordered `EffectDescriptor` list. Each RPC power driven with target `clientId == 100` (bot range) → the bot-target intention is the recorded oracle (the intercepted-vs-wire assertion is a 4.1+ dispatch concern). `LogAssert.ignoreFailingMessages` must be set in the test BODY (the framework resets LogAssert state after `[UnitySetUp]`).
- **Faithfulness findings:** `CardEffectManager.AddCardEffect(id, ulong, object)` — the 3rd arg is `object _effectData` (the `false`/`true` is boxed); descriptor models it as `Flag`. `SelectionFlowService.instance` is a never-null auto-singleton with null-safe internals, so the `OnUsed→StopUse` tail is drivable with no UI setup. `CheckIsTargetValid(100)` passes with default flags (non-local, unrevealed, uncorrupted). Domain `FactionType` lives in namespace `Characters` (single definition, no ambiguity).
- **Gate:** 140 EditMode + 138 PlayMode green; every pre-existing test unchanged (NFR6, behavior-preserving).

### File List

- **Added:** `Assets/Scripts/Domain/EffectDescriptor.cs`, `Assets/Scripts/Characters/Powers/PowerEffectTrace.cs`, `Assets/Scripts/Tests/Editor/EffectDescriptorTests.cs`, `Assets/Scripts/Tests/PlayMode/PowerGoldenTraceTests.cs`
- **Modified:** `Assets/Scripts/Characters/Powers/Power.cs`, `POmniscience.cs`, `PCursedVision.cs`, `PCorruptingMark.cs`, `PBoundByInk.cs` (observation-seam `Record` calls only)

## Change Log

- 2026-06-11 — Story 4.0 drafted; inventory captured; night-mode fork (real effect surface ≫ epic sketch) resolved in autonomy to Shape B (full game-effect union, single dispatch-seam family) + sub-fork to the observation-only seam (option ii); alternatives + power mapping recorded above.
- 2026-06-11 — Implemented T1–T5; 140 EditMode + 138 PlayMode green; status → done.
