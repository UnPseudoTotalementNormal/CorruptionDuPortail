# Investigation: Hard-disconnect not detected (Alt-F4 / power-off) — heartbeat needed?

## Hand-off Brief

1. **What happened.** Owner reports that when a client or host quits HARD (Alt-F4, kill process, power off — no graceful shutdown), the game does not react: a hard-quitting host leaves clients stuck in a dead game, and a hard-quitting client is not treated as "left" server-side.
2. **Where the case stands.** Stronghold Confirmed: the production Facepunch/Steam transport DOES have `OnDisconnected` callbacks (client + server) that raise `NetworkEvent.Disconnect`. Open question under investigation: do Steam's socket callbacks actually fire on a HARD peer death, is the transport pumped each frame to process them, and does that reach the game's leave/host-drop handlers.
3. **What's needed next.** A grounded map of the current hard-disconnect behavior (transport timeout, Facepunch propagation, Phase-3 handler firing, cloud-lobby heartbeat) to feed a party-mode architecture session for the heartbeat design.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | N/A |
| Date opened      | 2026-07-08 |
| Status           | Active |
| System           | Unity 6000.2.6f2, NGO + Facepunch(Steam) transport (prod) / UnityTransport (tests); branch feat/player-leave-stability |
| Evidence sources | Source code (transport, NGO config, LobbyManager, ClientDisconnectHandler); NO runtime capture (hard-quit not reproducible by Claude) |

## Problem Statement

Owner (Poyo): hard-quit (Alt-F4 / kill / power-off) does not end/clean the game. Host hard-quit → clients stranded; client hard-quit → not registered as left server-side. Proposes an app-level heartbeat. Investigation must first establish what the transport does today (challenge the "needs a custom heartbeat" premise — NGO/Steam may already time out, or just need tuning).

## Evidence Inventory

| Source | Status | Notes |
| ------ | ------ | ----- |
| `Assets/Scripts/Facepunch/FacepunchTransport.cs` | Available | OnConnected/OnDisconnected Steam callbacks; polling/Receive pump; timeout behaviour |
| NGO `NetworkConfig` (transport/timeout/approval) | Available | disconnect-timeout config, ConnectionApproval |
| `Assets/Scripts/Network/Services/LobbyManager.cs` | Available | cloud-lobby heartbeat (15s) + poll (5.1s) — separate layer |
| `Assets/Scripts/Network/ClientDisconnectHandler.cs` | Available | Phase-3 OnClientStopped/OnTransportFailure host-drop handlers |
| Runtime hard-quit capture | Missing | Claude cannot playtest; owner would capture — see Missing Evidence |

## Hypothesized Paths

### Hypothesis 1 (owner): a custom app-level heartbeat is required

**Status:** Open

**Theory:** The transport does not detect hard-disconnects, so an application-level heartbeat (periodic ping; miss N → declare gone) is needed for both host-drop (client detects) and client-drop (server detects).

**Would confirm:** Evidence that Steam sockets do NOT fire OnDisconnected on hard peer death within a usable window, OR the transport is not pumped so callbacks never process, OR NGO has no disconnect timeout.

**Would refute:** Evidence that Steam OnDisconnected DOES fire on timeout and reaches the game handlers (then the fix is tuning/wiring, not a new heartbeat).

## Confirmed Findings

### Finding 1: The Facepunch transport raises NetworkEvent.Disconnect from Steam OnDisconnected callbacks

**Evidence:** `Assets/Scripts/Facepunch/FacepunchTransport.cs:211` (`IConnectionManager.OnDisconnected` → `InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, ...)` :213) and `:262` (`ISocketManager.OnDisconnected` → `InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, ...)` :266).

**Detail:** A detected Steam disconnect DOES propagate into NGO as a transport Disconnect (client-side: connection to host lost; server-side: a client connection dropped). The open question is whether Steam FIRES these on a hard peer death and whether the transport's Receive loop is pumped to deliver them.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| Real hard-quit timing (does OnDisconnected fire, after how long) | Decides heartbeat-vs-tuning | Owner captures: 2 Steam builds, Alt-F4 one, watch the other's console/logs + timing |
| Steam relay keepalive/timeout defaults | Bounds the detection window | Steamworks docs / SteamNetworkingConfig |

### Finding 2: Detection IS wired end-to-end today (both directions) — the "nothing detects it" premise is contradicted

**Evidence:** Transport is pumped every frame (`FacepunchTransport.cs:60-63` `Update()→SteamClient.RunCallbacks()`; `:152-161` `PollEvent→Receive()`). Chain, both directions: Steam/UTP disconnect → `NetworkEvent.Disconnect` → server `OnClientDisconnectCallback`→`GameManager.HandlePlayerLeft` (`GameManager.cs:181/623`); client `OnClientStopped`/`OnTransportFailure`→`ClientDisconnectHandler` (`:158-159/199-238`). Graceful-vs-abrupt discriminated by a one-shot flag (`HostDropPolicy.ShouldNotifyHostLoss`), not a transport signal.

**Detail:** A hard-quit DOES eventually reach the game handlers. The felt symptom ("nothing happens") is **latency + transport coupling**, not missing detection.

### Finding 3: The current transport is UnityTransport (Unity Relay), and its disconnect timeout is 30 s — the concrete cause of the felt delay

**Evidence:** `Assets/Scenes/BootScene.unity:570-578` — `m_ProtocolType: 1` (Relay), `m_HeartbeatTimeoutMS: 500`, `m_ConnectTimeoutMS: 1000`, `m_MaxConnectAttempts: 60`, `m_DisconnectTimeoutMS: 30000`. Owner confirmed (2026-07-08) he is NOT on Facepunch currently → UnityTransport/Relay is the live transport; Facepunch is the future Steam-ship transport (`NetworkTransportDetector` picks between them).

**Detail:** UTP sends keepalives every 500 ms but only declares a peer dead after **30 000 ms of silence** → a hard-quit is detected ~30 s later (≈ "the game doesn't react"). Facepunch would be ~10 s (Steam default, unconfigured — Finding C9). So detection latency is **transport-specific** (30 s UTP vs ~10 s Steam) — the coupling the owner wants to remove.

### Finding 4: The cloud-lobby heartbeat is a separate layer and its poll result is unconsumed for drop detection

**Evidence:** `LobbyManager.cs` — Unity Lobby heartbeat (15 s) + poll (5.1 s) covers **cloud-lobby membership**, not the NGO game session; nothing subscribes to `OnLobbyUpdated`/`OnLobbyLeft` to react to a member vanishing (only `ClientDisconnectHandler` consumes `OnLobbyError`). No app-level ping/heartbeat on the game/NGO layer anywhere (grep: 0 hits outside LobbyManager).

## Hypothesis 1 — resolution

**Status:** Refuted (as stated) / reframed. "Nothing detects a hard-quit" is Refuted (Finding 2 — it is wired). What the evidence supports instead: detection is **transport-coupled and slow** (30 s UTP / ~10 s Steam) and **unvalidated beyond UnityTransport loopback**. The owner's real requirement (2026-07-08) is a **transport-agnostic, interface-decoupled liveness layer** so detection is fast + consistent + mockable across UnityTransport/Relay (now) and Facepunch/Steam (ship) — a valid architecture goal regardless of the per-transport timeout, because relying on each transport's own timeout is the coupling to remove.

## Conclusion

**Confidence:** Medium-High (detection wiring + current UTP 30 s timeout are Confirmed from code; the only runtime unknown is per-transport hard-kill firing/latency, deferred to a 2-build test).

Hard-disconnect **is** detected today (transport disconnect event → `HandlePlayerLeft` / `ClientDisconnectHandler`), but the mechanism is **coupled to each transport's own timeout** (UTP 30 s — the felt delay; Steam ~10 s) and unproven outside loopback. The clean fix per the owner is not a bug-patch but an **architecture**: a decoupled, interface-based liveness/heartbeat abstraction, transport-agnostic and testable, sitting above the transport so detection speed and behaviour no longer depend on which transport is underneath. This grounds the party-mode architecture session.

## Recommended Next Steps

- **Architecture (owner's call): party-mode** to design the transport-agnostic liveness layer (interfaces, where the heartbeat lives, host-drop vs client-drop, integration with the existing `HandlePlayerLeft` / `ClientDisconnectHandler` reactions, testability with a mock transport clock).
- **Cheap interim (independent):** UTP `m_DisconnectTimeoutMS` 30 000 → e.g. 5 000–8 000 would cut the felt delay now — but it is exactly the transport-coupled knob the decoupled design should supersede; treat as a stopgap, not the solution.
- **Runtime validation (owner, later):** the 2-build test (kill host / kill client, measure latency, confirm the discriminator) validates whichever design ships — especially on Facepunch, never yet exercised.

