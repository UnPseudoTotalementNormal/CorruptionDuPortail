# Story 5.0b: `NetworkAction` NetworkManager injection (the cross-wiring fix)

Status: review

## Story

As a developer,
I want `NetworkAction` to register/invoke/receive through an injected `NetworkManager` instead of 36 hardwired `Singleton` reads,
so that two in-process clients can no longer overwrite each other's named-message handlers (prerequisite of the 5.0 fixture, issue #50).

## Acceptance Criteria

1. All internal `NetworkManager.Singleton` reads in `Assets/Plugins/NetworkAction/NetworkAction.cs` route through one private resolved accessor per class.
2. The `NetworkBehaviour`-bound constructors derive the manager from `_networkBehaviour.NetworkManager`; the unbound (string-only) constructors keep the `Singleton` fallback — current behavior preserved verbatim.
3. The wire format is untouched: messageID derivation, truncation logic, `allowInvokeByClients` gating, serializer selection — byte-identical.
4. `GameManager`'s two field-initializer actions (`onGameStarted`, `onNewDayPassed`, `GameManager.cs:48-49`) are NOT rebound in this story (deferred — see Dev Notes "Why GameManager actions stay unbound").
5. Full EditMode + PlayMode suite passes at baseline counts; console clean.
6. One commit, conventional format, no AI attribution.

## Tasks / Subtasks

- [x] Task 1: Record test baseline (AC: 5) — same procedure as story 5.0a Task 1.
- [x] Task 2: Read `Assets/Plugins/NetworkAction/NetworkAction.asmdef` and confirm it references `Unity.Netcode.Runtime` (it must, the file already uses `NetworkManager`). No asmdef change expected.
- [x] Task 3: Migrate `NetworkAction` (non-generic, lines ~12-174) (AC: 1, 2)
- [x] Task 4: Migrate `NetworkAction<T>` (lines ~176-362) — identical edit
- [x] Task 5: Migrate `NetworkAction<T1,T2>` (lines ~364-555) — identical edit
- [x] Task 6: Migrate `NetworkAction<T1,T2,T3>` (lines ~557-756) — identical edit
- [x] Task 7: Gate + commit (AC: 5, 6) — same gate procedure as story 5.0a.

## Dev Notes

### The edit, exactly (apply to all 4 classes)

Each of the 4 classes (`NetworkAction`, `NetworkAction<T>`, `NetworkAction<T1,T2>`, `NetworkAction<T1,T2,T3>`) has the same structure: 2 ctors, `Invoke`, `OnReceiveMessage`, `Register`, `Unregister`. Per class:

1. Add one private field + accessor:

```csharp
private NetworkManager boundNetworkManager;

private NetworkManager Manager => boundNetworkManager != null ? boundNetworkManager : NetworkManager.Singleton;
```

2. In the `NetworkBehaviour`-bound ctor (the one taking `NetworkBehaviour _networkBehaviour`), after the `IsSpawned` guard and **before** `Register()`:

```csharp
boundNetworkManager = _networkBehaviour.NetworkManager;
```

(`_networkBehaviour.IsSpawned` is already required by the existing guard, so `NetworkManager` is valid at that point.)

3. Replace every `NetworkManager.Singleton` in `Invoke`, `OnReceiveMessage`, `Register`, `Unregister` with `Manager`. That includes the null-guards: `if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)` becomes `if (Manager == null || Manager.CustomMessagingManager == null)` — semantics identical for the unbound case, correct for the bound case.

4. `NetworkManager.ServerClientId` is a **static constant** — leave those reads as-is (e.g. `Invoke`'s `SendNamedMessage(messageID, NetworkManager.ServerClientId, _writer)`).

### What you must NOT change (wire format is frozen)

- messageID derivation: `_messageID + "_" + _networkBehaviour.NetworkObjectId + "_" + _networkBehaviour.NetworkBehaviourId` — character-identical.
- `MAX_MESSAGE_LENGTH` truncation (300) and its warning strings.
- `allowInvokeByClients` checks and the server-side rebroadcast pattern in `OnReceiveMessage` (including the "Early return to avoid invoking listeners twice on the server" branches).
- `FastBufferWriter` sizes (1 / 128 / 256 / 384) and the `Allocator.Temp` usage.
- The serializer factory and all serializer classes (lines ~758-948) — zero edits below the `#region Serialization System` line.
- The `Application.isPlaying` early-return in `NetworkAction<T1,T2,T3>`'s bound ctor (line ~586) — an asymmetry vs the other 3 classes; preserve it, do not "fix" it.

### Why GameManager actions stay unbound (AC: 4)

`GameManager.onGameStarted` / `onNewDayPassed` are **field initializers** (`GameManager.cs:48-49`): they run at `Awake`-time with global string IDs and register on `Singleton.CustomMessagingManager` (or warn-and-skip if no NM yet — see `Register()`'s null-guard). Binding them to the owning NM would require moving creation to `OnNetworkSpawn`, which changes *when* registration happens relative to the first `Invoke` and to listener attachment — a real timing change in network-critical code. The epics AC allows rebinding only behind a characterization proving identical timing; the architect's decision is to **defer** that question to story 5.0e, where the coexistence probe will show whether the unbound fallback actually breaks the fixture (the second client's GameManager actions registering on the primary NM). Do not touch `GameManager.cs` in this story.

### Who already uses the bound ctor (your behavior oracle)

`PersonalBeaconObject.cs:21` — `new NetworkAction<bool>($"onCorruptedBeaconChanged_{ownerClientId}_{targetClientId}", _ownerPower)`. After your edit this action resolves through `_ownerPower.NetworkManager` — in production identical to Singleton. If any PlayMode test touching beacons (`PPersonalBeacons`) changes result, you broke step 2 or 3.

### Plugin asmdef caution

`NetworkAction.cs` lives in its own assembly (`Assets/Plugins/NetworkAction/NetworkAction.asmdef`). It cannot reference `Game` types — your edit only uses `Unity.Netcode` types, so this is fine. Do not add references to the asmdef. Do not move the file.

### Gate procedure

Identical to story 5.0a: `refresh_unity` (compile=request) → `read_console` errors empty → full EM + PM at baseline. Pay extra attention to PlayMode: NetworkActions are exercised by the host+bot harness (`NetworkTestHelper`), so a regression here shows up as PlayMode failures or console warnings `"Client attempted to invoke NetworkAction..."` / `"...is already registered"` that were not there before. Compare warning noise, not just pass counts: run `read_console` with `types: ["warning"]`, `filter_text: "NetworkAction"` before and after.

### Commit message template

```
refactor(net): route NetworkAction through an injected NetworkManager

Add a bound-NetworkManager field to all four NetworkAction variants, derived from
the NetworkBehaviour-bound constructor; string-only constructors keep the Singleton
fallback so unbound global actions behave exactly as before. Wire format (messageID
derivation, truncation, client-invoke gating, rebroadcast) is untouched. Groundwork
for the in-process two-client fixture (story 5.0, issue #50): without this, both
clients' actions register the same messageID on the same CustomMessagingManager and
silently overwrite each other's handlers.
```

No `UX:` line. No AI attribution.

### Project Structure Notes

- Single file edited: `Assets/Plugins/NetworkAction/NetworkAction.cs`.
- Prerequisite: story 5.0a merged (not technically coupled, but keeps the gate baseline meaningful).

### Project Context Rules (extracted from project-context.md)

- No magic-string changes — messageIDs are wire-format (silent-runtime-miss rule).
- After any code change: `read_console` before assuming success.
- Commits: English, conventional, body always, no AI attribution.
- Never edit `.csproj`/`.sln`; the asmdef is the unit (and here: read-only).

### References

- [Source: _bmad-output/refactor-architecture-desingleton.md#1 — NetworkAction cross-wiring; #3 open question 2; #4 slice 2]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0b]
- Current code: `Assets/Plugins/NetworkAction/NetworkAction.cs` (949 lines, 4 mirrored classes + serializers)
- Bound-ctor consumer precedent: `Assets/Scripts/Characters/Powers/PowerObjects/PersonalBeaconObject.cs:21`

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Baseline (pre-edit, HEAD e2049fd): EditMode 155/155, PlayMode 138/138; `read_console` warnings filtered `NetworkAction` = 0.
- Post-edit: compile errors = 0; EditMode 155/155; PlayMode 138/138; `NetworkAction` warning noise = 0 (no new `"is already registered"` / `"Client attempted to invoke..."`).

### Completion Notes List

- Mechanical migration, wire format frozen. Per class (4 variants): added `private NetworkManager boundNetworkManager;` + `private NetworkManager Manager => boundNetworkManager != null ? boundNetworkManager : NetworkManager.Singleton;`.
- Bound (NetworkBehaviour) ctor sets `boundNetworkManager = _networkBehaviour.NetworkManager;` right after the `IsSpawned` guard, before `Register()` — manager valid because guard already requires spawn.
- Replaced all 44 `NetworkManager.Singleton` reads in `Invoke` / `OnReceiveMessage` / `Register` / `Unregister` with `Manager`. `NetworkManager.ServerClientId` (static const, 8 reads) left untouched.
- AC4 honoured: `GameManager.cs` not touched; `onGameStarted` / `onNewDayPassed` stay on the unbound `Singleton` fallback (deferred to 5.0e).
- Preserved the `NetworkAction<T1,T2,T3>` bound-ctor `if (!Application.isPlaying) return;` asymmetry (line ~605) verbatim.
- Static grep proof: `NetworkManager.Singleton` = 4 (accessors only), `NetworkManager.ServerClientId` = 8, binding line = 4.
- `NetworkAction.asmdef` unchanged (already references `Unity.Netcode.Runtime`).

### File List

- `Assets/Plugins/NetworkAction/NetworkAction.cs` (modified)
