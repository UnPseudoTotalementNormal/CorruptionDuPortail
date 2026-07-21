# Investigation: VPN → instant disconnect from a game

## Hand-off Brief

1. **What happened.** Player feedback: "On est instantanément déco d'une game si on a un VPN." Unverified user
   claim; not yet reproduced, no log captured.
2. **Where the case stands.** The networking stack is mapped (Confirmed): Unity Relay over **DTLS/UDP** transport
   + UGS Lobby, plus a custom 5-second liveness/heartbeat eviction layer that is **2.4× stricter than the
   transport's own 12 s disconnect timeout**. Two competing mechanisms fit the symptom depending on WHEN the drop
   happens (at join vs mid-game) — that discrimination is the missing evidence.
3. **What's needed next.** One reproduction with the client `Player.log` captured (VPN on), plus the answer to
   "does it fail at join, or after being seated in the game?".

## Case Info

| Field            | Value                                                                                        |
| ---------------- | -------------------------------------------------------------------------------------------- |
| Ticket           | N/A (verbal playtest feedback)                                                                |
| Date opened      | 2026-07-21                                                                                    |
| Status           | Active — evidence-light                                                                       |
| System           | Unity 6000.2.6f2, NGO, UnityTransport (`m_ProtocolType: 1` = Relay), UGS Lobby, Windows client |
| Evidence sources | Source code (Confirmed), `Assets/Scenes/BootScene.unity` transport config (Confirmed). No logs. |

## Problem Statement

Verbatim (reporter, via Poyo): *"On est instantanément déco d'une game si on a un VPN"*.

Treated as a hypothesis. Two unstated variables are load-bearing and are NOT established:
- **When**: at join (never gets in) vs mid-game (was in, got kicked out)?
- **Who**: the VPN is on the *client*, on the *host*, or both?

## Evidence Inventory

| Source                                | Status    | Notes                                                                 |
| ------------------------------------- | --------- | --------------------------------------------------------------------- |
| Source code (network stack)           | Available | Fully readable, cited below                                           |
| BootScene NetworkManager/UTP settings | Available | `BootScene.unity:570-609`                                             |
| Client `Player.log` with VPN on       | **Missing** | The decisive artifact — would name the exact kick path              |
| Repro (VPN vendor, protocol, split-tunnel) | **Missing** | VPN behaviour differs wildly (UDP block vs route flap)          |
| Timing ("instant" = seconds?)         | **Missing** | 5 s / 10 s / 12 s deadlines are distinguishable if timed             |
| Issue tracker / Discord thread        | Not checked | No task thread opened for this yet                                   |

## Investigation Backlog

| # | Path to Explore                                                              | Priority | Status | Notes |
| - | ---------------------------------------------------------------------------- | -------- | ------ | ----- |
| 1 | Get one repro + `Player.log` (VPN on) and grep for `[LIVENESS]` / `[LEAVE][PHASE3]` / `RelayServiceException` | High | Open | Each tag points at a different root cause |
| 2 | Determine join-time vs mid-game drop                                          | High     | Open   | Splits H1 from H2/H3 |
| 3 | Check whether UDP/DTLS is reachable through the reporter's VPN                 | High     | Open   | Relay is `dtls` only — no `wss`/TCP fallback in code |
| 4 | Measure real RTT/loss under that VPN vs the 5 s liveness window               | Medium   | Open   | 5 consecutive lost beats = eviction |
| 5 | Host-side-VPN variant: Relay allocation region is picked from the HOST's IP    | Medium   | Open   | Would degrade the whole lobby, not just one player |
| 6 | UGS Lobby polling/heartbeat failure path (`OnLobbyError` → notification only)  | Low      | Open   | Surfaces a message; not currently known to kick |

## Confirmed Findings

### Finding 1: Prod transport = Unity Relay over DTLS/UDP, no TCP/WebSocket fallback

**Evidence:** `Assets/Scenes/BootScene.unity:570` (`m_ProtocolType: 1` = Relay Unity Transport);
`Assets/Scripts/UI/MainMenu.cs:258-260` and `:410-412`, `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:468-470`
— all three call sites use `_allocation.ToRelayServerData("dtls")`.

**Detail:** Every host-create and client-join path binds the Relay connection to **DTLS over UDP**. There is no
`"wss"` (WebSocket/TCP 443) fallback anywhere in the codebase. A VPN that blocks, throttles or NATs UDP badly
kills the session outright with no alternative path. Steam/Facepunch transport exists in the repo
(`Assets/Scripts/Facepunch/FacepunchTransport.cs`) but is NOT the wired transport in BootScene.

### Finding 2: Transport-level timeouts

**Evidence:** `Assets/Scenes/BootScene.unity:575-578`, `:608-609`.

**Detail:** `m_HeartbeatTimeoutMS: 500`, `m_ConnectTimeoutMS: 1000`, `m_MaxConnectAttempts: 60`,
`m_DisconnectTimeoutMS: 12000`, `ClientConnectionBufferTimeout: 10`, `ConnectionApproval: 1`. So UTP itself
tolerates **12 s** of silence before declaring a disconnect, and retries a connect for up to 60 s.

### Finding 3: A custom liveness layer evicts after 5 s of silence — stricter than the transport

**Evidence:** `Assets/Scripts/Domain/LivenessConfig.cs:26` (`new LivenessConfig(5.0, 1.0)` — 5 s window at 1 Hz);
`Assets/Scripts/Network/Liveness/LivenessService.cs:218-230` (`PeerLost` → server `HandlePlayerLeft` /
client `ClientDisconnectHandler` host-loss); `Assets/Scripts/Network/ClientDisconnectHandler.cs:133-139, 274-288`
(host loss ⇒ notification "Connexion à l'hôte perdue" + forced `SceneManager.LoadScene(MainMenu)`).

**Detail:** Both directions run it. Server side: 5 missed client heartbeats ⇒ that client is routed into the
**leave pipeline** — and per the project's disconnect policy a mid-game leave is an instant CHAIN, i.e.
irreversible. Client side: 5 missed host keepalives ⇒ forced return to the main menu. **5 s < 12 s**, so the
liveness layer will always fire *before* the transport would have given up — including for connections the
transport considers perfectly healthy.

### Finding 4: The liveness stall-guard only protects against LOCAL stalls, not network stalls

**Evidence:** `Assets/Scripts/Domain/LivenessPumpPolicy.cs:29-37`;
`Assets/Scripts/Network/Liveness/LivenessNetworkPump.cs:90-113`.

**Detail:** `ShouldSkipTick` compares *local wall-clock elapsed since the pump's previous wake* against 2× the
beat period. It suppresses the tick when the **process itself** was starved (GC, scene load, `timeScale=0`,
alt-tab). It has **no notion of network conditions**: a route change, VPN reconnect, or 5 s of packet loss ticks
the miss counter five times at full rate and evicts the peer. Reliable RPC delivery does not save it —
retransmitted beats arriving late still miss their windows.

### Finding 5: Join-time deadline is 10 s until synchronization starts

**Evidence:** `Assets/Scripts/Network/JoinHandshake.cs:31-32` (`ApprovalTimeoutSeconds = 10f`,
`SyncTotalTimeoutSeconds = 90f`); decision in `Assets/Scripts/Domain/ConnectHandshakePolicy.cs:102-105`.

**Detail:** If the DTLS/Relay handshake never completes, the client fails with `ApprovalTimeout` after 10 s and is
returned to the menu with a generic message. From a player's seat that reads as "instantly kicked".

## Deduced Conclusions

### Deduction 1: The symptom has two distinct candidate mechanisms, separated by timing

**Based on:** Findings 1, 3, 4, 5.

**Reasoning:** If the VPN blocks/mangles UDP, the DTLS handshake never completes and the failure is at **join**
(≈10 s, Finding 5). If the VPN merely adds jitter/latency or flaps its tunnel, the connection establishes and the
kill comes from the **5 s liveness window** mid-game (Findings 3+4), long before UTP's own 12 s tolerance.

**Conclusion:** "Instant" is doing a lot of work in the report. Two different fixes. Establishing join-vs-mid-game
resolves which.

### Deduction 2: The liveness window is the tightest failure surface in the whole stack

**Based on:** Findings 2, 3, 4.

**Reasoning:** 5 s eviction vs 12 s transport tolerance vs 60 s connect retries. The custom layer is strictly the
first to fire, and it is the only one with no allowance for adverse network conditions.

**Conclusion:** Any network-degrading condition (VPN, mobile hotspot, congested Wi-Fi, ISP hiccup) hits the
liveness layer first. VPN is likely just the most reproducible instance of a broader class.

## Hypothesized Paths

### Hypothesis 1: The VPN blocks/mangles UDP, so the Relay DTLS handshake never completes

**Status:** Open

**Theory:** Relay is bound to `"dtls"` (UDP) with no `wss`/TCP fallback (Finding 1). Many consumer VPNs
(especially corporate/DoH-forcing, or those with aggressive split-tunnel and NAT) drop or rewrite UDP. The client
never gets approved; `JoinHandshake` fails at 10 s with `ApprovalTimeout`.

**Supporting indicators:** Finding 1 (dtls-only), Finding 5 (10 s deadline reads as "instant").

**Would confirm:** `Player.log` shows the join failing before any `[LIVENESS]` line, with a
`ConnectFailReason.ApprovalTimeout`, and no `OnSynchronize`. Or: the same VPN with UDP explicitly allowed works.

**Would refute:** The player reports being *in* the game (seeing the lobby/board) before being kicked.

**Resolution:** —

### Hypothesis 2: The 5 s liveness window evicts a VPN-jittered but healthy connection

**Status:** Open

**Theory:** The tunnel establishes; then a VPN reconnect / route change / burst loss produces >5 s without
arriving beats, and `LivenessTracker` declares the peer lost — despite UTP still holding the connection open
(12 s tolerance, Finding 2). Server side that is a permanent CHAIN-out; client side it is a forced return to menu
with "Connexion à l'hôte perdue".

**Supporting indicators:** Findings 3+4 — the stall-guard covers only local starvation, never the wire.

**Would confirm:** `Player.log` (or the host's) contains `[LIVENESS] client declared the host lost` /
`[LIVENESS] server declared client N lost` at the moment of the drop, while no transport disconnect preceded it.

**Would refute:** Drop happens with no `[LIVENESS]` line, or a transport-level disconnect is logged first.

**Resolution:** —

### Hypothesis 3: The VPN is on the HOST and degrades the Relay allocation region for everyone

**Status:** Open

**Theory:** `RelayService.Instance.CreateAllocationAsync(maxConnections)`
(`Assets/Scripts/UI/MainMenu.cs:410`) is called with **no explicit region**, so UGS picks the region nearest the
host's apparent IP. A host behind a VPN exiting in another country pins the whole match to a distant relay,
inflating every client's RTT and making the 5 s liveness window much easier to blow.

**Supporting indicators:** No region argument at any allocation call site; H2's mechanism amplified.

**Would confirm:** The reporter was the host, and/or all players (not just the VPN user) reported drops in the
same session.

**Would refute:** The VPN user was a plain client and the rest of the lobby was unaffected.

**Resolution:** —

### Hypothesis 4: UGS Lobby (HTTPS) failure under VPN causes the kick

**Status:** Open — low prior

**Theory:** UGS Lobby heartbeat/polling calls fail under the VPN and something downstream tears the session down.

**Supporting indicators:** `LobbyManager` runs a heartbeat + polling loop
(`Assets/Scripts/Network/Services/LobbyManager.cs:112-113, 161-162, 187-188`).

**Would refute (current reading):** `OnLobbyError` is wired only to a *notification*
(`Assets/Scripts/Network/ClientDisconnectHandler.cs:328-332`) — it shows a message, it does not disconnect.
Lobby is HTTPS, which VPNs almost never break. Kept open only because the read was not exhaustive.

**Resolution:** —

## Missing Evidence

| Gap                                        | Impact                                                       | How to Obtain                                                                                 |
| ------------------------------------------ | ------------------------------------------------------------ | --------------------------------------------------------------------------------------------- |
| Client `Player.log` during a VPN drop       | Directly separates H1 / H2 / H3 — each writes a distinct tag  | `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log` right after the repro           |
| Join-time vs mid-game                       | Splits H1 from H2                                             | Ask the reporter: did they see the lobby/board before being kicked?                             |
| Reporter's role (host or client)            | Tests H3                                                      | Ask                                                                                             |
| VPN vendor + protocol (WireGuard/OpenVPN/…) | Whether UDP is blocked or merely tunneled                     | Ask; then test `nc -u` / a UDP reachability check to a Relay endpoint                           |
| Actual elapsed time before the drop         | 5 s ⇒ liveness; 10 s ⇒ join approval; 12 s ⇒ transport        | Stopwatch on the repro                                                                          |

## Source Code Trace

| Element       | Detail                                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Error origin  | H2: `Assets/Scripts/Network/Liveness/LivenessService.cs:218-230` (`HandlePeerLost`) → `Assets/Scripts/Network/ClientDisconnectHandler.cs:274-288`. H1: `Assets/Scripts/Domain/ConnectHandshakePolicy.cs:102-105` (`ApprovalTimeout`) |
| Trigger       | H2: 5 consecutive pump wakes with no beat received (`Assets/Scripts/Network/Liveness/LivenessNetworkPump.cs:90-113`). H1: DTLS/Relay handshake never completing within 10 s of `StartClient` |
| Condition     | Degraded/blocked UDP path introduced by the VPN, with Relay pinned to `"dtls"` and no fallback (`Assets/Scripts/UI/MainMenu.cs:260`) |
| Related files | `Assets/Scripts/Domain/LivenessConfig.cs`, `Assets/Scripts/Domain/LivenessPumpPolicy.cs`, `Assets/Scripts/Domain/LivenessTracker.cs`, `Assets/Scripts/Network/Liveness/LivenessNetworkBridge.cs`, `Assets/Scripts/Network/JoinHandshake.cs`, `Assets/Scenes/BootScene.unity:570-609` |

## Conclusion

**Confidence:** Low (evidence-light — no log, no repro).

Confirmed: the stack is Relay/DTLS-only with no TCP fallback, and a custom liveness layer evicts peers after 5 s
of silence — strictly tighter than the transport's own 12 s tolerance and blind to network (as opposed to local)
stalls. Both facts independently make the game hostile to any latency/jitter/UDP-restricting network path, VPN
being the reproducible case.

Which of the two is actually firing is unresolved and cannot be settled from source alone.

## Recommended Next Steps

### Diagnostic (do this first)

1. Ask the reporter: **at join or mid-game?** host or client? which VPN? how many seconds?
2. One repro with VPN on, then grab `Player.log` and search for `[LIVENESS]`, `[LEAVE][PHASE3]`,
   `RelayServiceException`, `ApprovalTimeout`.
3. Same VPN, UDP explicitly permitted / split-tunnel excluding the game → does it work? (isolates H1)

### Fix direction (only after the above — not authorized yet)

- **If H1:** offer a `"wss"` Relay connection type as a fallback (or a setting), since `dtls` is currently
  hard-coded at three call sites.
- **If H2:** the 5 s liveness window is the lever — either widen it (it should never be tighter than the
  transport's 12 s) or make the pump network-aware so a burst of loss is not counted as five separate misses.
- **If H3:** pass an explicit region to `CreateAllocationAsync`, or surface the chosen region to the host.

## Reproduction Plan

Two builds, two machines (or one machine + one VM). Host without VPN, client with VPN on
(test both a UDP-restrictive corporate VPN and a permissive consumer WireGuard one). Time the drop with a
stopwatch and keep both `Player.log`s. Repeat with the VPN on the host instead, to exercise H3.

## Side Findings

- `Assets/Scripts/Facepunch/FacepunchTransport.cs` is present and complete but is not the transport wired in
  `BootScene.unity` — prod is Relay/UTP. Dead-ish weight worth confirming intent on (Confirmed by
  `BootScene.unity:570`).
- `GetCurrentRtt` returns a hard-coded `0` in the Facepunch transport
  (`Assets/Scripts/Facepunch/FacepunchTransport.cs:100-103`) — irrelevant to prod, noted for completeness.

## Follow-up: 2026-07-21

### New Evidence — industry reference windows (web research)

| Stack | Dead-peer detection | Note |
| --- | --- | --- |
| Unity Transport (UTP) default | **30 000 ms** `DisconnectTimeoutMS` | heartbeat 500 ms; `ConnectTimeoutMS` 1000 × `MaxConnectAttempts` 60 |
| Unity Relay (infra) | **TTL 10 s** idle | 60 s while the host is alone (post-BIND, pre-CONNECT) |
| Unreal Engine | `ConnectionTimeout` **60 s** | `InitialConnectTimeout` also 60 s (BaseEngine.ini) |
| Photon Realtime | `DisconnectTimeout` **10 000 ms** | client-side; not server-configurable |
| Mirror / KCP2K | **10 000 ms** | community raises it to 50 000 ms around scene loads |
| **This project (before)** | **liveness 5 s**, UTP 12 s | both below every reference above |

Sources: [NetworkParameterConstants](https://docs.unity3d.com/Packages/com.unity.transport@2.5/api/Unity.Networking.Transport.NetworkParameterConstants.html),
[Relay client timeouts](https://docs.unity.com/ugs/manual/relay/manual/client-timeouts),
[Unreal ConnectionTimeout](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/Engine/UNetDriver/ConnectionTimeout),
[Photon disconnects](https://doc.photonengine.com/realtime/current/troubleshooting/analyzing-disconnects),
[Mirror KCP](https://mirror-networking.gitbook.io/docs/transports/kcp-transport).

### Additional Findings

#### Finding 6: `m_DisconnectTimeoutMS` was set 2.5× BELOW the Unity default

**Evidence:** `Assets/Scenes/BootScene.unity:578` was `12000`; `NetworkParameterConstants.DisconnectTimeoutMS` = `30000`.

**Detail:** No documented rationale for the tightening. Combined with the 5 s liveness window it left the project
with the two strictest eviction deadlines of any stack surveyed.

#### Finding 7: Liveness beats and transport heartbeats are independent channels

**Evidence:** liveness beats ride application RPCs (`Assets/Scripts/Network/Liveness/LivenessNetworkBridge.cs:171-183`);
UTP keeps its own 500 ms heartbeat (`Assets/Scenes/BootScene.unity:575`); Relay's TTL is reset by transport traffic
([Relay client timeouts](https://docs.unity.com/ugs/manual/relay/manual/client-timeouts)).

**Detail:** A path that jitters application RPCs while transport heartbeats keep flowing evicts the player at the
liveness layer while UTP *and* Relay both still consider the connection healthy. This is the precise mechanism of
Hypothesis 2 and it does not require the connection to actually be broken.

### Changes applied (owner-approved, 2026-07-21)

| Setting | Before | After | File |
| --- | --- | --- | --- |
| Liveness detection window | 5 s (threshold 5) | **15 s** (threshold 15) | `Assets/Scripts/Domain/LivenessConfig.cs:26` |
| `m_DisconnectTimeoutMS` | 12 000 | **30 000** | `Assets/Scenes/BootScene.unity:578` |
| `ApprovalTimeoutSeconds` | 10 s | **30 s** | `Assets/Scripts/Network/JoinHandshake.cs:35` |
| Pinning test | `AreEqual(5, …)` | `AreEqual(15, …)` | `Assets/Scripts/Tests/Editor/LivenessThresholdTests.cs:48` |

Unchanged on purpose: `BeatPeriodSeconds` (1 Hz) and `SyncTotalTimeoutSeconds` (90 s).

Resulting hierarchy — liveness 15 s < transport 30 s < connect-retry budget 60 s. The liveness layer keeps its
design role (detect a half-dead peer *before* the transport does) while clearing the 10 s industry low-water mark.

Verification: EditMode **539/539**, PlayMode **251/251**, no compile errors.

### Updated Hypotheses

- **H2** (5 s liveness window evicts a jittered-but-healthy connection): still Open as the *reported* cause, but
  its mechanism is now mitigated. If the reporter still drops with a VPN after this change, H2 is effectively
  refuted and H1 becomes the prime suspect.
- **H1** (UDP/DTLS blocked ⇒ handshake never completes): **untouched by this change.** No `wss` fallback exists.
  This is now the highest-value remaining path.
- **H3** (host-side VPN degrades the Relay region): unchanged, but a 15 s window absorbs far more of it.

### Backlog Changes

| # | Path to Explore | Priority | Status |
| - | --- | --- | --- |
| 7 | Add a `"wss"` Relay fallback (3 hard-coded `"dtls"` call sites) — the only fix for H1 | High | Open |
| 8 | Re-test with the reporter's VPN after this change — splits H1 from H2 definitively | High | Open |

Backlog #1–#6 remain Open; the log capture is still the decisive artifact.

### Updated Conclusion

**Confidence:** Medium on the *class* of defect (Confirmed: the project's eviction windows were the strictest of
any surveyed stack, and the liveness layer is blind to network — as opposed to local — stalls), still Low on
whether that specific mechanism produced this specific report (no log, no repro).

The widened windows are a justified fix on their own merits regardless of the VPN report. They are **not** a
confirmed fix for it — H1 remains fully live and needs a separate `wss` fallback.

## Follow-up: 2026-07-21 #2 — Backlog #7 feasibility: `wss` Relay fallback

Scope: answer the three blocking constraints for the `wss` fallback (backlog #7 / H1) from the project's ACTUAL
package versions, then settle the architecture. All three answered from primary sources (package source in
`Library/PackageCache` + version-matched Unity docs). Design ready; implementation not started (owner gate).

Package versions (Confirmed, `Packages/packages-lock.json`): NGO **2.12.0**, `com.unity.transport` **6.5.0**
(resolved above NGO's 2.6.0 floor), `com.unity.services.multiplayer` **2.1.3** (Relay SDK lives here — there is
no standalone `com.unity.services.relay` package in this project).

### Constraint 1 — Do host and clients need the same connection type? **NO (Confirmed)**

- Doc for the exact UTP major in use ([UTP 6.5 cross-play manual](https://docs.unity3d.com/Packages/com.unity.transport@6.5/manual/cross-play.html)):
  *"Cross-play support is automatic if you are using Unity Relay. The host could connect to the Relay server
  from an Android device using DTLS, and a client could connect from a web browser using WebSockets, and both
  would be able to communicate without any issue."* The page's own example is host=`"dtls"`, client=`"wss"`.
- Mechanism: each peer connects to the Relay server independently; the peer↔relay hop's protocol is a purely
  local choice. The connection type never crosses to the other peers.
- Corroborated in source: `AllocationUtils.GetValidProtocols()` returns per-PLATFORM valid sets (WebGL: WSS only;
  desktop: UDP/DTLS/WSS) — mixed-platform lobbies are only possible because types are per-peer
  (`Library/PackageCache/com.unity.services.multiplayer@01ce6abde85b/Runtime/Relay/Models/AllocationUtils.cs:250-263`).

**Architectural consequence:** the fallback is **purely local to each peer**. No host↔client negotiation, no
transit through the UGS lobby, no `LobbyManager` change. The Opus-prompt's "type must travel via the lobby"
branch is dead.

### Constraint 2 — `UseWebSockets` coherence: **runtime-settable, MANDATORY, must be set before Start\* (Confirmed)**

- NGO's `UnityTransport` picks the driver interface from `m_UseWebSockets` ALONE:
  `driver = NetworkDriver.Create(new WebSocketNetworkInterface(), …)` only when the flag is true
  (`…netcode.gameobjects@aaabf07f880c/Runtime/Transports/UTP/UnityTransport.cs:1866-1877`).
- A `wss` `RelayServerData` with the flag false (or vice-versa) is only a `Debug.LogError` — NO auto-correction
  (`UnityTransport.cs:1853-1864`); UTP double-checks the same invariant at driver level
  (`…transport@68f8ff7cdc01/Runtime/NetworkDriver.cs:508-520`).
- `UseWebSockets` is a public property (`UnityTransport.cs:99-103`) — settable at runtime; the driver is created
  inside `StartClient`/`StartHost`, so it must be set BEFORE the Start call, alongside `SetRelayServerData`.
- `BootScene.unity` keeps `m_UseWebSockets: 0` serialized — the helper sets the property explicitly on EVERY
  attempt (true for wss, back to false for dtls), so no scene edit and no stale-flag leak into a later session
  or the UTP loopback tests.

### Constraint 3 — WebSocket support in the shipped transport: **YES (Confirmed)**

- `com.unity.transport` 6.5.0 ships `WebSocketNetworkInterface` (used across
  `Runtime/NetworkDriver.cs`, `Runtime/NetworkStack.cs:171-187`, `Runtime/Layers/NetworkInterfaceLayer.cs`).
- `AllocationUtils.ToRelayServerData(alloc, "wss")` is valid on desktop builds and returns
  `RelayServerData(isSecure: endpoint.Secure, isWebSocket: true)` (`AllocationUtils.cs:86-146`). Only
  `udp`/`dtls`/`wss` are accepted — plain `ws` is NOT in the map (`AllocationUtils.cs:19-25`).
- Caveat (runtime-only): the wss endpoint must exist in `allocation.ServerEndpoints`; `GetEndpoint` throws
  `ArgumentException` otherwise (`AllocationUtils.cs:153-168`). Docs say allocations expose all types; verify
  once with a log line in the first playtest.

### Additional Findings

#### Finding 8: The failure taxonomy needed for a safe retry already exists

**Evidence:** `Assets/Scripts/Domain/ConnectHandshakePolicy.cs:6-18` (`ConnectFailReason`:
`SessionEnded` / `ApprovalTimeout` / `TotalTimeout`); `Assets/Scripts/Network/JoinHandshake.cs:46-114`.

**Detail:** A UDP-blocked path produces `ApprovalTimeout` (transport keeps retrying ~60 s
— `ConnectTimeoutMS 1000 × MaxConnectAttempts 60` — so the 30 s approval deadline always fires first). An
explicit server rejection (mid-game join, `ConnectionApprovalGate`) tears NGO down ⇒ `SessionEnded` **with
`NetworkManager.DisconnectReason` filled**. A transport give-up ⇒ `SessionEnded` with an EMPTY reason. Retry
policy: retry on `ApprovalTimeout` or empty-reason `SessionEnded`; never retry a filled-reason rejection.

#### Finding 9: Host-side BIND failure is detectable — `GetRelayConnectionStatus()`

**Evidence:** `UnityTransport.GetNetworkDriver()` is public (`UnityTransport.cs:382`); NGO itself polls
`m_Driver.GetRelayConnectionStatus() == RelayConnectionStatus.AllocationInvalid` each early-update
(`UnityTransport.cs:1161-1167`).

**Detail:** After `StartHost()` (which returns true on a merely-local bind), the host can poll
`GetNetworkDriver().GetRelayConnectionStatus()` until `Established` or a short deadline (~10 s). `NotEstablished`
at deadline ⇒ relay unreachable over dtls ⇒ shutdown, re-allocate, retry wss. Today `GetJoinCodeAsync` runs
BEFORE `StartHost` (`MainMenu.cs:413`) — it must move after the bind check so the published join code always
belongs to the surviving allocation.

#### Finding 10: A fresh `JoinAllocationAsync` per attempt is required

**Evidence:** Relay idle TTL ≈ 10 s pre-connect (Follow-up #1 table); client attempt lasts up to 30 s.

**Detail:** By the time a dtls attempt times out, the join allocation created for it is expired. Each retry must
re-call `JoinAllocationAsync(joinCode)` (host allocation is untouched; a new client allocation is cheap).

#### Finding 11: The retry loop must hold the `SetJoinHandshakeInProgress` claim across attempts

**Evidence:** `JoinHandshake.cs:75-78, 105-107`; `ClientDisconnectHandler` pops "Connexion à l'hôte perdue" on
`OnClientStopped` otherwise.

**Detail:** Between a failed dtls attempt and the wss attempt there is a `Shutdown()` — without the claim held
across the whole loop, the intermediate teardown flashes a bogus disconnect notification mid-fallback.

### Settled architecture (design ready — NOT implemented)

One decision point, e.g. `Assets/Scripts/Network/RelayConnector.cs` (static, UniTask):

- `ConnectClientAsync(joinCode)` — for `type in [dtls, wss]`: fresh `JoinAllocationAsync` →
  `transport.UseWebSockets = (type==wss)` → `SetRelayServerData(alloc.ToRelayServerData(type))` →
  `StartClient()` → `JoinHandshake.WaitForConnectedOrTimeout()`. Retry per Finding 8 only; between attempts
  `Shutdown()` + await `!IsListening`; claim held per Finding 11.
- `HostAsync(maxConnections)` — `CreateAllocationAsync` → dtls → `StartHost` → poll relay status per Finding 9;
  on failure re-allocate in wss; `GetJoinCodeAsync` only after `Established`.
- The 3 call sites (`MainMenu.cs:260`, `MainMenu.cs:412`, `LobbySelectionPanel.cs:470`) collapse onto it; the
  `WaitForConnectedOrTimeout` calls move from the callers into the loop.
- Testing: retry decision extracted as a pure Domain policy (reason × disconnect-reason → verdict) + EditMode
  tests; no real-wss PlayMode test (loopback stays UTP/UDP per project test-transport rule).

**Open decisions for Poyo:** (1) first-attempt dtls deadline — keep 30 s (worst case 60 s to a final failure) or
shorten to ~15 s for a snappier fallback (a false-positive only costs "connected via wss instead of dtls", not a
failure); (2) show a "nouvelle tentative en mode compatible…" UI hint during the wss retry; (3) ship host-side
fallback in the same change or client-only first.

**Player-facing effect:** a player whose network blocks UDP (VPN, captive portal, strict NAT) connects via
WebSocket/TCP 443 after the dtls deadline instead of never connecting. wss has higher latency (TCP
head-of-line blocking) — it stays a fallback, never the default.

**Real-playtest checklist:** tester behind a UDP-blocking network (corporate VPN / UDP-filtered Wi-Fi); log
`allocation.ServerEndpoints` once to confirm the wss endpoint exists in prod; confirm a wss session survives the
15 s liveness window; TLS-MITM proxies may still break wss — out of scope.

### Backlog Changes (#2)

| # | Path to Explore | Priority | Status |
| - | --- | --- | --- |
| 7 | Add a `"wss"` Relay fallback | High | **Implemented** (2026-07-21, spec-wss-relay-fallback, EM 548/PM 251 green) — awaiting commit authorization + a UDP-blocked playtest (owner has no VPN; needs an external tester). Review discovery: NGO fills `DisconnectReason` with a "[Disconnect Event]…" placeholder on EVERY client transport drop — the retry verdict discriminates via `RelayFallbackPolicy.HasServerReason` |
