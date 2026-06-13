# Story 1.4: Modular per-observer information system (rewrite `GameInfoRevealer`)

Status: ready-for-dev

> **⚠️ THIS STORY IS A BACKBONE REWRITE.** `GameInfoRevealer` + `CharacterInfoReveal` are consumed by ~17 call sites across powers, game states, UI and target filtering. The hard requirement is **behaviour-preserving**: current gameplay (who learns what, when) must be *bit-for-bit identical* after the rewrite. The win is internal: a modular, lie-capable, extensible info model — not a gameplay change. Story 1.3 (`character-info-badge-overlay-system.md`) is **blocked on this** and expects this rewrite to expose **heal reveal info**.

## Story

As a developer (Poyo),
I want the information system (who knows what about each character — corrupted, role, healed, …) to be a **modular per-observer registry** where each info is a `(key → entry)` pair carrying an optional caller-supplied value,
so that I can add a new piece of info (e.g. "was healed") without touching the corruption logic, and so that a power can reveal a **true OR false** value at will — all without changing any existing gameplay behaviour.

## Context

`GameLogic/GameInfoRevealer.cs` is the single source of "what does observer X know about character Y". Today it is rigid in three ways the redesign must fix:

1. **Fixed hardcoded fields.** `CharacterInfoReveal` has exactly three fields: `isRoleRevealed`, `isCorruptRevealed`, `forceCorruptOnRoleRevealed`. Adding "healed" = add a 4th field + wire every consumer. Not modular.
2. **Reflection-by-string write path.** `SetRevealLevel` takes a `FixedString64Bytes _revealVariableName` and does `typeof(CharacterInfoReveal).GetField(name)`. Stringly-typed, no compile safety, slow, brittle.
3. **Truth-only.** A reveal is a *visibility tier* (`RevealLevel { False, Personal, Public }`) over a value that is always read live from the `Character` (`isCorrupted.Value`, `role`). There is **no way to show a value that differs from reality** — `forceCorruptOnRoleRevealed` is a bolt-on hack that exists only because the system can't carry an explicit value.

### Design resolved with Poyo (decisions locked — see "Resolved Decisions")

- **Registry, not fixed fields.** Entry keyed by an `InfoKey` enum. Adding an info = new enum value, zero new field, zero reflection.
- **Two reveal modes — this is the core insight:**
  - **Live / standing** (observer is *always* up to date): the entry grants permission to read the **live truth from the source**. No stored value. This is exactly today's corruption behaviour (`PCorruptionKnowledge` gives a standing permission; if a player is corrupted *later*, the observer sees it). **Behaviour preservation depends on keeping this live.**
  - **Snapshot** (observer learns the value *at a precise moment*): the entry stores an explicit caller-supplied `InfoValue`. The value can be the **truth** or a **lie** — lying is simply "pass a value ≠ reality". Also how event-info like `WasHealed = true` works.
  - Implementation: entry holds an **optional** value. `null` ⇒ live (read source). non-`null` ⇒ snapshot/explicit.
- **Scope replaces the `RevealLevel` tier for routing**, not for value. `InfoScope { Personal, Public }`: Personal = revealed to one observer (targeted RPC); Public = revealed to everyone + merged into every simulated bot brain (broadcast). Presence of an entry = "is it revealed".
- **Generic value typed by key**, serialised for NGO without `object` on the wire (see Dev Notes → networking).
- **Observer/notification system** replaces the single global `Action onCharacterInfoRevealedChanged` with something cleaner (per-observer / per-key change notification) — while keeping a compatible signal for existing UI subscribers.
- **`IGameInfoRevealer` interface** introduced so consumers (powers, UI, target filtering) depend on an interface, not the concrete `NetworkBehaviour` (project-context line 351: mock NGO via interface; line 270: Powers reference GameLogic *interfaces*).

## Acceptance Criteria

1. **Behaviour parity (the prime directive).** After the rewrite, every existing reveal flow produces an **identical observable result** — same observer learns the same thing at the same trigger. Specifically preserved:
   - Corruption stays **live**: an observer with corruption knowledge who sees a player corrupted *after* the reveal still sees it (today: `isCorrupted.Value && forceCorrupt > False`, read live every `UpdateCharacter()`).
   - Role reveals (`Personal` and `Public`) reach the same observers as today.
   - Simulated bots (`observerId >= 100`) keep their separate per-brain knowledge; Public reveals still merge into all bot brains.
   - "No downgrade" semantics: a `Public` reveal is not lost if a later `Personal` write happens. **Rule: Public supersedes Personal; within the same scope, last-write-wins.**
2. **Modular registry.** Adding a new info type requires **only** a new `InfoKey` enum value (+ its value type mapping) — **no** new field on a struct, **no** edit to corruption code, **no** reflection. Demonstrated by AC 6.
3. **No reflection / no stringly-typed field names.** The `FixedString64Bytes _revealVariableName` + `GetField` path is **deleted**. Writes go through a typed API: `SetInfo(clientId, InfoKey.X, value, scope, observer)` style (final signatures dev's call, but compile-checked, no `typeof().GetField`).
4. **True OR false values.** A reveal can carry an explicit value that differs from the character's real state (a lie). With `overrideValue == null` the system reads live truth (parity path). With a value set, that value is what the observer sees. Covered by a unit test that reveals a *false* corruption and asserts the observer reads the lie while reality is unchanged.
5. **Live vs snapshot modes both work.** Standing/live reveal (no stored value, tracks source) and snapshot reveal (frozen explicit value) are both expressible and tested. Corruption knowledge is migrated as **live**; a snapshot reveal is tested independently.
6. **Heal info exposed (unblocks Story 1.3).** The new API exposes a heal info (`InfoKey.WasHealed` or equivalent) that Story 1.3's badge resolver can consume — gated like corruption (character `isHealed` true AND observer has the heal info). `Character.isHealed` state already exists; this story adds only the **reveal**, not new authoritative state. This proves AC 2 end-to-end (heal added without touching corruption logic).
7. **`forceCorruptOnRoleRevealed` removed.** The hack field is gone; its behaviour is reproduced by a live `Corrupt` reveal. `CharactersBarObject` and any other reader migrate to the new API with identical visible result; no double-source for corruption.
8. **Interface seam.** Consumers depend on `IGameInfoRevealer` (resolved via `GameManager.instance.gameInfoRevealer` typed as the interface, or DI). The concrete `NetworkBehaviour` stays in `GameLogic/` (designated NGO surface, project-context line 268).
9. **Networking correctness.** No `object` on the wire. Payload uses the project's `NetworkSerializableObject` pattern (project-context line 95) or an `INetworkSerializable` `InfoValue`. Every RPC target still wrapped with `GetSafeRpcTarget`; identity checks use `IsLocalOrSimulated`, never raw `IsLocalClient` (project-context lines 460–461). Personal = targeted, Public = broadcast + bot-brain merge, preserved.
10. **Observer/notification.** UI consumers (`InfoTableSystem`, `CorruptedCardText`, `CharactersBarObject`) still refresh when the local observer's knowledge changes. The replacement notification fires for the **local observer** on relevant change; existing subscribers are migrated (no dangling subscriptions, symmetric sub/unsub preserved).
11. **No regression in tests.** All existing tests pass — `VisionPowerTests` (asserts `RevealLevel.Personal`/`False` on `isCorruptRevealed`/`isRoleRevealed`) is migrated to the new API while asserting the **same logical outcome**. EditMode suite green (≥ current count). New logic (registry, live-vs-snapshot, lie, scope precedence, bot-brain merge) has unit coverage.

## Tasks / Subtasks

- [ ] **Task 1 — Design the info model (AC: 2, 3, 4, 5)**
  - [ ] Define `InfoKey` enum (`Role`, `Corrupt`, `WasHealed`, … — start with the keys needed to cover existing fields + heal). Map each key to its value type.
  - [ ] Define `InfoValue` carrying a typed primitive value (bool / int / ulong covers role-id, corrupt-bool, healed-bool) — must be NGO-serialisable (see Task 4). Provide typed get/set helpers; no `object` on the wire.
  - [ ] Define `InfoEntry { InfoScope scope; InfoValue? overrideValue; }`. `overrideValue == null` ⇒ live (read source); non-null ⇒ explicit value (truth or lie). Define `InfoScope { Personal, Public }`.
  - [ ] Define precedence: `Public` supersedes `Personal`; same-scope = last-write-wins (allows re-lie / correction). Document why (replaces the old monotone `RevealLevel` no-downgrade guard at `GameInfoRevealer.cs:120`).
- [ ] **Task 2 — `IGameInfoRevealer` interface + reader API (AC: 1, 8, 10)**
  - [ ] Extract an interface with the read/write/notify surface consumers need: `GetInfo(clientId, key, observerId)` (or a `bool TryGetInfo` + typed value), `IsRevealed(clientId, key, observerId)`, the live-truth fallback resolution, and the change-notification subscription.
  - [ ] Reader resolves live-vs-snapshot internally: if entry exists and `overrideValue != null` → return stored value; if entry exists and `overrideValue == null` → read live source (corruption from `Character.isCorrupted.Value`, role from `Character.role`, heal from `Character.isHealed.Value`); if no entry → not revealed.
  - [ ] Preserve the observer split: `observerId >= 100` reads the simulated bot brain; real observers read their own store. Reuse/replace the existing `simulationsKnowledge` machinery — keep it behind the interface (consumers never touch raw dictionaries; project-context: mock NGO via interface).
- [ ] **Task 3 — Write path + reveal modes (AC: 1, 3, 4, 5, 9)**
  - [ ] Typed write API replacing `SetRevealLevel` reflection. Two intents: **reveal-live** (grant standing permission, no value) and **reveal-value** (snapshot, explicit value — truth or lie).
  - [ ] Server-authoritative reveal RPCs replacing `SendRevealLevelRpc` / `SetRevealLevelRpc` / `SetRevealLevelSimulatedRpc`: Personal → targeted single observer (host-redirect for bot ids ≥100, exactly as today's `SendRevealLevelRpc` does); Public → broadcast + merge into observer 0 and **all** simulated brains (today's `RevealLevel.Public` branch). Keep the host-intercept-for-bots routing.
  - [ ] Every RPC target wrapped with `GetSafeRpcTarget`; identity via `IsLocalOrSimulated`. No `IsLocalClient`.
- [ ] **Task 4 — NGO serialisation (AC: 9)**
  - [ ] `InfoValue` (and the reveal payload) serialised via the project pattern: inherit `NetworkSerializableObject` (`Assets/Scripts/Network/NetworkSerializableObject.cs`) **or** implement `INetworkSerializable` as a tagged union (type tag + the primitive). **No `object` / no managed boxing on the wire.** Confirm round-trip in a test.
- [ ] **Task 5 — Migrate all consumers behaviour-preserving (AC: 1, 7, 10)** — for each, swap to the new API, assert identical result, no double-source:
  - [ ] **Reveal writers (powers):** `PEmbraceOfShadows`, `PCursedVision`, `PCorruptionParanoia`, `PCorruptionInsight`, `PCorruptionKnowledge` (→ live `Corrupt` reveal, drop `forceCorrupt`), `PPersonalBeacons` (→ live `Corrupt`), `POmniscience`, `PBlessing`, `PCorruptingMark`, `PLackOfAffection`, `PChainedByTheShadows`, `PHighPriorityBounty`, `PCardsShuffling`, `PDroolyHealing`.
  - [ ] **Game states / managers:** `TakeDownThePortalState` (`SetRevealLevelRpc … Public, Everyone` role reveal; read `isRoleRevealed >= Public`), `GameEndingState` (Public role reveal, `showInfo=false`), `ChainingManager` (Public role reveal, `showInfo=false`).
  - [ ] **Readers:** `TargetUtils` (`isRoleRevealed > False`, `isCorruptRevealed > False && isCorrupted.Value` — keep the live corrupt read), `Card.cs` (`isRoleRevealed > 0`), `InfoTableSystem` (`isRoleRevealed > 0`), `CorruptedCardText` (`isCorruptRevealed > False`), `CharactersBarObject` (`forceCorruptOnRoleRevealed > False && isCorrupted.Value` → live `Corrupt` reveal read).
  - [ ] Migrate the notification: replace `onCharacterInfoRevealedChanged` usage in `InfoTableSystem`, `CorruptedCardText`, `CharactersBarObject` with the new observer signal; preserve symmetric subscribe/unsubscribe.
- [ ] **Task 6 — Expose heal info (AC: 6, unblocks Story 1.3)**
  - [ ] Add `InfoKey.WasHealed` mapped to `Character.isHealed`, revealed as **live** (overrideValue null → reader reads `Character.isHealed.Value`), mirroring corruption for consistency with the rest of the system (Poyo's choice). A heal never expires, so live and snapshot are visually equivalent today, but live keeps heal symmetric with every other standing reveal. Wire the server-side reveal as a live/standing permission at the existing heal trigger (`HealPlayerServerRpc` path / wherever heal becomes known to an observer per design).
  - [ ] Do **not** add new authoritative heal state — `Character.isHealed` already exists.
- [ ] **Task 7 — Tests (AC: 4, 5, 11)**
  - [ ] Migrate `VisionPowerTests` to the new API, asserting the same logical outcomes (CorruptionInsight reveals corrupt to owner; Omniscience reveals role to owner; non-revealed = not known).
  - [ ] New EditMode/PlayMode cases: live reveal tracks a source change; snapshot reveal freezes; **lie** (reveal Corrupt=false on a corrupted player → observer reads false, reality unchanged); scope precedence (Public over Personal, same-scope last-write-wins); bot-brain merge on Public; `InfoValue` NGO round-trip.
  - [ ] `mcp__UnityMCP__read_console` clean after each change; `mcp__UnityMCP__run_tests` green.

## Dev Notes

### Current state of files being modified (read before editing)

- **`Assets/Scripts/GameLogic/GameInfoRevealer.cs`** — the system. Today:
  - `Dictionary<ulong, CharacterInfoReveal> charactersInfoRevealed` = local observer's knowledge (this machine). `Dictionary<ulong, Dictionary<ulong, CharacterInfoReveal>> simulationsKnowledge` = per-bot brains (observerId ≥ 100, host-held).
  - `CharacterInfoReveal { RevealLevel isRoleRevealed, isCorruptRevealed, forceCorruptOnRoleRevealed }`; `RevealLevel { False=0, Personal=10, Public=20 }`.
  - `GetCharacterInfo(clientId, observerId=MAX)` → resolves observer (local id if MAX), routes ≥100 to bot brain, lazily creates entries. **The value is never stored — readers read `Character` live and use the level only as a gate.**
  - `SetRevealLevel(clientId, FixedString64Bytes name, level, observerId, showInfo)` — **reflection** `typeof(CharacterInfoReveal).GetField(name.ToString())`; **monotone guard** at line 120 (`if current >= new return`); on local-observer change pokes `BoardManager … ShowPseudoWithRevealedInfo` and fires `onCharacterInfoRevealedChanged`.
  - Reveal routing: `SendRevealLevelRpc` (server-only) → Personal targets `Single(observerId)`, bot ids ≥100 redirect to host(0) via `SetRevealLevelSimulatedRpc`. `SetRevealLevelRpc` / `SetRevealLevelSimulatedRpc` are `[Rpc(SendTo.Everyone, AllowTargetOverride=true)]`; `Public` branch applies to observer 0 **and loops all `simulationsKnowledge` brains**. Public reveals are often called directly on the Everyone-RPC (`TakeDownThePortalState`), Personal via `SendRevealLevelRpc`.
  - `GetSimulatedBrain` / `AddCharacterToSimulatedInfoList` prefill brains with self-role = Personal.
  - **Preserve all of: observer routing, bot-brain merge on Public, host-redirect for ≥100, the self-knows-own-role seeding, the local-observer change signal + pseudo reveal poke.**
- **`Assets/Scripts/Characters/Powers/`** (writers): one-liners calling `SendRevealLevelRpc` / `SetRevealLevel(Rpc)` with `nameof(CharacterInfoReveal.<field>)`. Notably:
  - `PCorruptionKnowledge.OnGameStartedServer` → sets `forceCorruptOnRoleRevealed = Personal` for **every** character to its owner = the standing "see everyone's live corruption" permission. **Migrate to a live `Corrupt` reveal (overrideValue null).**
  - `PPersonalBeacons` → same `forceCorruptOnRoleRevealed` standing reveal.
  - `PEmbraceOfShadows`, `PCursedVision`, `PCorruptionParanoia`, `PCorruptionInsight` → corrupt reveals (Personal). `POmniscience`, `PBlessing`, `PCorruptingMark`, `PLackOfAffection`, `PChainedByTheShadows`, `PHighPriorityBounty`, `PCardsShuffling`, `PDroolyHealing` → role reveals.
- **Readers** — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs` (target filtering by revealed role/corrupt — `isCorruptRevealed > False && isCorrupted.Value`, **live**), `Board/Card.cs` (`isRoleRevealed > 0`), `UI/InfoTable/InfoTableSystem.cs` (`isRoleRevealed > 0` → lock role), `UI/Misc/CorruptedCardText.cs` (`isCorruptRevealed > False`), `Board/UI/CharacterBar/CharactersBarObject.cs:178` (`forceCorruptOnRoleRevealed > False && isCorrupted.Value`, **live**, in `async UniTaskVoid UpdateCharacter`).
- **Game states** — `TakeDownThePortalState.cs:96` (Public role reveal on correct mage guess; reads `isRoleRevealed >= Public` at :214), `GameEndingState.cs:77` (Public role reveal, `showInfo=false`), `ChainingManager.cs:59` (Public role reveal on chain, `showInfo=false`).
- **`Assets/Scripts/Characters/Character.cs`** — `isCorrupted`, `isHealed`, `role` are the **live truth sources** the live-mode reader must read. `isHealed` already exists (set server-side); only the *reveal* is new.
- **Tests** — `Assets/Scripts/Tests/PlayMode/VisionPowerTests.cs` asserts `RevealLevel.Personal`/`False` on `GetCharacterInfo(...).isCorruptRevealed` / `isRoleRevealed`. Must be migrated to the new API with equivalent assertions.

### Networking — the load-bearing constraint

NGO RPCs cannot send `object`. The old string-fieldname existed to keep payloads blittable. Replacement: a typed payload — either inherit `NetworkSerializableObject` (`Assets/Scripts/Network/NetworkSerializableObject.cs`, the project's "complex RPC payload" base, project-context line 95) or implement `INetworkSerializable` on `InfoValue` as a **tagged union** (a `byte`/enum type tag + the primitive `ulong`/`int`/`bool`). API stays typed (`SetInfo(InfoKey.WasHealed, true)`); the wire carries the union. **No reflection, no boxing on the wire.** Round-trip must be tested.

`GameInfoRevealer` is a `NetworkBehaviour` in `GameLogic/` — a designated NGO surface (project-context line 268 allows NGO in `Network/` **and designated managers**); keep it there, do not scatter `[Rpc]` into powers/UI. Wrap every target with `GetSafeRpcTarget`; bot ids ≥100 host-redirect exactly as today. Use `IsLocalOrSimulated`, never `IsLocalClient`.

### Live vs snapshot — why this exact shape

Poyo's requirement: some info you must "toujours être au courant" (live — corruption: see it even if it happens later), some you learn "à un moment précis" (snapshot — an event, or a lie frozen in time). Modelled as `InfoEntry.overrideValue` nullable: `null` = live permission → reader reads `Character` truth every read (= today's behaviour, the reason corruption stays correct after a later change); non-null = the value the observer was told, which may differ from reality (a lie) or capture an event (`WasHealed=true`). This is the single mechanism that satisfies *both* "behaviour must not change" (everything migrates as live → identical) *and* "I want to add false/event info later".

### Existing reusable patterns to follow (don't reinvent)

- `Assets/Scripts/Network/NetworkSerializableObject.cs` — base for complex RPC payloads.
- `GetSafeRpcTarget` / `IsLocalOrSimulated` — the bot-routing/identity helpers (CLAUDE.md critical patterns); the old code's `Single(observerId)` + host-redirect-for-≥100 is the pattern to keep.
- The existing observer split (`charactersInfoRevealed` vs `simulationsKnowledge[observerId]`) — keep the *concept*, hide it behind `IGameInfoRevealer`.

### Testing standards

- NGO-touching logic tested in PlayMode via `NetworkTestHelper` (project-context line 335); pure registry/precedence/lie logic in EditMode without scene deps. New RPC flow ⇒ PlayMode multi-client (line 323). RPCs in tests still wrap with `GetSafeRpcTarget` (line 360). Mock the NGO surface via `IGameInfoRevealer`, not a raw `NetworkBehaviour` subclass (line 351).
- Migrate `VisionPowerTests`; add the new cases in Task 7. Run `mcp__UnityMCP__run_tests`, confirm green.

### Project Structure Notes

- Keep `GameInfoRevealer` (concrete `NetworkBehaviour`) + RPCs in `Assets/Scripts/GameLogic/`. `IGameInfoRevealer`, `InfoKey`, `InfoValue`, `InfoEntry`, `InfoScope` live in `GameLogic/` (or a `GameLogic/Info/` subfolder) within `Game.asmdef`. Powers reference the **interface** (project-context line 270: `Powers/` → `GameLogic/` interfaces only).
- No magic strings — `InfoKey` enum replaces the `nameof(field)` strings (project-context line 251/507).

### Project Context Rules (must follow)

- **`GetSafeRpcTarget(clientId)` on every RPC target**; **`IsLocalOrSimulated` not `IsLocalClient`** (lines 460–461). Bot ids ≥100 host-intercept.
- **Server-authoritative**: reveals mutate knowledge server-side then RPC out; clients never self-grant info (line 462).
- **Complex RPC payload → `NetworkSerializableObject`** (line 95). No `object` on the wire.
- **Async = UniTask** only; `CharactersBarObject.UpdateCharacter` is `async UniTaskVoid` — keep it.
- **`[FormerlySerializedAs]`** if any serialized field is renamed (line 94/503) — relevant if `CharacterInfoReveal`-typed serialized refs exist in scene/prefab; check `GameScene`. If `CharacterInfoReveal` is `[Serializable]` and serialized anywhere, removing fields wipes data silently — verify before deleting fields.
- **`OnNetworkDespawn` before `OnDestroy`** for `NetworkVariable.OnValueChanged` unsubscription (line 516) — relevant if live-mode readers subscribe to source `NetworkVariable`s.
- **NetworkVariable mutated by event, not per frame** (line 76/191) — reveals are event-driven; do not poll.
- After every change: `mcp__UnityMCP__read_console` for compile errors before assuming it works.

### References

- [Source: Assets/Scripts/GameLogic/GameInfoRevealer.cs] — full current system: `CharacterInfoReveal`, `RevealLevel`, reflection write, observer/bot routing, Public bot-brain merge.
- [Source: Assets/Scripts/Characters/Powers/PCorruptionKnowledge.cs] — standing `forceCorrupt` reveal to migrate to live `Corrupt`.
- [Source: Assets/Scripts/Characters/Powers/Target/TargetUtils.cs] — live corrupt read in target filtering.
- [Source: Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs#UpdateCharacter] — `forceCorrupt && isCorrupted.Value` live corruption display.
- [Source: Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs] / [GameEndingState.cs] / [ChainingManager.cs] — Public role reveals.
- [Source: Assets/Scripts/Tests/PlayMode/VisionPowerTests.cs] — reveal assertions to migrate.
- [Source: Assets/Scripts/Network/NetworkSerializableObject.cs] — RPC payload base.
- [Source: _bmad-output/implementation-artifacts/character-info-badge-overlay-system.md] — Story 1.3, blocked on this; needs heal reveal.
- [Source: _bmad-output/project-context.md] — NGO/RPC/UniTask/asmdef/testing rules cited above.

## Resolved Decisions (from Poyo)

1. **Behaviour-preserving is non-negotiable.** Current gameplay (who knows what, when) stays identical; this is an internal cleanup of a backbone system. Refactor surrounding spaghetti freely where it helps.
2. **Registry + caller-supplied value.** Info = `InfoKey → entry`; the writer supplies the value (true or false). Reflection-by-fieldname is removed.
3. **Live vs snapshot via nullable `overrideValue`.** `null` = standing/live (read source truth — corruption stays live); set = snapshot/explicit (event or lie). Both required; an observer-style system underpins live updates.
4. **Scope = `Personal`/`Public`** replaces the `RevealLevel` tier for routing; presence = revealed. Precedence: **Public > Personal, same-scope last-write-wins.**
5. **Generic value typed by key**, NGO-serialised without `object` on the wire.
6. **`IGameInfoRevealer` interface** introduced for consumers; concrete NGO stays in `GameLogic/`.
7. **Heal info exposed** for Story 1.3 (`InfoKey.WasHealed` over existing `Character.isHealed`); no new authoritative state.
8. **Heal reveal mode = live.** Mirrors corruption for consistency across the system (Poyo). A heal never expires so it's visually equivalent to snapshot today, but live keeps heal symmetric with every other standing reveal. The **snapshot** path is still exercised independently by the lie/snapshot unit tests (AC 4/5), not by heal.

## Dependencies

- **Blocks:** Story 1.3 `character-info-badge-overlay-system.md` (needs heal reveal + stable new API).
- **Blocked by:** none. Foundational.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
