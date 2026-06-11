# Story 5.0e: Coexistence gate — two in-process clients without clobber

Status: review

## Story

As a developer,
I want a minimal PlayMode probe spinning up two `NetworkManager`s with replicated `GameManager` + `CharacterManager`,
so that the de-singletonisation (5.0a–5.0d) is proven sufficient before investing in the full 5.0 fixture API.

## Acceptance Criteria

1. A PlayMode test starts two `NetworkManager`s in one process (host + one real client over loopback transport) and the second client replicates `GameManager` + `CharacterManager` prefab instances.
2. No replica is destroyed at Awake (assert both managers exist and are alive on the client side after spawn).
3. The `instance` façades still point at the **host's** objects throughout.
4. `GameManager.For(clientNm)` / `CharacterManager.For(clientNm)` resolve the client's instances; `For(hostNm)` resolves the host's.
5. A `NetworkAction` bound to the client's manager does not hijack the host's named-message handler (and the unbound `GameManager` field-init actions' behavior on the second client is characterized and recorded — this answers the question deferred by 5.0b).
6. Green here = explicit unblock signal for story 5.0; flip `5-0-...` from `blocked` to `backlog` in sprint-status.yaml and note it on issue #50.

## Tasks / Subtasks

- [x] Task 1: Choose the two-NetworkManager substrate (Dev Notes, "Substrate options") and record the choice.
- [x] Task 2: Build the probe test in `Assets/Scripts/Tests/PlayMode/Desingleton/CoexistenceGateTests.cs` (new folder OK; mirrors-source rule does not apply — this is infrastructure, like `NetworkTestHelper.cs`).
- [x] Task 3: Assertions for AC 2-4 (skeleton in Dev Notes).
- [x] Task 4: NetworkAction cross-wiring assertion + characterization of the unbound field-init actions (AC: 5). Record findings in Dev Agent Record — they feed the 5.0b deferred decision.
- [x] Task 5: Full suite green (the probe must not destabilize the existing host+bot PlayMode tests: it must tear down BOTH NetworkManagers and all spawned objects in `[TearDown]`).
- [x] Task 6: Update sprint-status + comment on issue #50 (AC: 6). Commit.

## Dev Notes

### Substrate options (pick one, record why)

**Option A — hand-rolled dual NetworkManager (recommended, no manifest change):**
Create two inactive `GameObject`s, each with a `NetworkManager` + `UnityTransport` component (`com.unity.transport` ships with NGO; UnityTransport on `127.0.0.1`, same port). Configure both `NetworkConfig.NetworkPrefabs` with the manager prefabs. `hostNm.StartHost()`, then `clientNm.StartClient()`. Caveats:
- NGO's `NetworkManager.Singleton` is claimed by whichever `NetworkManager` initializes first — verify in NGO source (`NetworkManager.cs`, look for `SetSingleton` / `Singleton =` assignments and whether a second instance overwrites or refuses). The test must assert `NetworkManager.Singleton == hostNm` after both are running — if the second overwrites, the façade invariant (AC 3) is broken at the NGO layer and the whole approach needs the integration-helper route (Option B).
- The project rule "never call `StartHost()` directly in a test — route through `NetworkTestHelper`" exists to protect the bot-flow harness. This probe is the documented exception: it tests the multi-NM substrate itself. Put that sentence in a comment at the top of the file so a future reviewer doesn't "fix" it.
- The game's production transport is Facepunch (Steam) — do NOT use it in tests; UnityTransport loopback only. `NetworkTransportDetector` is bypassed because you configure the transport explicitly on each NM.

**Option B — NGO integration helpers (`NetcodeIntegrationTest`):** requires `"testables": ["com.unity.netcode.gameobjects"]` in `Packages/manifest.json` (NGO 2.6.0 ships the helpers inside `Unity.Netcode.RuntimeTests`; there is no standalone TestHelpers asmdef in this layout — see issue #50). This drags NGO's own test suite into the runner and adds a fragile cross-package reference. Only fall back to this if Option A's Singleton behavior makes hand-rolling impossible. A manifest change must be its own commit and flagged in the PR description.

### Prefab setup

The real `GameManager`/`CharacterManager` live in `GameScene` — do NOT load `GameScene` for this probe (its other NetworkBehaviour singletons — `ChatManager`, `MessageManager`, `StatesCanvas`, `GameAudioManager`, `BoardManager`, etc. — are NOT de-singletonised and would clobber on the second client; architecture doc §5 scope fence). Instead build minimal prefabs **in the test** (programmatic `GameObject` + `AddComponent<GameManager>` / `AddComponent<CharacterManager>` + `NetworkObject`, registered as a network prefab on both NMs), OR reference an existing minimal prefab if one exists under `Assets/Prefabs/` (check first — reuse beats reinvention). `GameManager.OnNetworkSpawn` calls `SetupGameStates()` on an empty `gameStates` dictionary — verify it tolerates empty (the foreach just doesn't run; `GetGameState(0)` would throw → if `OnNetworkSpawn` requires states, populate `gameStates` with a single minimal test `GameState` ScriptableObject instance, or assert on `CharacterManager` only for the spawn-path ACs and `GameManager` for the registry ACs with `ignoreGameLoop = true` if needed — read `GetGameState` and decide; record the choice).

### Assertion skeleton (AC 2-4)

```csharp
// after clientNm connected and prefabs replicated (wait frames, not WaitForSeconds):
Assert.IsTrue(NetworkManager.Singleton == hostNm, "second NM must not steal Singleton");
Assert.IsNotNull(GameManager.For(clientNm), "client replica was destroyed or never registered");
Assert.AreNotEqual(GameManager.For(hostNm), GameManager.For(clientNm));
Assert.AreEqual(GameManager.instance, GameManager.For(hostNm), "façade must stay primary");
// idem CharacterManager
```

Wait for replication by polling `For(clientNm) != null` across frames with a frame-count cap (~300 frames), `yield return null` each frame — never `WaitForSeconds` (CI-flake rule).

### NetworkAction assertion (AC: 5)

Host side: NetworkBehaviour-bound action `new NetworkAction("coexistProbe", hostSpawnedBehaviour)` + listener. Client side: same string on the client's spawned counterpart behaviour. Invoke from host; assert host listener fired and the message arrived on the client's NM (client listener fired exactly once), and that registering the client's action did not replace the host's handler (invoke again after client registration; host listener must still fire). Then characterize the **unbound** path: construct `new NetworkAction("coexistGlobalProbe", false)` while both NMs run — record (in Dev Agent Record + issue #50 comment) which `CustomMessagingManager` it lands on (it will be `Singleton` = host) and what that means for the 5.0 fixture (the fixture's second client cannot rely on unbound actions; GameManager's `onGameStarted`/`onNewDayPassed` will need binding or a fixture-side workaround — that is the 5.0b deferred decision, now with data).

### TearDown discipline (protects the whole suite)

`[TearDown]`: `clientNm.Shutdown()`, `hostNm.Shutdown()`, destroy both NM GameObjects and all test-spawned objects, then verify `NetworkManager.Singleton == null` (NGO clears it on shutdown of the owning instance — verify in source; if it doesn't, destroy in the right order so the next test's `NetworkTestHelper` boots clean). The registry statics self-clean via `OnNetworkDespawn`/`OnDestroy` (5.0c/5.0d) + the `SubsystemRegistration` reset — assert `GameManager.For(hostNm) == null` post-teardown as a regression net.

### Test asmdef note

`Tests.PlayMode.asmdef` already references `Game` and Unity.Netcode (existing network tests compile). If you hit CS0012 on `CorruptionDuPortail.Domain` types, add the explicit Domain reference to the asmdef (known project gotcha: `autoReferenced` doesn't reach test asmdefs).

### Commit message template

```
test(net): add two-NetworkManager coexistence gate for the de-singletonised managers

PlayMode probe boots a host plus a real in-process client over UnityTransport
loopback, replicates minimal GameManager/CharacterManager prefabs, and asserts:
no replica destroyed at Awake, instance facades stay on the host, For(nm) resolves
per-manager instances, and a bound NetworkAction does not hijack the host handler.
This is the explicit unblock gate for the full multi-client fixture (story 5.0,
issue #50) and records the unbound-NetworkAction characterization deferred by 5.0b.
```

No `UX:` line. No AI attribution.

### Project Structure Notes

- New file: `Assets/Scripts/Tests/PlayMode/Desingleton/CoexistenceGateTests.cs` (+ .meta generated by Unity — let `refresh_unity` create it, never hand-write .meta).
- Prerequisites: 5.0a–5.0d ALL merged.
- Branch: `epic5-network`.

### Project Context Rules (extracted from project-context.md)

- Yield `null` per frame, never `WaitForSeconds` (flake rule).
- Each test independent + any order; `[TearDown]` despawns everything it spawned.
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]` already on the test asmdef — expected.
- Editor-only packages never referenced from runtime asmdefs (irrelevant here but keep the probe inside Tests.PlayMode).
- `NetworkManager.Singleton == null` checks during teardown (shutdown gotcha).
- Commits: English, conventional, body always, no AI attribution.

### References

- [Source: _bmad-output/refactor-architecture-desingleton.md#4 slice 5; #6 DoD]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0e and #Story 5.0 (the fixture this unblocks)]
- [Source: GitHub issue #50 — testables/`Unity.Netcode.RuntimeTests` discussion]
- Harness precedent: `Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs` (host + simulated bots — the thing this probe deliberately does NOT use)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- First run: 4/4 failed. Root cause #1 — the prefab templates are active GameObjects
  carrying a `NetworkObject`, so `StartHost`'s in-scene sweep
  (`NetworkSpawnManager.ServerSpawnSceneObjectsOnStartSweep`, spawns anything with
  `IsSceneObject` null or true) auto-spawned the TEMPLATES as scene objects. They
  registered for the host and claimed `instance`; our explicit `InstantiateAndSpawn`
  clones were then destroyed by the same-NM duplicate guard in `OnNetworkSpawn`. Fix:
  set `IsSceneObject = false` on each template via reflection (`MarkAsNonSceneObject`)
  to exclude them from the sweep while keeping them active (so instantiated clones run
  `Awake`).
- Root cause #2 — at shutdown, `GameManager.OnPlayerDisconnectedServer` (subscribed in
  the IsServer branch of `OnNetworkSpawn`) NRE'd on the absent `BoardManager.instance`
  (and the unwired `characterManager`). Fix: despawn the host GameManager/CharacterManager
  FIRST in `[TearDown]` so `OnNetworkDespawn` unsubscribes the callback before the
  disconnect sequence; also wired `characterManager` for parity.
- Second run: 4/4 bodies passed; only failure was a benign UTP shutdown log
  `[Error] All socket receive requests were marked as failed` when two in-process
  loopback sockets close. Non-deterministic count (0–2) makes `LogAssert.Expect`
  fragile, so `LogAssert.ignoreFailingMessages = true` for the teardown window only
  (explicit assertions are unaffected).
- Final: CoexistenceGateTests 4/4 PASS; full PlayMode 142/142; EditMode 155/155.

### Completion Notes List

**Task 1 — substrate.** Option A (hand-rolled dual `NetworkManager`, UnityTransport
loopback, no `Packages/manifest.json` change, no dependency on NGO's RuntimeTests).
Verified against NGO source: `NetworkManager.Singleton` is claimed in `OnEnable` only
when still null (`NetworkManager.cs:1024`), so the host (created first) keeps the
Singleton and the client never overwrites it — Option B was unnecessary. The host's
in-process client connects over `127.0.0.1` loopback.

**GetGameState empty-tolerance decision.** `GameManager.OnNetworkSpawn` calls
`GetGameState(0)` (`ElementAt`) immediately, which throws on an empty dictionary. `gameStates`
is NOT a NetworkVariable, so the client replica cannot be populated post-spawn. Resolution:
seed ONE `DummyGameState` into the prefab template's `gameStates`; `Object.Instantiate`
(used by both the host `InstantiateAndSpawn` and the client replication path) copies the
SerializedDictionary, so host clone and client replica both spawn with a valid state.
`ignoreGameLoop = true` keeps the loop quiet. `DummyGameState` has no `stateUIPrefab`, so
`OnStateCreated` never touches `StatesCanvas.Instance`.

**AC 5 — NetworkAction characterization (feeds the 5.0b deferred decision):**
- BOUND actions (`new NetworkAction(id, networkBehaviour)`) set `boundNetworkManager =
  behaviour.NetworkManager`, so each routes through its OWN NM's `CustomMessagingManager`.
  The wire key (`id_{NetworkObjectId}_{NetworkBehaviourId}`) is identical on host and client
  because the IDs replicate, yet the two handlers live on two different managers → NO
  cross-NM clobber. Verified: one host invoke fired host (loopback) and client listeners
  exactly once each; a second invoke after the client registered still fired the host
  listener (handler not hijacked). Per-NM isolation holds.
- UNBOUND actions (`new NetworkAction(id, false)`, the kind `GameManager.onGameStarted` /
  `onNewDayPassed` are) have no `boundNetworkManager`, so the `Manager` getter falls back
  to `NetworkManager.Singleton` = the HOST. They register and fire on the host only; the
  second client's NM never receives them. **Consequence for the full 5.0 fixture: the
  second real client cannot rely on unbound NetworkActions.** `GameManager`'s field-init
  actions will need either binding to the spawned GameManager behaviour, or a fixture-side
  workaround. This is the data the 5.0b deferred decision was waiting for (recorded on
  issue #50).

**AC 6 — unblock signal.** Gate is green, so `5-0-...` is flipped `blocked → backlog` in
sprint-status.yaml and noted on issue #50.

### File List

- `Assets/Scripts/Tests/PlayMode/Desingleton/CoexistenceGateTests.cs` (new)
- `Assets/Scripts/Tests/PlayMode/Tests.PlayMode.asmdef` (added `NetworkAction` and
  `Unity.Networking.Transport` references — direct `NetworkAction` use and
  `UnityTransport.SetConnectionData`'s `NetworkEndpoint`)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (5-0e → review; 5-0 blocked → backlog)

### Change Log

- 2026-06-11: Implemented story 5.0e. Added the two-NetworkManager coexistence gate
  (4 PlayMode tests, all AC covered). Characterized the unbound-NetworkAction path for
  5.0b. Flipped story 5.0 from blocked to backlog. PlayMode 142/142, EditMode 155/155.
