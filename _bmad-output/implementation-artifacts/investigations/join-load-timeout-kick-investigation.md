# Investigation: Joining client kicked during a slow GameScene load ("heartbeat too short")

## Hand-off Brief

1. **What happened.** A joining client on a slow PC is torn down before it finishes loading `GameScene`; the reporter attributes it to the liveness heartbeat window (5 s) being too short.
2. **Where the case stands.** **Root cause CONFIRMED and it is NOT the heartbeat.** The binding limit is the client-side `ConnectTimeoutSeconds = 10 s` wait in `MainMenu.WaitForClientConnectedOrTimeout`, which is gated on FULL NGO scene synchronization (GameScene load + every NetworkObject spawn). When that sync exceeds 10 s the client throws "Connection timed out" and calls `NetworkManager.Shutdown()` on itself. The liveness server never ticks a still-loading client (enrollment happens post-sync).
3. **What's needed next.** Decouple the connect deadline from the scene-sync phase (short timeout for approval, long/watchdog timeout for sync) or raise `ConnectTimeoutSeconds`; also confirm transport `DisconnectTimeoutMS = 12000` stays above worst-case load. Route to `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-13                                                                 |
| Status           | Concluded (root cause Confirmed; fix not yet implemented)                  |
| System           | Unity 6000.2.6f2, NGO 2.12.0, branch `Dev`. Slow-PC client join.           |
| Evidence sources | Source code (project + NGO package cache), BootScene transport config      |

## Problem Statement

Reporter (Poyo): heartbeat timeout for players doesn't leave enough time to connect. A scene load can take a long time on slow PCs; the game freezes, the heartbeat can't be sent, and the player is kicked before finishing the load — impossible to join.

**Premise challenged:** the "heartbeat" is not the mechanism that kicks a *loading* client (see Finding 3 + Deduction 1). The freeze-blocks-connect intuition is correct; the specific timer is different.

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| Liveness layer (Domain + Network/Liveness) | Available | Config, tracker, pump, bridge, service all read |
| MainMenu join flow | Available | `WaitForClientConnectedOrTimeout`, `SwitchToGameScene` |
| NGO 2.12.0 package source | Available | Connection + SceneManagement invocation points |
| BootScene transport config | Available | UnityTransport timeouts |
| Runtime logs of an actual kick | Missing | Would timestamp which timer fired; see Missing Evidence |

## Timeline of Events (a single join on a slow client)

| Time (approx) | Event | Source | Confidence |
| --- | --- | --- | --- |
| t0 | `StartClient()`; transport connects, `ConnectionApproved` received | MainMenu.cs:267 / NGO | Confirmed |
| t0 | Client begins NGO synchronization → loads `GameScene` (Single) | NGO NetworkSceneManager | Confirmed |
| t0..tLoad | Client main thread hitches/freezes loading GameScene (FMOD banks, avatars, NetworkObject spawns) | Deduced | Deduced |
| t0+10s | `CancellationTokenSource(10s)` fires while sync still incomplete | MainMenu.cs:222 | Confirmed |
| after freeze | `WaitUntil` returns canceled → `WaitForClientConnectedOrTimeout` returns false → throw → `NetworkManager.Shutdown()` | MainMenu.cs:160-182 | Confirmed |
| tLoad (>10s) | `IsConnectedClient=true` would have been set — too late | NetworkSceneManager.cs:2367 | Confirmed |

## Confirmed Findings

### Finding 1: Liveness detection window is 5 s; transport disconnect is 12 s
`LivenessConfig.Default = (5.0s, 1.0s)` ⇒ threshold 5 beats (`Assets/Scripts/Domain/LivenessConfig.cs:26`). BootScene UnityTransport `m_DisconnectTimeoutMS: 12000` (`Assets/Scenes/BootScene.unity:578`). So among the network-layer timers the liveness window is the fastest — this is why the heartbeat is the natural suspect.

### Finding 2: The server enrolls a joining client only AFTER full scene sync
Scene management is enabled (host drives `NetworkManager.SceneManager.LoadScene("GameScene")`, `Assets/Scripts/UI/MainMenu.cs:471`). With scene management enabled, NGO invokes the server's `OnClientConnectedCallback` only on receipt of the client's `SynchronizeComplete` — `Library/PackageCache/com.unity.netcode.gameobjects@aaabf07f880c/Runtime/SceneManagement/NetworkSceneManager.cs:2511` (the pre-sync path at `NetworkConnectionManager.cs:1058` runs only when `!EnableSceneManagement`). `LivenessService` enrolls exactly on that callback (`Assets/Scripts/Network/Liveness/LivenessService.cs:120` → `EnrollRealClient`).

### Finding 3: The client's `IsConnectedClient` flips true only at the END of scene sync
`NetworkManager.IsConnectedClient = true` for the scene-management path is set at `NetworkSceneManager.cs:2367`, reached only after the client has loaded all server scenes and spawned all NetworkObjects (lines 2340-2367), immediately before it sends `SynchronizeComplete`. The `ConnectionApprovedMessage` sets `IsConnectedClient` early ONLY in the `!EnableSceneManagement` branch (`ConnectionApprovedMessage.cs:319-326`) — not our case.

### Finding 4: MainMenu bounds the whole connect+sync in a 10 s wall-clock deadline
`ConnectTimeoutSeconds = 10f` (`Assets/Scripts/UI/MainMenu.cs:58`). `WaitForClientConnectedOrTimeout` uses `new CancellationTokenSource(TimeSpan.FromSeconds(10))` (a real-time timer, fires even while the main thread is frozen) and `UniTask.WaitUntil(() => NM==null || !IsListening || IsConnectedClient)` (`MainMenu.cs:222-229`). On cancel it returns false (`:233`); the caller throws and runs teardown `NetworkManager.Shutdown()` + `LeaveLobby()` (`MainMenu.cs:160-188`).

### Finding 5: The liveness pump has a stall-guard — but it only protects the LOCAL starved pump
`LivenessPumpPolicy.ShouldSkipTick(elapsed, beatPeriod)` skips a tick when the pump itself overslept > 2× the beat period (`Assets/Scripts/Domain/LivenessPumpPolicy.cs:29-37`), consumed by `LivenessNetworkPump.RunAsync` (`Assets/Scripts/Network/Liveness/LivenessNetworkPump.cs:110`). This prevents a frozen client from false-declaring the host. It does NOT protect a peer whose *remote* counterpart froze — but per Finding 2 that scenario never arises during load, because the frozen client isn't enrolled yet.

## Deduced Conclusions

### Deduction 1: The heartbeat layer cannot kick a still-loading joiner
**Based on:** Findings 2, 3, 5.
**Reasoning:** The joining client is enrolled server-side only at `SynchronizeComplete` (post-load), so the server tracker never ticks its miss counter during the load. The client-role pump starts only after `CompositionRoot` (scene-placed in GameScene) awakes — also post-load — and is stall-guarded. Neither direction can fire during the load window.
**Conclusion:** The reporter's "heartbeat too short" is Refuted as the mechanism for the *loading* kick.

### Deduction 2: The 10 s connect deadline is the binding limit
**Based on:** Findings 3, 4.
**Reasoning:** `IsConnectedClient` cannot become true until GameScene sync finishes. The 10 s CTS is wall-clock and fires regardless of the freeze. If sync wall-clock > 10 s, the wait cancels and MainMenu shuts the client down — before the load completes. 10 s < worst-case slow-PC GameScene sync is entirely plausible.
**Conclusion:** Slow-load joins fail because the client times out its own connect attempt and tears itself down. This is the root cause. Confidence High.

## Hypothesized Paths

### Hypothesis 1: Client-role liveness false-fires host-lost right after a successful (near-boundary) connect
**Status:** Open (low priority)
**Theory:** If load completes just under 10 s but the first post-load frames are still asset-streaming hitchy, the freshly started client pump could miss keepalives.
**Would confirm:** A `[LIVENESS] client declared the host lost` log within ~5 s of a successful join.
**Would refute:** Stall-guard skips oversized ticks and keepalives resume at 1 Hz post-load — expected to hold.
**Resolution:** Unresolved; needs runtime logs. Secondary to Deduction 2.

### Hypothesis 2: Transport `DisconnectTimeoutMS = 12000` also drops the socket on very long loads
**Status:** Open (secondary)
**Theory:** A load whose contiguous freeze exceeds 12 s trips the transport socket timeout independently.
**Would confirm:** Loads measured > 12 s wall-clock; a transport-level disconnect reason.
**Would refute:** Loads stay < 12 s (then only the 10 s app timer matters).
**Resolution:** Even if true, the 10 s app timer fires first — so raising only the transport timeout would not fix the symptom.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | --- | --- |
| Runtime log of an actual kick | Confirms *which* timer fired (app 10 s vs transport 12 s vs liveness) | Repro on a slow client; capture Player.log; look for "Connection timed out" (MainMenu) vs `[LIVENESS]` vs transport disconnect reason |
| Measured GameScene sync wall-clock on the slow PC | Quantifies how far past 10 s loads go → sizes the new timeout | Stopwatch from `StartClient` to `OnSynchronizeComplete` |

## Source Code Trace

| Element | Detail |
| --- | --- |
| Error origin | `Assets/Scripts/UI/MainMenu.cs:160` (`WaitForClientConnectedOrTimeout` returns false) → throw at `:169` → `Shutdown()` at `:182` |
| Trigger | Joining client whose NGO GameScene synchronization wall-clock exceeds `ConnectTimeoutSeconds = 10 s` |
| Condition | Scene management enabled ⇒ `IsConnectedClient` gated on `SynchronizeComplete` (`NetworkSceneManager.cs:2367`); slow-PC load > 10 s |
| Related files | `MainMenu.cs` (timeout + teardown); `NetworkSceneManager.cs` (2367 client flag, 2511 server enroll); `LivenessService.cs`/`LivenessNetworkPump.cs` (ruled out); `BootScene.unity:578` (transport 12 s) |

## Conclusion

**Confidence:** High.

Confirmed root cause: the client-side connect deadline `ConnectTimeoutSeconds = 10 s` in `MainMenu.WaitForClientConnectedOrTimeout` is coupled to full NGO scene synchronization (GameScene load + all NetworkObject spawns), which on a slow PC can exceed 10 s. When it does, the client cancels its own wait, throws "Connection timed out", and calls `NetworkManager.Shutdown()` — kicking itself before the load finishes. The liveness heartbeat window (5 s) is Refuted as the cause: the server enrolls a joining client only at `SynchronizeComplete` (post-load), so it never ticks a still-loading peer, and the client pump is both post-load and stall-guarded. Transport `DisconnectTimeoutMS = 12 s` is a secondary, slower limit that the 10 s app timer pre-empts.

## Recommended Next Steps

### Fix direction
Decouple the connect deadline from the scene-load phase (mechanism: timeout scope, not timeout value):
1. **Preferred:** bound only the *approval/handshake* phase with a short timeout, then wait on `NetworkManager.SceneManager.OnSynchronizeComplete` (or `OnClientConnectedCallback` client-side) with a much longer deadline or a progress watchdog. This keeps fast failure on a dead host while allowing arbitrarily slow honest loads.
2. **Minimal:** raise `ConnectTimeoutSeconds` (e.g. 45-60 s) sized from the measured worst-case slow-PC load (Missing Evidence row 2).
3. **Guard the transport too:** ensure `DisconnectTimeoutMS` (currently 12000) comfortably exceeds the worst-case *contiguous* load freeze, else a very long single freeze drops the socket regardless of the app timer.
4. Do NOT change `LivenessConfig` for this symptom — it is not the cause. (A separate, real concern: the liveness `TimeoutSeconds`/threshold is currently hardcoded `Default`; unrelated to this kick.)

### Diagnostic
Add a tagged log around the connect wait: on timeout log elapsed + `IsListening`/`IsConnectedClient`/`SceneManager` progress; subscribe once to `OnSynchronizeComplete` to timestamp real sync completion. Repro on the slow PC and read Player.log to confirm "Connection timed out" fires (app path) rather than a `[LIVENESS]` or transport disconnect.

## Reproduction Plan

- Setup: host on a fast machine; join from a slow PC (or artificially delay GameScene: heavy synchronous load / disabled asset cache) so sync wall-clock > 10 s.
- Trigger: click Join.
- Expected (current bug): client freezes loading, then at 10 s MainMenu logs "Connection timed out — the host did not respond." / DisconnectReason and returns to menu before the load finishes.
- Expected (after fix): client completes the load and seats in the lobby regardless of load duration, while a dead host still fails fast.

## Side Findings

- `LivenessConfig` timeout/period are hardcoded to `Default` in `CompositionRoot` (`Assets/Scripts/GameLogic/CompositionRoot.cs:210`), not authored via ScriptableObject — arch doc §8.1 anticipated a B2 SO front-end that was not built. Not this bug, but relevant if the liveness window is ever tuned.
- The client path calls `SwitchToGameScene()` (`MainMenu.cs:173`) after connect; on a client `NetworkManager.SceneManager.LoadScene` is a server-only op and is effectively redundant with the server-driven sync — worth a glance during the fix but not causal here.
