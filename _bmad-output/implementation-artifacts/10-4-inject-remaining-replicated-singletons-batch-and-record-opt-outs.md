# Story 10.4: Inject the remaining replicated singletons (batch) + record opt-outs

Status: review

## Story

As a developer,
I want StatesCanvas, MessageManager, GameAudioManager, ChainingManager, LobbyPlayerInfoHolder migrated or explicitly opted out,
so that the replicated-singleton census closes with every survivor recorded.

## Acceptance Criteria

1. **Each of the 5 gets a decision:** migrate per recipe §7 (census → root accessor → consumers per lane → static narrowed) OR recorded verify-don't-force opt-out with reason. No silent skips.
2. **`GameAudioManager` is the explicit opt-out candidate** (global audio façade; FMOD no-op-safe constraint; consumed from everywhere including non-injectable contexts). If opted out: recorded in the architecture doc §4 census with the reason; its static stays whitelisted; consumers untouched.
3. **`ChainingManager`** consumers were partially migrated in 7.4 (pass-through slice) — this story finishes its direct-static consumers (e.g. `PTruthChains:43` — note: PTruthChains is in the guard registry; migrating its ChainingManager read makes its registry entry FULLY clean).
4. **`LobbyPlayerInfoHolder`** is itself NGO-spawned — its consumers use lane C/B; its own 1 instance-hit rerouted.
5. **Census table updated** in architecture doc §4 (each of the 8 replicated singletons: migrated / opt-out + reason).
6. **Gated:** suite + fixture per batch; registry/guards green; sprint-status.

## Tasks / Subtasks

- [x] **Task 1:** Per-singleton census (5 targets); decisions table (Dev Agent Record).
- [x] **Task 2:** Migrate per lane — **all 5 resolved**: ChainingManager + StatesCanvas (prior pass), MessageManager + LobbyPlayerInfoHolder (this pass) migrated; GameAudioManager opt-out.
- [x] **Task 3:** GameAudioManager opt-out recorded (§4e, stays whitelisted, OUT of ForbiddenLocators); ChainingManager + StatesCanvas + MessageManager + LobbyPlayerInfoHolder locked in guard #1.
- [x] **Task 4:** Gates green for all targets (compile 0, DiSeamGuard 4/4, EM 162/162, PM 148/148); §4e/§4a census updated; sprint-status flipped to review.

## Dev Notes

- The danger in batch stories is rote application where judgment is needed: each of the 5 has different consumer shapes (StatesCanvas = UI host; MessageManager = send flows with GetSafeRpcTarget territory; LobbyPlayerInfoHolder = player-info reads from everywhere). Census FIRST, per target; one commit per target, not one mega-commit.
- FMOD rule if GameAudioManager IS migrated instead: every consumer keeps going through it (never AudioSource) — injection changes resolution, not the audio architecture.
- Staleness: census-driven; consumer sets reshaped by Epics 7-9.

### Project Structure Notes

- Modified: per-target manager files, `CompositionRoot.cs`, consumers, architecture doc §4, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- FMOD via GameAudioManager only (whether migrated or opted out). GetSafeRpcTarget verbatim in MessageManager flows. Persistent EventInstance release rules untouched.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §4 census, §7, §8 Epic 10] / [epics.md#Story 10.4]
- [Source: Assets/Scripts/Characters/Powers/PTruthChains.cs:43-46] — the ChainingManager/Chat/LobbyPlayerInfoHolder consumer cluster from 6.3's scope fence.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors. EM 162/162 + PM 148/148. One PM iteration: VoteTallyGoldenMasterTests (`...AndChained`) NRE'd because VoteState.OnEndStateServer now chains through the injected `GameState.chainingManager` (lane-B) rather than the `ChainingManager.instance` global, and the manual harness wired `characterManager` but not `chainingManager` → added `_voteState.chainingManager = _chainingManager` (the harness already spawns one).
- Final pass — one DiSeamGuard iteration: `MigratedConsumers_DoNotReferenceTheLocator` flagged `MessageManager` because the new façade *comment* contained the literal substring `MessageManager.instance` and MessageManager is in the `All` registry (source-scanned). The guard is a naive substring scan over the whole file — reworded the comment to phrase it as the bare `instance` self-ref. Re-ran: 4/4 green. (Same trap avoided pre-emptively for `LobbyPlayerInfoHolder.instance` in the holder's own annotation.)

### Completion Notes List

**Task 1 — per-singleton census + decisions (all 5).** See architecture doc §4e for the table. Decisions: GameAudioManager = **opt-out** (AC2 — global FMOD façade, ~19 non-injectable sites); ChainingManager + StatesCanvas = **migrate** (done this pass); MessageManager + LobbyPlayerInfoHolder = **migrate** (pending per-target passes — sprawling/multi-context consumers needing wire-vs-record verification per the never-defer-wiring rule).

**Done this pass (3 of 5 targets):**
- **GameAudioManager — OPT-OUT.** Recorded §4e; stays whitelisted (deliberately NOT in `ForbiddenLocators`); consumers untouched. No code.
- **ChainingManager — MIGRATED + locked.** Root accessor; `Power.chainingManager` base field (lane C, null-tolerant); 3 chaining powers (PTruthChains/PHighPriorityBounty/PChainedByTheShadows) rerouted; VoteState rerouted onto the inherited `GameState.chainingManager` (lane-B push from 7.4). Guard #1 locks `ChainingManager.instance`; annotated façade. Harness `VoteTallyGoldenMasterTests` wires `_voteState.chainingManager`.
- **StatesCanvas — MIGRATED + locked.** Root accessor; `GameState.statesCanvas` lane-B field pushed by `SetupGameStates`; sole consumer `GameState.OnStateCreated` rerouted (null-tolerant — only used when `stateUIPrefab != null`). Guard #1 locks `StatesCanvas.Instance`; annotated façade.

**Done this pass (final 2 of 5 targets — all 5 now landed):**
- **MessageManager — MIGRATED + locked.** Root accessor (`MessageManager` on instance + Services). `SendMessagePanel` rerouted onto a lane-C `messageManager` field resolved in `OnNetworkSpawn` — **asserted** (not null-tolerant): MessageManager is a GameScene NetworkBehaviour (Awake-set instance up before this spawns) and `SendMessageRpc` dereferences it on the local client, so null = a wiring bug. Guard #1 locks the qualified instance accessor; `instance` annotated façade. Recorded non-registered survivors keep the global → Epic 12.2: `AwakeningRecapMessages` (StateUI prefab leaf), `AnonymousRevealedMessagesComponent` (UI leaf). MessageManager itself is in the `All` registry (8.3) and uses the bare `instance` self-ref, so the lock does not bite it.
- **LobbyPlayerInfoHolder — MIGRATED + locked.** Root accessor. `Power.lobbyPlayerInfoHolder` base field (lane C, null-tolerant) → 4 player-name powers rerouted (PBlessing / PTruthChains / PHighPriorityBounty / PCardsShuffling). `Character` rerouted onto a lane-C field (null-tolerant — `GetOwnerPseudo` already guards null). **AC4 — the holder's own 1 instance-hit rerouted:** its `CharacterManager.instance.GetSafeRpcTarget` dropped onto a lane-C `characterManager` field resolved at the top of its own `OnNetworkSpawn` — **null-tolerant, NO Assert** (the only consuming path, `AskForPlayerInfo`, is server-only via `OnClientConnectedCallback` and is fired in-line during the same `OnNetworkSpawn`, so on the server the registry is populated exactly as the old `.instance` read required; on clients the field stays null and is never dereferenced → byte-identical to before). This clears the §4a CharacterManager-census row (planned death = 10.4). Holder registered (NoLocatorOnly, lane-C source-scan only — same shape as SendMessagePanel); guard #1 locks the qualified instance accessor; `instance` annotated façade. Recorded non-registered survivors keep the global: `PlayerButtonObject` / `ConnectedPlayerPanel` / `ChatPanel` (UI → 12.2), `UlongExtensions` (static ext → 10.5), `CharacterManager.AddDebugPlayer` (debug). NFR5: `GetSafeRpcTarget` verbatim on the concrete CharacterManager.

**Judgment (per the batch-story warning):** the two targets got *different* null-handling, each derived from its own consumer shape — SendMessagePanel asserts (GameScene NB, client-side use), LobbyPlayerInfoHolder's CharacterManager is null-tolerant (server-only use, client-legitimate-null). Rote application of one rule would have been wrong (an Assert in LobbyPlayerInfoHolder would false-fail on clients).

**Gate (final pass):** compile 0 errors; DiSeamGuard 4/4; EM 162/162; PM 148/148 — all baselines held. One guard iteration: the first MessageManager façade comment literally contained the forbidden substring `MessageManager.instance`, which the source-scan guard flagged on MessageManager.cs (in the `All` registry) — reworded the comment to use the bare-`instance` phrasing. Lesson recorded for any future lock of an already-registered manager: keep the qualified literal out of that manager's own source (comments included).

### File List

**Modified (production) — this pass:**
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `using UI;` + `ChainingManager` + `StatesCanvas` accessors (instance + `Services`).
- `Assets/Scripts/Characters/Powers/Power.cs` — `protected ChainingManager chainingManager` base field + lane-C resolve.
- `Assets/Scripts/Characters/Powers/{PTruthChains,PHighPriorityBounty,PChainedByTheShadows}.cs` — `ChainingManager.instance` → `chainingManager`.
- `Assets/Scripts/GameLogic/GameStates/VoteState.cs` — `ChainingManager.instance` → inherited `chainingManager`.
- `Assets/Scripts/GameLogic/GameState.cs` — `public StatesCanvas statesCanvas { get; set; }` lane-B field + `OnStateCreated` reroute.
- `Assets/Scripts/GameLogic/GameManager.cs` — `SetupGameStates` pushes `statesCanvas`.
- `Assets/Scripts/GameLogic/ChainingManager.cs` — `instance` recorded-façade annotation (`// dies 12.3`). No code change.
- `Assets/Scripts/UI/StatesCanvas.cs` — `Instance` recorded-façade annotation (`// dies 12.3`). No code change.

**Modified (tests):**
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"ChainingManager.instance"` + `"StatesCanvas.Instance"` → `ForbiddenLocators`.
- `Assets/Scripts/Tests/PlayMode/VoteTallyGoldenMasterTests.cs` — wire `_voteState.chainingManager`.

**Modified (production) — final pass (MessageManager + LobbyPlayerInfoHolder):**
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `MessageManager` + `LobbyPlayerInfoHolder` accessors (instance + `Services`).
- `Assets/Scripts/MessageSystem/SendMessagePanel.cs` — lane-C `messageManager` field (resolved + asserted in `OnNetworkSpawn`); `MessageManager.instance.SendMessageRpc` → `messageManager.SendMessageRpc`.
- `Assets/Scripts/MessageSystem/MessageManager.cs` — `instance` recorded-façade annotation (`// recorded: dies in 12.3`). No behaviour change.
- `Assets/Scripts/Characters/Powers/Power.cs` — `protected Network.LobbyPlayerInfoHolder lobbyPlayerInfoHolder` base field + lane-C resolve.
- `Assets/Scripts/Characters/Powers/{PBlessing,PTruthChains,PHighPriorityBounty,PCardsShuffling}.cs` — `LobbyPlayerInfoHolder.instance.GetPlayerInfo` → `lobbyPlayerInfoHolder.GetPlayerInfo`.
- `Assets/Scripts/Characters/Character.cs` — lane-C `lobbyPlayerInfoHolder` field + resolve; `GetOwnerPseudo` rerouted (null-tolerant).
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs` — `using GameLogic;`; lane-C `characterManager` field + resolve (null-tolerant); `CharacterManager.instance.GetSafeRpcTarget` → `characterManager.GetSafeRpcTarget`; `instance` recorded-façade annotation.

**Modified (tests) — final pass:**
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` — `typeof(Network.LobbyPlayerInfoHolder)` → `NoLocatorOnly`.
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"MessageManager.instance"` + `"LobbyPlayerInfoHolder.instance"` → `ForbiddenLocators`.

**Docs:** `_bmad-output/refactor-architecture-despaghetti.md` (§4e MessageManager + LobbyPlayerInfoHolder rows → MIGRATED; §4a LobbyPlayerInfoHolder row → RESOLVED); this story; `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Batch (3 of 5 targets done; story in-progress): GameAudioManager OPT-OUT (recorded §4e, stays whitelisted); ChainingManager migrated (Power.chainingManager base field + VoteState inherited lane-B) + locked; StatesCanvas migrated (GameState.statesCanvas lane-B push) + locked. Root accessors added; guard #1 locks both. EM 162/162 + PM 148/148 (VoteTally harness wire added). MessageManager + LobbyPlayerInfoHolder = remaining per-target passes. |
| 2026-06-12 | Final pass (targets 4 + 5 of 5 — story → review): MessageManager migrated (SendMessagePanel lane-C, asserted) + locked; LobbyPlayerInfoHolder migrated (Power.lobbyPlayerInfoHolder base field for 4 powers + Character lane-C; holder's own CharacterManager.instance.GetSafeRpcTarget rerouted lane-C null-tolerant, clearing §4a) + locked + registered. Root accessors added. §4e both rows → MIGRATED; §4a LobbyPlayerInfoHolder row → RESOLVED. Compile 0; DiSeamGuard 4/4; EM 162/162 + PM 148/148. All 5 replicated-singleton targets resolved → census closes. |
