# Story 5.0a: Mechanical `NetworkManager` resolution (zero observable change)

Status: review

## Story

As a developer,
I want every `NetworkManager.Singleton` read inside NetworkBehaviours, GameStates, and the wire-reference wrapper replaced by the locally-resolvable `NetworkManager`,
so that the trivially-injectable population is migrated first, at zero behavioral risk, on the way to unblocking the 5.0 multi-client fixture (issue #50).

## Acceptance Criteria

1. NetworkBehaviours use the inherited `NetworkManager` property instead of `NetworkManager.Singleton` at the 4 sites listed below.
2. GameStates use the already-injected `gameManager.NetworkManager` at the 9 sites listed below.
3. `NetworkBehaviourReferenceWrapper.TryGet` gains an optional `NetworkManager` parameter (default `null` → falls back to `Singleton`); existing callers compile unchanged.
4. Pre-game bootstrap (MainMenu, LobbySelectionPanel, LoginMenu, FacepunchTransport, NetworkTransportDetector), dev scripts (`IsServerTest`, `NetworkActionTester`, `DevIdentityController`), and the scene-local Mono `PowerManager` are **not touched**.
5. Full EditMode + PlayMode suite passes with the same counts as the pre-change baseline; `read_console` shows 0 errors.
6. One commit, conventional format, no AI attribution.

## Tasks / Subtasks

- [x] Task 1: Record test baseline (AC: 5)
  - [x] `mcp__UnityMCP__run_tests` mode=EditMode, then mode=PlayMode, on the untouched branch. Record pass counts in Dev Agent Record (last known: 149 EM + 138 PM before #51/#52 added tests — re-measure, do not trust this number). **Measured: 155 EM + 138 PM, 0 fail.**
- [x] Task 2: NetworkBehaviour sites (AC: 1)
  - [x] `Assets/Scripts/GameLogic/GameManager.cs:400` — `NetworkManager.Singleton.Shutdown();` → `NetworkManager.Shutdown();`
  - [x] `Assets/Scripts/Characters/CharacterManager.cs:342` — `Assert.IsTrue(NetworkManager.Singleton.IsServer, ...)` → `Assert.IsTrue(NetworkManager.IsServer, ...)`
  - [x] `Assets/Scripts/Characters/CharacterManager.cs:358` — same replacement
  - [x] `Assets/Scripts/Characters/Powers/PVisionOfTheImpossible.cs:109` — same replacement
- [x] Task 3: GameState sites (AC: 2)
  - [x] `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:53,58,62` — `NetworkManager.Singleton` → `gameManager.NetworkManager`
  - [x] `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:184,325` — `if (!NetworkManager.Singleton.IsServer)` → `if (!gameManager.NetworkManager.IsServer)`
  - [x] `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs:123,171` — Assert sites → `gameManager.NetworkManager.IsServer`
  - [x] `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs:241,258` — `NetworkManager.Singleton.RpcTarget.ClientsAndHost` → `gameManager.NetworkManager.RpcTarget.ClientsAndHost`
- [x] Task 4: Wrapper signature (AC: 3)
  - [x] `Assets/Scripts/Network/NetworkBehaviourReferenceWrapper.cs:28-42` — see exact diff in Dev Notes
- [x] Task 5: Gate + commit (AC: 5, 6)
  - [x] `mcp__UnityMCP__refresh_unity` (compile=request) → `mcp__UnityMCP__read_console` (types=error) → fix until clean (0 errors after each batch)
  - [x] Full EM + PM suite green at baseline counts (155 EM + 138 PM post-change, identical to baseline)
  - [x] Commit `e2049fd` (message per template in Dev Notes)

## Dev Notes

### Why this is safe (the invariant you must not break)

`NetworkBehaviour.NetworkManager` resolves to `NetworkObject.NetworkManager`, which returns the internal `NetworkManagerOwner` **if set, else `NetworkManager.Singleton`**. Production runs exactly one `NetworkManager` per process, so every replacement below is observationally identical. You are changing the *resolution path*, never the resolved object.

### Trap 1 — do NOT use the `IsServer` shorthand

`NetworkBehaviour.IsServer` is **not** equivalent to `NetworkManager.Singleton.IsServer`: it can be false before the behaviour is spawned. Always go through the `NetworkManager` property: `NetworkManager.IsServer`. Same rule in GameStates: use `gameManager.NetworkManager.IsServer`, not `gameManager.IsServer` (even though the `GameState` base class uses `gameManager.IsServer` in its own asserts — do not "harmonize" those, leave them alone).

### Trap 2 — GameState context is valid

`GameState` is a `ScriptableObject` clone with `public GameManager gameManager { get; set; }` (`GameState.cs:17`), assigned in `GameManager.SetupGameStates()` (`GameManager.cs:117`) **before** `OnStateCreated()` is called (`GameManager.cs:118`). So `gameManager` is non-null at every site in Task 3, including `LobbyState.OnStateCreated`. The pattern already exists in the same file: `LobbyState.cs:68,74` already use `gameManager.NetworkManager.OnClientConnectedCallback`. You are extending an existing idiom, not inventing one.

### Trap 3 — scope fence (do not "improve" while you're there)

Out of scope, leave every one of these untouched even though they read `NetworkManager.Singleton`:
- `Assets/Scripts/UI/MainMenu.cs` (12 uses), `UI/LobbyUI/LobbySelectionPanel.cs`, `UI/LoginMenu.cs`, `UI/StateUI/LobbyUI.cs`, `UI/StateUI/AwakeningRecap/AwakeningRecapMessages.cs`, `UI/Misc/ShutOffGameButton.cs` — pre-game/bootstrap UI, legitimately global.
- `Assets/Scripts/Facepunch/FacepunchTransport.cs`, `Network/NetworkTransportDetector.cs` — transport infra.
- `Assets/Scripts/Network/IsServerTest.cs`, `Test/NetworkActionTester.cs`, `Misc/DevIdentityController.cs` — dev/debug scripts.
- `Assets/Scripts/GameLogic/PowerManager.cs` (6 uses) — it is a **MonoBehaviour** (no inherited `NetworkManager`), scene-local, not replicated; handled later if ever.
- `Assets/Plugins/NetworkAction/NetworkAction.cs` (36 uses) — that is story 5.0b, not this one.
- `Assets/Scripts/GameSceneOnlineChecker.cs`, `Network/NetworkBehaviourReferenceWrapper.cs` callers — callers unchanged by design (optional param).

### Exact diff for `NetworkBehaviourReferenceWrapper.TryGet`

Current (`NetworkBehaviourReferenceWrapper.cs:28-42`):

```csharp
public bool TryGet<T>(out T _behaviour) where T : NetworkBehaviour
{
    _behaviour = null;
    if (networkBehaviourId == NULL_NETWORK_BEHAVIOUR_ID || networkObjectId == NULL_NETWORK_OBJECT_ID)
    {
        return false;
    }
    
    if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject _networkObject))
    {
        _behaviour = _networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId) as T;
        return _behaviour != null;
    }
    return false;
}
```

Target:

```csharp
public bool TryGet<T>(out T _behaviour, NetworkManager _networkManager = null) where T : NetworkBehaviour
{
    _behaviour = null;
    if (networkBehaviourId == NULL_NETWORK_BEHAVIOUR_ID || networkObjectId == NULL_NETWORK_OBJECT_ID)
    {
        return false;
    }
    
    var _manager = _networkManager != null ? _networkManager : NetworkManager.Singleton;
    if (_manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject _networkObject))
    {
        _behaviour = _networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId) as T;
        return _behaviour != null;
    }
    return false;
}
```

Do **not** migrate any `TryGet` caller to pass a manager in this story — the parameter exists so later stories (5.0c/5.0d) and the fixture can use it. Do not touch `NetworkSerialize`, `Equals`, `GetHashCode` — this struct's wire format is frozen.

### Gate procedure (after EVERY file edit batch)

1. `mcp__UnityMCP__refresh_unity` with `compile: "request"`, `wait_for_ready: true`.
2. `mcp__UnityMCP__read_console` with `types: ["error"]` — must be empty. Compile errors here mean your edit broke something; fix before proceeding.
3. After all edits: `mcp__UnityMCP__run_tests` mode=EditMode then mode=PlayMode (async — poll with `mcp__UnityMCP__get_test_job`, `wait_timeout: 60`). Counts must equal the Task-1 baseline. Any delta = stop and investigate, do not rationalize.

### Commit message template

```
refactor(net): resolve NetworkManager via instance instead of Singleton

Replace NetworkManager.Singleton with the inherited NetworkBehaviour.NetworkManager
property (NetworkBehaviours) and the injected gameManager.NetworkManager (GameStates),
and add an optional NetworkManager parameter to NetworkBehaviourReferenceWrapper.TryGet.
The property falls back to Singleton when the object has no owner, so behavior is
identical with a single NetworkManager; this is groundwork for the in-process
two-client fixture (story 5.0, issue #50) where a second NetworkManager must not
resolve into the host's context.
```

No `UX:` line (pure infra). NEVER add `Co-Authored-By` or any AI attribution.

### Project Structure Notes

- All edits in existing files; no new files, no new asmdef, no `.meta` churn.
- Branch: `epic5-network` (never `dev-refactor`, never `Dev` directly).

### Project Context Rules (extracted from project-context.md)

- Server authority strict; this story must not move any state mutation.
- `GetSafeRpcTarget(clientId)` / `IsLocalOrSimulated(clientId)` / `clientId >= 100` bot gateway: not touched by this story — if an edit you're about to make would touch them, you are off-script, stop.
- After any code change: `read_console` before assuming success (silent killer rule).
- Commits: English, conventional, body always, no AI attribution (surfaced to Discord).
- Async = UniTask only; audio = FMOD only (not relevant here, but do not introduce either).

### References

- [Source: _bmad-output/refactor-architecture-desingleton.md#4 — slice 1]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0a]
- [Source: GitHub issue #50]
- Pattern precedent: `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:68`

## Dev Agent Record

### Agent Model Used

claude-fable-5 (Claude Code)

### Debug Log References

- Baseline (untouched branch): EditMode 155/155 pass (job e025fa39), PlayMode 138/138 pass (job 5ca0a453).
- Gate after Task 2 batch: refresh + read_console(error) → 0 errors.
- Gate after Task 3 batch: refresh + read_console(error) → 0 errors; grep confirms zero `NetworkManager.Singleton` left in `GameLogic/GameStates/`.
- Final gate: refresh + read_console(error) → 0 errors; EditMode 155/155 (job f330f095), PlayMode 138/138 (job 649215c1) — counts identical to baseline.

### Completion Notes List

- All 14 mechanical sites replaced exactly as specified: 4 NetworkBehaviour sites (inherited `NetworkManager` property), 9 GameState sites (`gameManager.NetworkManager`), 1 wrapper signature change.
- `NetworkBehaviourReferenceWrapper.TryGet` gained optional `NetworkManager _networkManager = null` parameter; falls back to `Singleton` when null. No caller migrated (by design — parameter is for 5.0c/5.0d/fixture). `NetworkSerialize`/`Equals`/`GetHashCode` untouched (wire format frozen).
- Scope fence respected: MainMenu/Lobby UI, transports, dev scripts, `PowerManager` (MonoBehaviour), `NetworkAction.cs` (story 5.0b) all untouched — `git diff --stat` shows exactly the 7 in-scope files.
- No `IsServer` shorthand used anywhere (Trap 1); GameState base asserts left alone.
- No new tests authored: story is a zero-observable-change mechanical refactor; AC5's gate is the full existing suite at baseline counts, which is the specified verification (red-green not applicable — no behavior delta to pin).
- Commit `e2049fd` on `epic5-network`, conventional format, body explains the why, no AI attribution, no `UX:` line (pure infra).

### File List

- Assets/Scripts/GameLogic/GameManager.cs (modified)
- Assets/Scripts/Characters/CharacterManager.cs (modified)
- Assets/Scripts/Characters/Powers/PVisionOfTheImpossible.cs (modified)
- Assets/Scripts/GameLogic/GameStates/LobbyState.cs (modified)
- Assets/Scripts/GameLogic/GameStates/AwakeningState.cs (modified)
- Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs (modified)
- Assets/Scripts/Network/NetworkBehaviourReferenceWrapper.cs (modified)

## Change Log

- 2026-06-11: Story implemented — 14 `NetworkManager.Singleton` reads migrated to instance resolution + optional manager param on `TryGet`. Full suite green at baseline (155 EM + 138 PM). Commit `e2049fd`. Status → review.
