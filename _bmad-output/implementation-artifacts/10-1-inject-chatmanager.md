# Story 10.1: Inject ChatManager (fan-in 17)

Status: review

## Story

As a developer,
I want `ChatManager` consumers to receive it injected (behind `IChatService` where a narrow slice helps),
so that the highest-fan-in remaining singleton stops being a global.

## Acceptance Criteria

1. **Recipe §7 applied to ChatManager** (replicated NetworkBehaviour singleton, 216 LOC, fan-in 17): usage census → optional `IChatService` slice (extract ONLY if the census shows a clean read/send split; otherwise inject concrete — record the call) → root accessor → consumers migrated per lane → static narrowed/annotated.
2. **Chat behaviour unchanged on the fixture:** messages, channels (`ChatWindowIDs`), server messages, and the bot routing (`ChatManager` RPCs carry `GetSafeRpcTarget` territory — bodies untouched).
3. **Known consumers** (recon: PTruthChains' chat line, SendMessagePanel 5 hits, ChatWindow, powers' chat messages — census at dev time) migrated in batches; registry/guards green.
4. **Gated:** suite + fixture + goldens unchanged per batch; boot smoke includes sending a chat message host→bot context.

## Tasks / Subtasks

- [x] **Task 1:** Census + slice decision (Dev Agent Record).
- [x] **Task 2:** Root accessor (concrete, no interface — slice decision); `Power`/`PowerComponent.chatManager` base field resolved lane C in `OnNetworkSpawn`.
- [x] **Task 3:** Batch migration: 11 chatting powers + `PCReparentOnChain` rerouted onto the `chatManager` base field; UI leaves + static dispatcher recorded (deferred).
- [x] **Task 4:** Static narrowing (`instance` → recorded-callers façade) + guard #1 lock + §4b census + gates (EM 162 / PM 148 incl. host→bot smoke); sprint-status.

## Dev Notes

- Epic 10 cadence: one proven recipe (Epics 7-9), smaller targets. The per-story judgment is the SLICE decision (interface or concrete) — D-NFR6: interface where logic/tests benefit, concrete for presentation leaves.
- `ChatManager.SendChatMessageServerRpc` is called from server AND client contexts — verify the migrated access path resolves identically in both (fixture case).
- Staleness: census-driven; fan-in 17 is the 2026-06-11 figure.

### Project Structure Notes

- Modified: `ChatManager.cs` (static narrowing), `CompositionRoot.cs`, consumer files, registry; optional new `IChatService.cs`. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- `Network/`-surface rule: ChatManager keeps its NGO plumbing; consumers see the service surface only. GetSafeRpcTarget verbatim.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7 recipe, §8 Epic 10] / [epics.md#Story 10.1]
- [Source: Assets/Scripts/ChatSystem/ChatManager.cs].

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors (forced rebuild after the 16-file edit). EditMode **162/162** unchanged; PlayMode **147→148** (+1 = the new host→bot chat smoke). All power harnesses that exercise chat (PowerGoldenTrace / Entrapment / Corruption / Vision) PASS with **zero harness changes** — the root→`instance` resolution is behaviour-identical, confirming early-binding `chatManager` at `OnNetworkSpawn` resolves the same global the powers read late before.

### Completion Notes List

**Task 1 — census + slice decision.**

Census of `ChatManager.instance` (production), partitioned:

- **Powers (lane C via `Power` base field) — MIGRATE NOW:** PClandestineObservation, PDroolyHealing, PCardsShuffling, PBoundByInk, PHighPriorityBounty, PBlessing, PEyeOfTheVoid, PLackOfAffection, PPersonalBeacons, PTruthChains, PVisionOfTheImpossible (11 `Power` subclasses). All use only the fire-and-forget SEND/notify surface (`ReceiveChatMessageRpc`/`DiscoverChatRpc`/`UndiscoverChatRpc`/`SendChatMessageServerRpc`/`AddMessageLocal`).
- **PowerComponent (lane C via `PowerComponent` base field) — MIGRATE NOW:** PCReparentOnChain (`ReceiveChatMessageRpc`; keeps the `ChatManager.SERVER_CLIENT_ID` *const* — that is not a singleton read).
- **Static POCO dispatcher — RECORD (verify-don't-force):** PowerEffectDispatcher (`DiscoverChatRpc` + `AddMessageLocal`). Same bucket as its already-recorded sibling `CharacterManager.instance` (9.3 §4a); a `static class` with no injection context. Full POCO cleanup = Epic 11.1. Left on the singleton, recorded.
- **UI leaves — DEFER to Epic 12:** ChatPanel, ChatNotificationComponent, ChatWindow (read surface: events / `activeChatId` / `discoveredChatIds` / `GetChatWindow` + a couple of commands). Presentation, no injection context yet. Recorded.

**Slice decision: inject CONCRETE `ChatManager`, NO `IChatService` (D-NFR6, recorded).** The migrated-now consumers (powers/components) use only the send/notify surface as a presentation side-effect of power logic that is ALREADY POCO (Epic 4 EffectDescriptor/PowerResolver). No decision logic and no unit test would benefit from a mock chat. The read/command split exists but lives entirely in the deferred UI leaves. Mirrors 7.1 (CharacterManager injected concrete into `Power` first; slices came in Epic 9 only because mixed gameplay consumers existed — chat has none outside UI). Extract a slice later if a chat decision-logic test ever needs one.

**Resolution mechanism (recorded): `CompositionRoot` accessor forwards to `ChatManager.instance`.** ChatManager STAYS a replicated singleton (story scope — NOT de-singletonised; no `ChatManager.For(nm)` per-NM registry). So `CompositionRoot.For(nm).ChatManager` / the root's `ChatManager` accessor both return `ChatSystem.ChatManager.instance` — the root is the ONE sanctioned locator (already calls `CharacterManager.For(`/`GameManager.For(`), so a singleton-backed chat resolver belongs there too. The fan-in win is real: 13 files stop reading the global; the read collapses into the root, resolved once per consumer in `OnNetworkSpawn` and stored in a base field. No `[SerializeField]` / scene-wiring / `InjectedManagerTypes` / guard-#2 entry (nothing new to wire); behaviour-identical everywhere (production + every harness keep the same single global object the powers used before). Full kill of `ChatManager.instance` waits on a future per-NM-registry story or 12.3.

**Task 2 — root accessor + base fields (lane C).** `CompositionRoot` gains a `ChatManager` accessor on both the instance and the `Services` value-resolver, each forwarding to `ChatSystem.ChatManager.instance` (concrete, no `[SerializeField]`/scene-wiring/`InjectedManagerTypes` — chat is not de-singletonised). `Power` and `PowerComponent` each gain a `protected ChatManager chatManager;` resolved ONCE in `OnNetworkSpawn` via `CompositionRoot.For(NetworkManager).ChatManager`, right after the existing `characterManager`/`gameInfoRevealer` resolves. Null-tolerant (no `Assert`, mirroring `gameInfoRevealer`) — not every power chats and minimal harnesses spawn bare powers.

**Task 3 — batch migration (AC3).** 11 `Power` subclasses (PClandestineObservation, PDroolyHealing, PCardsShuffling, PBoundByInk, PHighPriorityBounty, PBlessing, PEyeOfTheVoid, PLackOfAffection, PPersonalBeacons, PTruthChains, PVisionOfTheImpossible) + `PCReparentOnChain` rerouted `ChatManager.instance.` → `chatManager.` (the `SERVER_CLIENT_ID` const left as-is — compile-time constant, not a singleton read). UI leaves + the static dispatcher are recorded leftovers (verify-don't-force), NOT migrated. No registry/guard-#2 work (powers were already Epic-7-registered; no new `[SerializeField]`).

**Task 4 — static narrowing + lock + gate (AC1, AC2, AC4).** `ChatManager.instance` annotated as the recorded-callers-only façade (`// recorded: dies in 12.3`). Guard #1 (`DiSeamNoLocatorGuardTests.ForbiddenLocators`) gained `"ChatManager.instance"` so the migrated registered powers (PTruthChains/PClandestineObservation/PEyeOfTheVoid/PVisionOfTheImpossible/PBlessing/PCardsShuffling/PHighPriorityBounty/PLackOfAffection + PCReparentOnChain) can never re-grab the global — all verified clean (EM 162 incl. the guard). §4b census table (caller → member → reason → death) added to the architecture doc. AC4 host→bot chat smoke (`ChatManager_ResolvesThroughRoot_AndSendsHostToBot`) added to `ChatManagerTests`: resolves the root chat accessor (`AreSame` the spawned singleton) then `SendChatMessageServerRpc` with a bot sender (clientId 100 ≥ 100 → host-simulated) reaches the server body, routes the sent-notification via `GetSafeRpcTarget(bot)` (NFR5 verbatim), and delivers. EM 162 / PM 148, guards green; bodies + Awake duplicate-guard untouched.

### File List

**Modified (production):**
- `Assets/Scripts/Characters/Powers/Power.cs` — `using ChatSystem;`, `protected ChatManager chatManager;` base field, resolved lane C in `OnNetworkSpawn`.
- `Assets/Scripts/Characters/Powers/PowerComponents/PowerComponent.cs` — same (base field + lane C resolve).
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `using ChatSystem;`, `ChatManager` accessor on the instance + `Services` struct (forward to the singleton).
- `Assets/Scripts/Characters/Powers/{PClandestineObservation,PDroolyHealing,PCardsShuffling,PBoundByInk,PHighPriorityBounty,PBlessing,PEyeOfTheVoid,PLackOfAffection,PPersonalBeacons,PTruthChains,PVisionOfTheImpossible}.cs` — `ChatManager.instance` → `chatManager`.
- `Assets/Scripts/Characters/Powers/PowerComponents/PCReparentOnChain.cs` — `ChatManager.instance` → `chatManager`.
- `Assets/Scripts/ChatSystem/ChatManager.cs` — `instance` recorded-callers-façade annotation (`// recorded: dies in 12.3`). No code/visibility change.

**Modified (tests):**
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"ChatManager.instance"` added to `ForbiddenLocators` (regression lock).
- `Assets/Scripts/Tests/PlayMode/ChatManagerTests.cs` — `ChatManager_ResolvesThroughRoot_AndSendsHostToBot` host→bot smoke (AC4).

**Docs:**
- `_bmad-output/refactor-architecture-despaghetti.md` — §4b ChatManager static census; §4a ChatManager row re-pointed (opposite axis, → 12.3).
- `_bmad-output/implementation-artifacts/deferred-work.md` — story-10.1 deferrals (dispatcher + UI leaves façade; not-de-singletonised design call; ChatManager's own CharacterManager reads).
- `_bmad-output/implementation-artifacts/10-1-inject-chatmanager.md` (this story); `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Injected ChatManager (fan-in 17) into 11 powers + PCReparentOnChain via the `Power`/`PowerComponent.chatManager` base field (lane C, resolved through `CompositionRoot.For(nm).ChatManager`). Concrete (no `IChatService` — D-NFR6); chat stays a singleton (root = the one indirection point). `instance` → recorded-callers façade; guard #1 locks `ChatManager.instance`; §4b census. UI leaves + static dispatcher recorded (Epic 11.1/12). EM 162/162 + PM 148/148 (+1 host→bot chat smoke). Status → review. |
