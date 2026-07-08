# Investigation: Vote-skip button does nothing during VoteState

## Hand-off Brief

1. **What happened.** The vote-skip button (`SkipVoteButton` inside the `VoteStateUI` prefab) fires its click and sends a server message, but the server-side listener that would call `VoteState.OnVoteSkipButtonPressed()` is attached only in `VoteStateUI.OnNetworkSpawn()`, which never runs because `VoteStateUI` is a `NetworkBehaviour` on a prefab with no `NetworkObject` (plain-`Instantiate`d, never spawned) — so the skip message dead-ends on the server with no effect and no error (host + client). **Confirmed.**
2. **Where the case stands.** Root cause Confirmed (code-traced + git-corroborated). The "regression" premise is **Refuted** — git shows no commit that unwired a working vote-skip; it has been architecturally broken across all visible history. Same dead-`OnNetworkSpawn`-hook class as the already-fixed `AwakeningStateUI`.
3. **What's needed next.** Design-owned confirmation of skip semantics, then a small fix: attach the server listener in a hook that actually runs (or dispatch skip via `GameManager.DoStateMethodRpc` like player-votes). → `gds-quick-dev`.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | N/A |
| Date opened      | 2026-07-08 |
| Status           | Concluded (root cause Confirmed; fix deferred, design-owned) |
| System           | Unity 6000.2.6f2, NGO, Facepunch/Steam; seen in real MP playtest |
| Evidence sources | Source code, `VoteStateUI.prefab`, `GameScene.unity`, git history. NO runtime logs (owner reports no errors) |

## Problem Statement

Owner (Poyo): during voting, pressing the skip button does **nothing** — no visible effect, no console error, host **and** client, spam-click doesn't help. Voting a specific player **works**. Owner believed vote-skip **worked in a previous build** (regression). The regression premise was tested and **refuted** by git evidence.

## Evidence Inventory

| Source | Status | Notes |
| ------ | ------ | ----- |
| `VoteStateUI.cs` / `VoteState.cs` | Available | vote-skip logic + the OnNetworkSpawn listener attach |
| `Assets/Prefabs/StateUI/VoteStateUI.prefab` | Available | `SkipVoteButton` UnityEvent → `OnVoteSkipButtonPressed` (the wiring the scene grep missed) |
| `GameState.cs` | Available | StateUIs are plain `Instantiate`d, never spawned |
| `Board/UI/SkipButton.cs` | Available | separate awakening/dodo skip (red herring) |
| git history | Available | archaeology done — no unwiring commit |
| Runtime console | Missing | no logs captured; F4 is Deduced, not observed |

## Timeline of Events

| Time | Event | Source | Confidence |
| ---- | ----- | ------ | ---------- |
| `6274b98` | "Adding skip vote button" — server dispatch via `[Rpc(SendTo.Server)]` (needs a spawned NetworkObject) | git | Confirmed |
| `66ceade`/`1ade267` (Dec 2025) | server dispatch via `RegisterNamedMessageHandler` inside `OnNetworkSpawn` | git | Confirmed |
| `d213d64` (Dec 25 2025) | current form: `NetworkAction` field-init registers handler; listener attach left in `OnNetworkSpawn` | git | Confirmed |
| `9daace2` (Jun 13 2026) | refactor changed only the client-send local-id source; root defect unchanged | git | Confirmed |

Every implementation depended on `VoteStateUI`'s network identity, which never existed.

## Confirmed Findings

### Finding 1: Two unrelated "skip" buttons

**Evidence:** board `Board/UI/SkipButton.cs:29-32` → `SleepCharacterServerRpc()` (awakening/dodo, unconditional, no phase guard). The vote-skip is a **separate** button, `SkipVoteButton`, a child of `Assets/Prefabs/StateUI/VoteStateUI.prefab`.

**Detail:** The board SkipButton is a red herring — it has always done sleep. (Corrects an earlier working note that treated it as the vote-skip button.)

### Finding 2: The vote-skip UI IS wired (not dead code) — the earlier "0 callers" conclusion was wrong

**Evidence:** `Assets/Prefabs/StateUI/VoteStateUI.prefab:437` — `m_MethodName: OnVoteSkipButtonPressed`, `m_TargetAssemblyTypeName: UI.VoteStateUI`, `m_Mode: 1` (void), on the `SkipVoteButton`'s `CustomButton.onButtonClickedUnityEvent`. `CustomButton.cs:50-51` fires both `onButtonClicked` and `onButtonClickedUnityEvent` on click. The earlier grep looked only in `GameScene.unity` (0 hits) and missed the **prefab** wiring.

**Detail:** The click DOES reach `VoteStateUI.OnVoteSkipButtonPressed()`, which invokes the `NetworkAction` to the server.

### Finding 3 (ROOT CAUSE): the server listener is attached in a lifecycle hook that never fires

**Evidence:** `Assets/Scripts/UI/StateUI/VoteStateUI.cs:19-26` attaches `onVoteSkipButtonPressedByClient += OnVoteSkipButtonPressedServer` only inside `OnNetworkSpawn()` (IsServer). `OnVoteSkipButtonPressedServer` (`:28-31`) is the ONLY caller of `VoteState.OnVoteSkipButtonPressed()`. But `VoteStateUI` is a `NetworkBehaviour` on a prefab with **no `NetworkObject`** (`grep -c NetworkObject VoteStateUI.prefab` → 0; `git log -S NetworkObject -- …VoteStateUI.prefab` → empty), instantiated via plain `Instantiate` in `GameState.cs:83` (never `NetworkObject.Spawn`; `git log -S ".Spawn(" -- GameState.cs` → empty). Corroborated by the repo's own note `AwakeningStateUI.cs:36-38`: "these UIs are locally instantiated, not network-spawned".

**Detail:** `OnNetworkSpawn` never executes → the listener is never attached → the server's `NetworkAction.listeners` is empty → the skip message dead-ends. The `NetworkAction` field initializer (`VoteStateUI.cs:17`) still registers the named-message handler at Instantiate time (network is up → `Register()` succeeds, no error), so the message is sent and even re-broadcast by the server, but lands on zero listeners.

### Finding 4: Why no error, both host and client (Deduced)

**Based on:** F3 + `NetworkAction.cs:236-238,260-278`. Client `Invoke` sends; server re-broadcasts; empty listener list → silent dead-end. Host-clicks-self self-receives into the same empty list. No exception path. Matches the symptom (no effect, no error, both roles). *Deduced (code-traced, not runtime-observed).*

### Finding 5: Why voting a player works (different path)

**Evidence:** `VoteState.ActivateVoteUI()` (`VoteState.cs:258-271`, from `OnStartStateClient`) subscribes `_voteCanvas.onVoteButtonClicked` (a plain C# event on the board Card, `VoteCanvas.cs:28,119`); the handler dispatches via `gameManager.DoStateMethodRpc(...)` (`VoteState.cs:44-53`) — the GameManager's RPC hub on a properly-spawned NetworkObject. Zero dependency on `VoteStateUI`'s network identity.

## Hypothesized Paths

### Hypothesis 1: A refactor unwired a previously-working vote-skip (regression)

**Status:** **Refuted**

**Theory:** Something removed/re-pointed the vote-skip wiring.

**Resolution:** git archaeology found no such commit. The UI wiring is intact (F2); the defect is the server-side dead hook (F3), present in every implementation across visible history (`6274b98`→`d213d64`→`9daace2`), all depending on `VoteStateUI`'s non-existent network identity. `d213d64` shaped the current broken form but inherited, not introduced, the defect. The owner's "used to work" is unexplained by git — most likely a memory of the board sleep/dodo skip (which works) or a pre-repo build.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| Runtime server-side `listeners.Count` at skip time | Would upgrade F4 from Deduced to Confirmed | one-shot tagged log in `NetworkAction<ulong>.OnReceiveMessage` during a real MP vote (owner playtest) |
| A historical build where StateUIs were network-spawned | Would resurrect the regression hypothesis | none found in this repo's git log |

## Source Code Trace

| Element | Detail |
| ------- | ------ |
| Failure origin | `VoteStateUI.cs:19-26` — server listener attached in `OnNetworkSpawn`, which never fires |
| Trigger | Clicking `SkipVoteButton` during `VoteState` |
| Condition | `VoteStateUI` prefab has no `NetworkObject` + plain `Instantiate` (`GameState.cs:83`) → `OnNetworkSpawn` never runs |
| Related files | `VoteStateUI.cs`, `VoteState.cs`, `VoteStateUI.prefab`, `GameState.cs`, `NetworkAction.cs`, `CustomButton.cs`, `AwakeningStateUI.cs` (precedent fix) |

## Conclusion

**Confidence:** High.

Root cause is Confirmed: the vote-skip click is correctly wired at the UI layer (`VoteStateUI.prefab:437`) and reaches `VoteStateUI.OnVoteSkipButtonPressed()`, but the server-side handler that dispatches into `VoteState.OnVoteSkipButtonPressed()` is attached only in `VoteStateUI.OnNetworkSpawn()`, which never executes because `VoteStateUI` is a `NetworkBehaviour` on a `NetworkObject`-less prefab, plain-`Instantiate`d and never spawned. The message dead-ends server-side. Not a regression (git-refuted). Same dead-hook class as the already-fixed `AwakeningStateUI`. Independent of the player-leave stability chantier.

## Recommended Next Steps

### Fix direction (design-owned; not implemented)
- **Move the server listener attach off `OnNetworkSpawn`** to a hook that actually runs for locally-instantiated StateUIs (`SetupStateUI` / an `onStateStartServer` subscription), gated on `gameManager.IsServer` (the codebase's reliable server check, not `NetworkBehaviour.IsServer`); move teardown to `OnDestroy` (mirror `AwakeningStateUI.cs:34-48`). Note the current `OnNetworkDespawn`-only `Unregister()` also **leaks** the named-message handler each round — same dead-hook root.
- **Preferred:** dispatch vote-skip through `gameManager.DoStateMethodRpc(typeof(VoteState).FullName, nameof(VoteState.OnVoteSkipButtonPressed), server target)` exactly like `OnPlayerVoted` (`VoteState.cs:44-53`), dropping the `NetworkAction`/`OnNetworkSpawn` dependency entirely.
- **Owner decisions:** (i) confirm intended skip semantics/threshold before wiring; (ii) decide whether the board `SkipButton` (sleep/dodo) should be phase-guarded so it does nothing during `VoteState`.

### Diagnostic (optional, to reach 100%)
One-shot `[VOTE-SKIP]` log of `listeners.Count` in the server's `NetworkAction<ulong>.OnReceiveMessage` during a real MP vote → confirms the empty-listener dead-end.

## Reproduction Plan

Real MP match (Host + ≥1 client), reach VoteState, press the vote skip button. Expected (current, broken): nothing, no error, both roles. After fix: the local client's skip registers a SKIP_VOTE_ID vote (skip counter increments; vote can resolve to SKIP).

## Side Findings

- The board `SkipButton` (`SkipButton.cs:29-32`) calls `SleepCharacterServerRpc` with **no phase guard** — it is active/clickable regardless of state. (Confirmed; design question, not the bug.)
- Same dead-`OnNetworkSpawn` hook + `OnNetworkDespawn` teardown leak pattern was already found and fixed once in `AwakeningStateUI.cs:34-48` — a codebase-wide StateUI smell worth a sweep. (Confirmed.)
- Not caused by the player-leave stability chantier — that branch does not touch `VoteStateUI`/`SkipButton`/`GameState`/the prefab. (Confirmed.)
