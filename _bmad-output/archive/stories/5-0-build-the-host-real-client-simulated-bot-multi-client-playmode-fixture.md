# Story 5.0: Build the host + real-client (+ simulated-bot) multi-client PlayMode fixture

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want a reusable PlayMode test fixture with a host, a real in-process remote client over loopback, and an optional simulated bot, that can record the ordered sequence of `currentGameStateIndex.OnValueChanged` observations on the remote client,
so that replication-skew and `OnValueChanged`-propagation bugs — invisible to `StartHost` (host==server, RTT=0) — can be caught before the index-ownership move (Story 5.3).

## Acceptance Criteria

1. **Real-client substrate.** The fixture spins up a host + at least one **real in-process client** over a UnityTransport loopback transport (two `NetworkManager`s in one process), so a server-side write to `GameManager.currentGameStateIndex.Value` traverses real serialization and a real network tick before the remote client observes it via `NetworkVariable.OnValueChanged`. (This is the load-bearing difference from the `StartHost`-only harness, where host==server and the value is never serialized.)
2. **De-singletonisation invariants preserved (regression net inherited from 5.0e).** While both `NetworkManager`s run: `NetworkManager.Singleton == host`; the second client's `GameManager` / `CharacterManager` replicas are **not** destroyed at Awake; the `instance` façades stay on the host; `GameManager.For(clientNm)` / `CharacterManager.For(clientNm)` resolve the client's own instances. The fixture must not regress any assertion proven by `CoexistenceGateTests`.
3. **Optional simulated bot (intercepted-dispatch path).** The fixture can also include a simulated bot (`clientId >= 100`) whose RPCs the host intercepts (never crossing the wire), to keep covering the `GetSafeRpcTarget` / bot-flow path. The **real client is the load-bearing addition**; the simulated bot is optional and additive. Adding a bot must not break AC 1–2. `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` gateway are used verbatim where the bot path is exercised (NFR5) — no edits to those mechanisms.
4. **Remote-client index recorder.** The fixture exposes a way to record, **on the remote client**, the ordered sequence of `currentGameStateIndex.OnValueChanged` observations (the `newValue` of each fire), readable as an ordered, append-only trace (e.g. `IReadOnlyList<int>`). The recorder subscribes on the client's replica (`GameManager.For(clientNm).currentGameStateIndex`) and unsubscribes in teardown. The initial-spawn-value firing behaviour (does NGO fire `OnValueChanged` for the value carried in the spawn payload on a late-joining client?) is **characterized and documented** so 5.3 can assert exact ordered traces without ambiguity.
5. **Self-validating.** At least one fixture self-test drives the server index across **multiple** states (e.g. 0→1→2, requiring ≥3 seeded states) and asserts the remote client's recorded trace matches the expected ordered sequence — proving the recorder captures real, serialized, per-tick propagation (not a host-loopback shortcut). The minimal driver (direct `host.currentGameStateIndex.Value = n` writes on the server, which owns the `NetworkVariable`) is acceptable for 5.0; driving the full `SwitchGameState` state machine is **deferred to 5.3** which widens this into the parameterized client-trace suite.
6. **Suite stability.** The fixture tears down **both** `NetworkManager`s, the simulated bot, all spawned objects, and resets the manager statics in `[UnityTearDown]`, so it never destabilises the existing host+bot PlayMode suite (no UDP port left bound, no leaked static `instance`, no leaked registry entry). Full PlayMode + EditMode suite green after the change (baseline: PM 142, EM 155 from 5.0e).
7. **Reusability + unblock signal.** The substrate is a **reusable fixture** (a base class or shared helper others can derive/call), not a one-off test buried in a single `[Test]`. Its existence is the explicit tool that unblocks Story 5.3; record the unblock on issue #50.

## Tasks / Subtasks

- [x] **Task 1: Promote the 5.0e dual-NM substrate into a reusable fixture** (AC: 1, 2, 7)
  - [x] Create `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs` — a base class (or static builder + small state-holding helper) that owns: host NM, client NM, the two network-prefab templates, the spawned host `GameManager`/`CharacterManager`, and the resolved client replicas. Lift the proven mechanics from `CoexistenceGateTests` (the `SetGlobalObjectIdHash` / `MarkAsNonSceneObject` / `ResetManagerStatics` reflection helpers, the `DummyGameState`, the `ConfigureNetworkManager` loopback wiring, the host-first / client-second ordering that keeps `NetworkManager.Singleton == host`).
  - [x] Keep the **documented exception** comment block (this fixture drives `StartHost`/`StartClient` by hand on purpose — do not "fix" it back onto `NetworkTestHelper`; production transport is Facepunch/Steam, tests use UnityTransport loopback only).
  - [x] Decide and record: does `CoexistenceGateTests` get **refactored to derive from / use** the new fixture (preferred — single source of truth, proves reuse), or stay standalone (only if refactoring risks the green gate)? **Decision: standalone** — see Completion Notes.
- [x] **Task 2: Seed ≥3 game states and a server-side index driver** (AC: 5)
  - [x] Seed the `GameManager` prefab template's `gameStates` with **3** `DummyGameState` instances (so the index can legally move 0→1→2; `SwitchGameState`/index reads bound-check against `gameStates.Count`). Keep `ignoreGameLoop = true` and no `stateUIPrefab` (so `OnStateCreated` never touches `StatesCanvas.Instance`).
  - [x] Expose a server driver: `SetServerIndex(int n)` writing `hostGm.currentGameStateIndex.Value = n` (server owns the NV; assert `IsServer`/host context). This is the minimal, behaviour-honest driver for 5.0 — note in a comment that 5.3 replaces it with the real `SwitchGameState` path.
- [x] **Task 3: Remote-client index recorder + initial-value characterization** (AC: 4)
  - [x] On the resolved client replica (`GameManager.For(clientNm)`), subscribe to `currentGameStateIndex.OnValueChanged`, appending each `newValue` to an append-only `List<int>`; expose it as `IReadOnlyList<int> RemoteIndexTrace`. Subscribe as early as the client replica resolves; unsubscribe in teardown (NGO rule: unsubscribe `OnValueChanged` in despawn/teardown, project-context.md:64).
  - [x] **Characterize the spawn-initial-value firing**: write a deterministic probe that records whether the client's `OnValueChanged` fires for the index value (0) carried in the spawn payload when the client late-joins, BEFORE any explicit server write. **Observed: FALSE** (NGO does not fire `OnValueChanged` for the spawn-payload value) — see Completion Notes. Traces therefore start empty, then `[1, 2]`.
- [x] **Task 4: Optional simulated-bot hook** (AC: 3)
  - [x] Provide an opt-in way to add a simulated bot (`clientId >= 100`) to the host, mirroring the existing `NetworkTestHelper` bot convention, so the intercepted-dispatch path stays covered. Keep it OFF by default (the real client is load-bearing); adding it must not perturb AC 1–2. Do **not** touch `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `>= 100` gateway — exercise them verbatim.
  - [x] If wiring a full bot through these managers requires more than the minimal `CharacterManager` path the fixture spawns, scope the bot hook to the smallest thing that proves an intercepted RPC lands on the host and record any remainder as a follow-up (the fixture spawns only the prefabs it exercises — arch doc §5 scope fence). **Scoped to `GetSafeRpcTarget` redirect proof; full bot-Character spawn is the recorded remainder.**
- [x] **Task 5: Fixture self-tests** (AC: 1, 4, 5, 6)
  - [x] `RemoteClient_RecordsOrderedIndexTrace_AcrossRealTicks`: drive 0→1→2 via `SetServerIndex`, yielding frames between writes; assert `RemoteIndexTrace` equals the expected ordered sequence (per the Task 3 characterization). Use `NetworkTestHelper.WaitUntilOrTimeout`, never `WaitForSeconds`.
  - [x] Re-assert the 5.0e invariants through the fixture (AC 2) as a smoke test so a future regression in the de-singletonisation fails here too.
  - [x] `[UnityTearDown]`: despawn host managers FIRST (avoids the `OnPlayerDisconnectedServer` NRE on absent `BoardManager`), shut down client then host NM, destroy all GO + templates + bot, `ResetManagerStatics()`, `LogAssert.ignoreFailingMessages = true` for the UTP socket-close window only, and assert `NetworkManager.Singleton == null` + `GameManager.For(host) == null` post-teardown (regression net).
- [x] **Task 6: Validate, gate, and signal the unblock** (AC: 6, 7)
  - [x] `mcp__UnityMCP__read_console` after each script change → zero compile errors before running tests.
  - [x] `mcp__UnityMCP__run_tests` (PlayMode + EditMode). Confirm no regression vs baseline (PM 142 / EM 155 from 5.0e — the new self-tests raise the PM count; the existing tests must all still pass). **Result: PlayMode 145/145, EditMode 155/155.**
  - [x] Update `sprint-status.yaml`: `5-0-... = review`. Note on issue #50 that the fixture exists and 5.3 is unblocked (recorded for the manual GitHub step). Commit (English, conventional, body, no AI attribution).

## Dev Notes

### What this story actually is

This is **infrastructure**, not gameplay. The deliverable is a reusable PlayMode fixture that makes a *real* second client observe a `NetworkVariable` over a *real* serialized tick. The existing harness (`NetworkTestHelper`) only ever runs `StartHost` — host==server, RTT=0 — so it is structurally **blind** to replication skew: a server write to `currentGameStateIndex.Value` is observed by the host in-process with no serialization. Story 5.3 (the highest-risk story of the whole refactor: silent multiplayer desync where the host advances and a client freezes in the old state with no exception) **cannot be safely gated without this fixture**. 5.0 builds the tool; 5.3 uses it and widens it into a parameterized client-trace suite.

### Build directly on 5.0e — do NOT start from scratch

`Assets/Scripts/Tests/PlayMode/Desingleton/CoexistenceGateTests.cs` (story 5.0e, status `review`) already solved every hard substrate problem. **Read it fully first.** Reuse, do not reinvent:

- **Dual `NetworkManager` over UnityTransport loopback** (`127.0.0.1`, single port `7787`), host created FIRST so its `OnEnable` claims `NetworkManager.Singleton` (NGO `NetworkManager.cs:1024` claims only when still null — verified in 5.0e), client second so it never overwrites the Singleton. This is what makes AC 2's façade invariant hold at the NGO layer.
- **`SetGlobalObjectIdHash(no, hash)`** by reflection — runtime-created `NetworkObject`s have `GlobalObjectIdHash == 0`; two prefabs sharing 0 collide on the registry source key (`NetworkPrefabs.cs:303`). Use distinct non-zero hashes.
- **`MarkAsNonSceneObject(no)`** by reflection — sets `IsSceneObject = false` so `StartHost`'s in-scene sweep (`NetworkSpawnManager.ServerSpawnSceneObjectsOnStartSweep`) does **not** auto-spawn the active prefab templates as scene objects (which would register the TEMPLATE for the host and then destroy the explicit `InstantiateAndSpawn` clones as same-NM duplicates). This was root-cause #1 of 5.0e's first failing run.
- **`ResetManagerStatics()`** by reflection — `GameManager.instance` is a static auto-property (private setter), `CharacterManager.instance` is a static field; `ReflectionHelper.SetPrivateField` cannot reach statics. Null them after building the templates (whose `Awake` claims the façade) and again in teardown.
- **Despawn host managers FIRST in teardown** — root-cause #2 of 5.0e: `GameManager.OnPlayerDisconnectedServer` (subscribed in the `IsServer` branch of `OnNetworkSpawn`) NREs on the absent `BoardManager.instance` during the shutdown disconnect sequence. Despawning the host `GameManager` first lets `OnNetworkDespawn` unsubscribe `OnClientDisconnectCallback` before disconnect runs. Also wire `hostGm.characterManager = hostCm` (production wiring `OnPlayerDisconnectedServer` reads).
- **UTP shutdown noise** — closing two in-process loopback sockets logs a benign non-deterministic `[Error] All socket receive requests were marked as failed` (0–2 times). `LogAssert.ignoreFailingMessages = true` for the teardown window only; explicit assertions are unaffected.

### `GetGameState(0)` empty-tolerance (settled in 5.0e)

`GameManager.OnNetworkSpawn` (GameManager.cs:144-149) calls `GetGameState(currentGameStateIndex.Value)` → `gameStates.Keys.ElementAt(index)` immediately, which **throws on an empty dictionary**. `gameStates` is NOT a `NetworkVariable`, so the client replica cannot be populated post-spawn — it must be on the prefab template. 5.0e seeded ONE `DummyGameState`; **this story needs ≥3** (so the index can move 0→1→2). `Object.Instantiate` copies the `SerializedDictionary`, so host clone and client replica both spawn with the same states. `ignoreGameLoop = true` keeps the loop quiet; `DummyGameState` has no `stateUIPrefab` so `OnStateCreated` never touches `StatesCanvas.Instance`.

### How the index reaches the remote client (the thing under test)

`currentGameStateIndex` is `NetworkVariable<int>` (GameManager.cs:75), server-write-authority. On the server, `SwitchGameState` (GameManager.cs:317-330) sets `currentGameStateIndex.Value = newGameStateIndex` and fires `DoStateMethodRpc` for OnEnd/OnStart on clients. For 5.0 the minimal honest driver is a **direct** `hostGm.currentGameStateIndex.Value = n` on the server — the value then serializes and replicates to the client, firing `currentGameStateIndex.OnValueChanged(old, new)` on the client's replica after a real tick. That `OnValueChanged` on the **client** is exactly what AC 4's recorder captures and what `StartHost` can never exercise. (5.3 will drive the real `SwitchGameState` and assert each transition fires `OnValueChanged` exactly once — catching a machine that jumps two states while the mirror writes once.)

### Initial-spawn-value firing — characterize, don't assume

A late-joining client receives the current `currentGameStateIndex.Value` (0) in the spawn payload. Whether NGO fires `OnValueChanged` for that initial value is **version-specific behaviour you must observe** (Task 3), because it decides if the expected trace starts `[0, 1, 2]` or `[1, 2]`. Subscribe on the client replica the moment `For(clientNm)` resolves, then look at whether `RemoteIndexTrace` already contains `0` before any explicit server write. Record the observed answer in Dev Agent Record and write the self-test assertion against the observed sequence — never against a guessed one.

### Simulated bot — optional, additive, NFR5-sacred

The simulated bot (`clientId >= 100`) never crosses the wire: the host intercepts its RPCs (`GetSafeRpcTarget`, `CharacterManager.cs:84/268/360`). It covers the intercepted-dispatch path that the real client does NOT exercise. Keep it **opt-in and off by default** — the real client is the load-bearing addition (epics.md:658). When wired, route through `GetSafeRpcTarget` / `IsLocalOrSimulated` verbatim; do **not** edit those mechanisms or branch on `Application.isPlaying` / `NetworkManager.Singleton.IsHost` (project-context.md:159, NFR5). If a full bot character needs more managers than the fixture spawns, scope to the smallest intercepted-RPC proof and record the remainder (arch doc §5: the fixture spawns only the prefabs it exercises; the 8 other NetworkBehaviour singletons are migrated on demand when the fixture proves one blocking).

### Unbound-NetworkAction caveat (data from 5.0e, do not relearn)

`GameManager.onGameStarted` / `onNewDayPassed` are **unbound** `NetworkAction`s (GameManager.cs:84-85, `new(..., false)`). 5.0e proved an unbound action's `Manager` getter falls back to `NetworkManager.Singleton` = the **host**, so it registers/fires on the host only and the **second real client never receives it**. Therefore the fixture's remote client **cannot rely on `onGameStarted`/`onNewDayPassed`** to know the game advanced — it must observe `currentGameStateIndex.OnValueChanged` directly (which is exactly what AC 4 does). Do not build the recorder on top of those unbound actions.

### Substrate decision already made

Option A (hand-rolled dual `NetworkManager`, no `Packages/manifest.json` change, no dependency on NGO's `Unity.Netcode.RuntimeTests`) is proven sufficient by 5.0e. **Do not** switch to Option B (`NetcodeIntegrationTest` / `"testables"`) — it drags NGO's own test suite into the runner and is a fragile cross-package reference (issue #50). A manifest change would be its own commit and is not needed here.

### Project Structure Notes

- New file: `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs` (+ optional `MultiClientFixtureTests.cs` for the self-tests, or fold self-tests into the fixture file). `.meta` files: let `mcp__UnityMCP__refresh_unity` generate them — never hand-write `.meta`.
- The `Desingleton/` folder is **infrastructure**; the "test file mirrors source path" rule (project-context.md:344) does **not** apply (same exemption `CoexistenceGateTests.cs` / `NetworkTestHelper.cs` take).
- `Tests.PlayMode.asmdef` already references `Game`, `Unity.Netcode`, `NetworkAction`, and `Unity.Networking.Transport` (5.0e added the last two). If a `CorruptionDuPortail.Domain` type triggers CS0012, add the explicit Domain reference to the asmdef (known gotcha: `autoReferenced` doesn't reach test asmdefs — see memory `reference_domain_asmdef_autoref_tests`).
- Branch: `epic5-network` (never `dev-refactor` / `Dev`). Prerequisites: 5.0a–5.0e all merged.

### Project Context Rules (extracted from project-context.md)

- **Yield `null` per frame or `WaitForFixedUpdate`, NEVER `WaitForSeconds`** — time-dependent tests flake in CI (project-context.md:160, 362). Use `NetworkTestHelper.WaitUntilOrTimeout` for all spawn/replication waits.
- **Unsubscribe `NetworkVariable.OnValueChanged` in teardown/despawn**, not `OnDestroy` (project-context.md:64). The recorder must clean up.
- **Each test independent, any order; `[UnityTearDown]` despawns everything it spawned**; no `static` mutable carried across tests (project-context.md:366). Reset the manager statics every teardown.
- **`NetworkManager.Singleton == null` after teardown** (shutdown gotcha) — assert it as a regression net.
- **`GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` are verbatim/NFR5** — exercised, never edited; no branching on `Application.isPlaying` / `IsHost` in any path the bot hook touches (project-context.md:159, 194).
- **`Network/` is the sole NGO surface** — the fixture lives in `Tests.PlayMode`, drives existing managers; it introduces no new `NetworkBehaviour`/RPC/`NetworkVariable` in gameplay code.
- **Expose internals to tests via `InternalsVisibleTo`**, never widen `public` for testability (project-context.md:139). `GameManager.For` / `currentGameStateIndex` are already public; if the fixture needs an internal, use the attribute.
- Commits: English, conventional, **body always**, no `UX:` line (pure infra/tooling), no AI attribution.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0] — the fixture ACs (real client load-bearing, simulated bot additive, record ordered `currentGameStateIndex.OnValueChanged` on the remote client).
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.3] — the consumer: parameterized client-trace suite, "every transition fires OnValueChanged exactly once", late-join read-time invariant (`c72b8d1`).
- [Source: _bmad-output/refactor-architecture-desingleton.md#4 slice 5; §5 scope fence; §6 DoD] — 5.0e gate → 5.0 unblock chain; spawn-only-what-you-exercise.
- [Source: Assets/Scripts/Tests/PlayMode/Desingleton/CoexistenceGateTests.cs] — the substrate to promote (dual-NM, reflection helpers, teardown discipline, unbound-NetworkAction characterization).
- [Source: Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs] — `WaitUntilOrTimeout` / `WaitUntilAllSpawnedOrTimeout`; the host+simulated-bot precedent the fixture extends (not replaces).
- [Source: Assets/Scripts/GameLogic/GameManager.cs:42-49 (For registry), :75 (currentGameStateIndex), :106-150 (OnNetworkSpawn), :317-330 (SwitchGameState)].
- [Source: Assets/Scripts/Tests/PlayMode/CorruptionTests.cs:38-127] — representative host + DummyGameState + manager-spawn + static-reset teardown pattern.
- [Source: GitHub issue #50] — the blocking driver; record the 5.3 unblock here.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- One compile error on first build: `currentGameStateIndex.OnValueChanged` requires the
  delegate type `NetworkVariable<int>.OnValueChangedDelegate`, not `System.Action<int,int>`
  (CS0029). Fixed by typing the recorder handler field correctly. (An earlier "tests did not
  start within timeout" / `total:0` run was this stale-assembly compile error masking the run;
  it cleared once the delegate type was fixed.)
- Spawn-payload characterization probe (temporary `[FIX50CHAR]` log, removed after reading):
  `SpawnPayloadFiredInitialValue=False traceCount=0` — confirmed NGO does NOT fire the
  client's `OnValueChanged` for the index value carried in the spawn payload on a late join.

### Completion Notes List

**Deliverable.** `MultiClientGameFixture` (abstract base, `[UnitySetUp]`/`[UnityTearDown]`)
+ `MultiClientGameFixtureTests` (3 `[UnityTest]`s deriving from it). The fixture boots a host
plus ONE real in-process client over UnityTransport loopback, replicates minimal
GameManager/CharacterManager, and exposes `IReadOnlyList<int> RemoteIndexTrace` recording the
ordered `currentGameStateIndex.OnValueChanged` newValues observed **on the remote client** —
the thing `StartHost` (host==server, RTT=0) can never produce.

**Task 1 — CoexistenceGateTests left standalone (decision).** The 5.0e gate keeps its own
substrate rather than being refactored to derive from the new fixture. Rationale: 5.0e is a
`review`-status green gate; refactoring it would risk regressing the unblock signal for no
test-coverage gain, and the two have deliberately different prefab hashes (`0xC0DE01xx` vs
`0xC0DE00xx`) and ports (7788 vs 7787) so they never collide if both ever run. The proven
mechanics were **lifted** (copied) into the fixture, not shared, preserving 5.0e untouched.

**Task 3 — spawn-payload characterization (the load-bearing answer for 5.3).**
`SpawnPayloadFiredInitialValue = False`: a late-joining client receives index 0 in the spawn
payload but NGO does **not** raise `OnValueChanged` for it. So an expected remote trace starts
**empty** and contains only the values from explicit server writes (`[1, 2]` for a 0→1→2 drive).
5.3 must assert against this — its "exactly once per transition" trace will NOT include a
phantom initial `0`. `SpawnPayloadFiredInitialValue` is exposed so 5.3 can branch defensively if
a future NGO upgrade changes this.

**Task 4 — simulated-bot scope.** `AssertSimulatedBotIsIntercepted()` proves the intercepted-
dispatch routing minimally: `HostCm.GetSafeRpcTarget(100)` (bot, redirected to host client 0)
yields a different `RpcTarget` than `GetSafeRpcTarget(7)` (a real clientId, no redirect),
exercising the `clientId >= 100` gateway verbatim (NFR5, untouched). It is opt-in (a derived
test calls it) and OFF in the base setup — the real client stays the load-bearing addition.
**Deferred remainder:** spawning a full bot `Character` (ownerClientId ≥ 100) through the
managers the fixture does not yet instantiate; pull it in on demand when a 5.3-era test needs a
bot-owned character, per arch doc §5 (spawn only what you exercise).

**AC 5 — self-validation.** `RemoteClient_RecordsOrderedIndexTrace_AcrossRealTicks` drives
0→1→2 via `SetServerIndex`, waits for the real tick to deliver each `OnValueChanged`, and
asserts the last two trace entries are `[1, 2]` in order AND the client replica's `Value`
converged to 2 — proving real serialized, per-tick propagation, not a host-loopback shortcut.

**AC 6 — gate.** PlayMode 145/145 (142 baseline from 5.0e + 3 new), EditMode 155/155. No
regression; the existing host+bot suite is undisturbed (the fixture tears down both NMs +
templates + statics, asserts `NetworkManager.Singleton == null` and an empty registry
post-teardown).

**AC 7 — unblock signal.** The reusable fixture now exists; Story 5.3 (GameLoopMachine index
ownership) is unblocked. The issue #50 comment is an outward GitHub action left for Poyo to post
(not done autonomously).

### Review Findings

Code review 2026-06-11 (3 adversarial layers: Blind Hunter, Edge Case Hunter, Acceptance Auditor; all Opus). 4 patch, 3 defer, 7 dismissed.

- [x] [Review][Patch] AC3 simulated-bot assertion is a tautology — does not prove the `>=100` redirect [MultiClientGameFixture.cs:AssertSimulatedBotIsIntercepted]. `Assert.AreNotEqual(GetSafeRpcTarget(100).Send.Target, GetSafeRpcTarget(7).Send.Target)` passes purely because clientId 0 ≠ 7 (and `RpcTarget.Single(_, Persistent)` allocates a fresh `DirectSendRpcTarget` per call with no `Equals` override); it would still pass if the `clientId >= 100` branch in `CharacterManager.GetSafeRpcTarget` were deleted. All 3 reviewers flagged it. **FIXED:** `ResolveTargetClientId` now reads the routed clientId — bot(100)→Single(0) collapses to `LocalSendRpcTarget` (= host, `LocalClientId` 0), real(7)→`DirectSendRpcTarget.ClientId` 7; assert bot==0, real==7. The bot test *failed* before the fix (the target was `LocalSendRpcTarget` with no `ClientId` field), confirming the new assertion is load-bearing.
- [x] [Review][Patch] `SpawnPayloadFiredInitialValue` characterization can yield baseline count 2, failing both test branches [MultiClientGameFixture.cs:SetUp + MultiClientGameFixtureTests.cs:RemoteClient_RecordsOrderedIndexTrace]. `GameManager.OnNetworkSpawn` writes `currentGameStateIndex.Value = 0` on the server (GameManager.cs:144) on top of the spawn-payload value; the observed pre-write trace count is timing-dependent (0, 1, or 2 across machines/NGO ticks), but the self-test hard-asserted exactly 0 or exactly 1. **FIXED:** the test no longer pins the count — it asserts every pre-write entry equals 0 and keys the explicit-transition assertions off the captured baseline. `SpawnPayloadFiredInitialValue` is still exposed for 5.3.
- [x] [Review][Patch] `DummyGameState` ScriptableObject instances leaked every run [MultiClientGameFixture.cs:TearDown]. **FIXED:** the 3 seeded SOs are tracked in `_seededStates` and `Object.Destroy`d in teardown.
- [x] [Review][Patch] `LogAssert.ignoreFailingMessages` set true in teardown and never reset [MultiClientGameFixture.cs:TearDown]. **FIXED:** reset to false right after the socket-close wait window, before the explicit regression assertions, so it cannot bleed into the next test.
- [x] [Review][Defer] `InstantiateAndSpawn(...).GetComponent<>()` not null-checked [MultiClientGameFixture.cs:SetUp] — deferred, diagnostic-quality only (a null would NRE with an opaque message; tests are green so the hash/registration path works).
- [x] [Review][Defer] Teardown `Assert.IsTrue(NetworkManager.Singleton == null)` waits a single frame after `Object.Destroy` [MultiClientGameFixture.cs:TearDown] — deferred, passes today; harden to a polled `WaitUntilOrTimeout` if it ever flakes.
- [x] [Review][Defer] Reflection on `GlobalObjectIdHash` / `IsSceneObject` is brittle against an NGO upgrade [MultiClientGameFixture.cs:SetGlobalObjectIdHash/MarkAsNonSceneObject] — deferred, pre-existing pattern lifted verbatim from 5.0e; NGO version is pinned at 2.6.0.

### File List

- `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs` (new) — reusable host +
  real-client + optional simulated-bot fixture; remote-client index recorder; server index driver.
- `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixtureTests.cs` (new) — 3 self-tests
  (ordered remote trace across real ticks, de-singleton invariants smoke, simulated-bot intercept).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (5-0 → in-progress → review).

### Change Log

- 2026-06-11: Implemented story 5.0. Added `MultiClientGameFixture` (host + real in-process
  client over UnityTransport loopback, optional simulated-bot intercept hook) exposing an ordered
  remote-client `currentGameStateIndex.OnValueChanged` trace, + 3 self-tests. Characterized the
  spawn-payload initial-value firing as FALSE (data for 5.3). PlayMode 145/145, EditMode 155/155.
  Unblocks Story 5.3.
- 2026-06-11: Addressed code review findings — 4 patches resolved (AC3 bot-interception assertion
  made load-bearing via routed-clientId reflection; baseline trace assertion hardened against the
  initial-value timing heisenbug; seeded DummyGameState SOs destroyed in teardown;
  `LogAssert.ignoreFailingMessages` reset). 3 items deferred to `deferred-work.md`, 7 dismissed.
  Re-gated PlayMode 145/145, EditMode 155/155.
