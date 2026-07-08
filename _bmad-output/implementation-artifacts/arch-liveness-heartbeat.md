---
title: "Architecture — Transport-agnostic liveness / heartbeat layer"
status: draft
type: architecture
owner: Poyo
created: 2026-07-08
branch: feat/player-leave-stability
source_investigation: investigations/hard-disconnect-heartbeat-investigation.md
source_roundtable: party-mode (Cloud Dragonborn / Link Freeman / Murat / Winston), 2026-07-08
---

# Architecture — Transport-agnostic liveness / heartbeat layer

## 1. Problem (grounded)

Hard-disconnect (Alt-F4 / kill process / power-off — no graceful close) reacts too slowly and, for one case, not at all:
- **Detection IS wired today** (transport disconnect → NGO `OnClientDisconnectCallback` → `GameManager.HandlePlayerLeft`; `OnClientStopped`/`OnTransportFailure` → `ClientDisconnectHandler`), but it is **coupled to each transport's own timeout**: UnityTransport/Relay `DisconnectTimeoutMS = 30000` (30 s — the felt "game doesn't react"), Facepunch ≈ 10 s.
- **The half-dead client is NEVER detected by the transport**: if a player's app freezes / soft-locks but the socket stays alive, no transport timeout fires. Owner requirement (2026-07-08): **a player who can no longer play MUST be removed from the game for the others.** Only an application-level heartbeat catches this (no beat received ⇒ the app is gone, even though the socket is up).

## 2. Owner decisions (locked)

- **Target detection latency ≈ 6 s** (no sub-second requirement).
- **Half-dead client MUST be handled** (app frozen, socket alive → the player leaves the game for everyone). → the app-level layer is justified (this is the case the transport cannot see).

## 3. Decision: BOTH, in two independent deliverables

### A. Interim — tune the transport timeout (analgesic, ship first)
- `Assets/Scenes/BootScene.unity` UnityTransport `m_DisconnectTimeoutMS: 30000 → 6000` (keep `HeartbeatTimeoutMS: 500`). Facepunch has no configured timeout today (investigation C9) — record for the Steam-ship pass; not a blocker now.
- Kills the hard-kill latency immediately, transport-native, zero new code. **Does NOT solve half-dead** (socket alive) — that is the layer's job. Interim, not the solution.

### B. The liveness layer (the real fix — justified by the half-dead requirement)

Converged design from the roundtable. Guiding principle (Cloud): *the heartbeat is a liveness clock that happens to use a network channel* — keep the clock/decision in a POCO, the network as a detail behind a seam.

**Direction & authority (Cloud + Link + Murat, unanimous):** server-authoritative.
- Clients emit a light `Alive` beat to the server at a fixed rate (1 Hz). The server holds the liveness state and **is the only one that declares a client lost**, feeding the existing server leave pipeline.
- The client independently tracks `lastServerContact`; if it dries up, the client declares the **host** lost and drives its own existing `ClientDisconnectHandler` path (local "I'm alone" detection, not authority).
- The silence IS the signal — no ack/handshake protocol.

**Seams (POCO-first, mockable):**
- **`ILivenessClock`** — narrow time port, same shape/discipline as the existing `IRandomProvider`: `long NowTicks { get; }` (monotonic; no `DateTime.Now`, no `Time.deltaTime` in domain logic). Real impl over the game loop; `FakeLivenessClock` for tests.
- **`LivenessTracker`** — pure POCO domain object, **NO time inside** (Murat, decisive): the decision is **count-of-missed-beats**, not seconds. `RecordBeat(peerId)` resets a per-peer miss counter; `Tick()` increments every peer's counter; a peer is lost when `missedBeats >= threshold`. Emits a decision (`PeerLost(peerId)`), not a reaction. The seconds↔beats conversion (6 s / 1 Hz ⇒ threshold ≈ 6) lives in config, never in the tracker.
- **`ILivenessPump` (scheduler)** — the ONLY place wall-clock lives: cadences `Tick()`. Prod impl accumulates the game loop; **`ManualLivenessPump` for tests** (`pump.AdvanceBeats(n)` — no real seconds elapse). Threshold logic never goes in the pump (the pump cadences, the tracker decides).
- **Beat transport** — the RPC carrier. **Must live on an ALREADY-SPAWNED `NetworkObject` whose lifecycle is guaranteed** — the GameManager's (Link, decisive): do NOT invent a new `LivenessManager` prefab that must be remembered-to-spawn (that is exactly the `OnNetworkSpawn`-never-fires trap that just bit vote-skip). The `NetworkBehaviour` is a dumb RPC pipe; a **guard log** (`[LIVENESS] spawned=true`) fires on spawn so a future spawn bug screams instead of dead-ending silently. Two named RPCs (`HeartbeatServerRpc`, and a server→client keepalive for `lastServerContact`), NOT a `DoStateMethodRpc` enum-dispatch (overkill). **`GetSafeRpcTarget` mandatory; exclude `clientId >= 100` (simulated bots never pong → they are alive by definition).**

**Integration — feed the existing reactions, never duplicate (Cloud + Link + Murat, unanimous):**
- Server: `LivenessTracker.PeerLost(clientId)` → the **same** `GameManager.HandlePlayerLeft(clientId)` already driven by `OnClientDisconnectCallback` (lobby → remove / mid-game → instant-chain leaver + victory re-eval + unblock waiting state). The layer is just a **second, faster ignition source** of the same pipeline.
- Client: host-lost → the **same** `ClientDisconnectHandler` path, consulting the **same** one-shot `expectedShutdown` flag so a graceful `ShutOffGame` / pause-leave never triggers a false "host lost".

**Complement, never supersede (unanimous):** the transport timeout stays armed as the last-resort backstop (the case where the liveness POCO has a bug / is not pumped). Layer wins the race (~6 s); transport backstop cleans up behind it. Requirement: `liveness window < transport DisconnectTimeoutMS`.

## 4. THE critical risk — idempotency of HandlePlayerLeft (Cloud + Murat, flagged as #1)

Two ignition sources now exist for the same leave (liveness at ~6 s, transport backstop later). `GameManager.HandlePlayerLeft(clientId)` **must be idempotent per clientId — a second call is a no-op.** The Phase-1 unification (commit `c032932`) should already give this (departed-set records the leaver; re-chaining an already-chained/removed seat must not double-fire, NRE, or re-mutate victory). **Lock it with an explicit test** — this is the regression risk of the whole chantier, not the heartbeat itself.

## 5. Testability (Murat) — max unit coverage

- **Pure EditMode unit tests (90% of confidence):** `LivenessTracker` truth table — `RecordBeat` then N `Tick()`s with no beat → `PeerLost` at exactly the threshold, not before; multi-peer independence; a beat resets; bots (≥100) never declared lost. Frame-independent, wall-clock-independent, zero `WaitForSeconds`.
- **`FakeLivenessClock` + `ManualLivenessPump`** drive all timing; the decision layer never sees a real second.
- **Silence simulation** at the app layer, NOT by killing a socket + waiting the transport timeout (30 s flake): stop calling `RecordBeat` for a peer (or a mocked `sender.Silence(peerId)`), `pump.AdvanceBeats(threshold)`, assert `HandlePlayerLeft` fired.
- **Idempotency test:** `HandlePlayerLeft(sameClient)` twice → second is a no-op (locks §4).
- **PlayMode integration (thin):** the real RPC round-trip on the 2-NetworkManager loopback fixture (`ILivenessTransport` NGO impl). **Drive/assert from the HOST only** — `IsOwner`/`LocalClientId` are unreliable on client replicas in the multi-NM fixture (project memory). Keep this minimal; the port-7777 bind flake makes PlayMode fragile — push maximum logic into the EditMode POCO.
- **No CI test for real Facepunch** (no headless Steam fixture): transport-agnosticism is guaranteed by the SEAM contract (`ILivenessTransport`), validated with a fake; the Facepunch impl is smoke-checked by Poyo at Steam-ship.

## 6. Phased delivery

- **B0 (interim):** BootScene `DisconnectTimeoutMS` 30000→6000. Independent, ship first.
- **B1 (POCO core):** `ILivenessClock`, `LivenessTracker`, `ILivenessPump` + `ManualLivenessPump` + `FakeLivenessClock`, wired into `CompositionRoot`. Full EditMode unit battery. NO NGO yet.
- **B2 (transport + wire):** the beat RPCs on the GameManager NetworkObject (`GetSafeRpcTarget`, exclude ≥100, guard log), server `PeerLost` → `HandlePlayerLeft`, client `lastServerContact` → `ClientDisconnectHandler` (respect `expectedShutdown`). PlayMode seam test (host-driven) + the idempotency test.
- **B3 (Steam-ship, later):** map the liveness/timeout onto Facepunch; manual 2-build Steam validation.

## 7. Non-goals / rejected

- **Reinventing the transport keepalive** (Winston): the app heartbeat exists ONLY for what the transport can't do (half-dead + a single cross-transport threshold). Native keepalive stays.
- **Piggybacking the beat on gameplay traffic** (Cloud): premature optimization; a dedicated 1 Hz tick is predictable + testable. Measure cost later.
- **Adaptive jitter/backoff thresholds** (Murat): non-deterministic; fixed threshold for v1.
- **A new spawned prefab for the layer** (Link): reuse the GameManager NetworkObject; no new spawn-lifecycle surface.
- **Supersede the transport timeout** (unanimous): it is the backstop.

## 8. Doc review resolution — LOCKED design (party-review 2026-07-08: Cloud / Link / Murat, all GO-WITH-CHANGES)

The three reviewers converged; one real tension (pure beat-count vs time-based decision) is reconciled below. This section supersedes the open items and is the binding spec for B1/B2.

### 8.1 Tracker vs pump — the reconciliation (resolves Murat ↔ Cloud/Link)
- **`LivenessTracker` = PURE integer beat-count, NO clock, NO time** (Murat, decisive). Decision is `missedBeats >= threshold`. `ILivenessClock` NEVER enters the tracker. The tracker is a pure EditMode unit.
- **The pump OWNS the clock AND the stall-guard** (Cloud/Link's mass-false-positive fix, kept OUT of the tracker): the prod pump wakes on a realtime cadence and measures real elapsed since its last wake via `ILivenessClock`. Normal elapsed → call `tracker.Tick()` once. **Abnormally large elapsed (the pump itself was starved: GC / scene-load / `timeScale=0` / editor focus-loss) → SKIP `Tick()` for that cycle** (the stall does NOT count as a missed beat for anyone). This prevents a host hitch from evicting the whole match, while the tracker stays pure and deterministic.
- **`ILivenessClock`**: `long NowTicks` from `Stopwatch.GetTimestamp` (QPC, monotonic, unaffected by `timeScale`) — NOT `Time.deltaTime`/`realtimeSinceStartup`. Consumed ONLY by the pump.
- **Threshold** is an `int` of beats, computed ONCE at wiring: `threshold = max(2, ceil(timeoutSeconds / beatPeriodSeconds))` from SO config in the factory/CompositionRoot. Floor of 2 (a threshold of 1 makes a single missed beat fatal — flaky even in prod). The tracker never sees seconds or period. Rounding = **ceil** (never declare lost too early) and is unit-tested.

### 8.2 Tracker semantics (locked)
- **Bots (`clientId >= 100`) are NEVER enrolled** — no tracker entry created (not "created then special-cased at detection"). No entry ⇒ no possible false PeerLost.
- **Enrollment grace**: a newly enrolled peer starts at `missedBeats = 0`; it is only ticked once enrolled (no eviction before its first beat window).
- **Terminal single-shot eviction**: on `missedBeats >= threshold`, emit `PeerLost(clientId)` **exactly once**, then **remove the peer from the tracked set** (terminal). A subsequent `Tick()` must not re-emit.
- **No resurrection** (reconnection is out of scope per [[project_disconnect_policy]]): `RecordBeat` on an unknown/already-lost clientId is a silent no-op — it does NOT re-insert the peer.
- `PeerLost` carries the correct clientId.
- **`Tick()` is called ONLY by the pump** (documented; ideally a guard that no `MonoBehaviour.Update` references the tracker) — never from a frame loop, or the count becomes framerate-dependent.

### 8.3 Pump home (open item 2 — LOCKED)
`ILivenessPump` prod tick = a **UniTask loop launched from `CompositionRoot`** when the server session starts, `UniTask.Delay(..., DelayType.Realtime)` (unscaled), cancelled via a `CancellationToken` bound to the session lifecycle (same teardown as the rest — do NOT let it outlive a return-to-menu, cf. the LobbyState-sub-leak family). **Not** `GameManager.Update` (framerate/timeScale coupling), **not** `OnNetworkSpawn` (the trap that bit vote-skip). The pump no-ops cleanly while the `LivenessNetworkBridge` is not spawned. `ManualLivenessPump` (`AdvanceBeats(n)`) drives tests — no real seconds.

### 8.4 Client host-liveness (open item 3 — LOCKED: explicit keepalive)
The server emits an **explicit** server→client keepalive (same 1 Hz pump). The client runs a **symmetric single-entry `LivenessTracker`** (the host is its one peer): `RecordBeat` on keepalive receipt, `Tick()` on its own local pump, `missedBeats >= threshold` → host-lost → the **existing** `ClientDisconnectHandler` path (respecting `expectedShutdown`). Reusing "any received gameplay RPC" as the signal is REJECTED (quiet phases → false host-lost; non-deterministic; and it is precisely the half-dead-host case — socket alive, host app frozen — that no gameplay traffic would signal). Optional belt-and-suspenders: also refresh `lastServerContact` on any server RPC as a false-positive reducer — never as the sole signal.

### 8.5 Network carrier (Link — LOCKED)
- A dedicated **`LivenessNetworkBridge : NetworkBehaviour` placed on the SAME GameObject as the GameManager `NetworkObject`** (session-long: GameManager is scene-placed in GameScene; the lobby is a *state*, not a scene, so the NO lives the whole session). NOT RPCs added to GameManager (bloat + mixed lifetimes). Multiple NBs on one NO have NO guaranteed inter-`OnNetworkSpawn` order → the bridge depends on NOTHING at spawn except registering itself; guard log `[LIVENESS] spawned=true`.
- `HeartbeatServerRpc` is **`[ServerRpc(RequireOwnership = false)]`** (explicit — else a non-owner client's beat is silently dropped and the server thinks every client is dead). The sender is read from `ServerRpcParams.Receive.SenderClientId`, never a client-supplied param.
- Keepalive server→client via `ClientRpc` + `GetSafeRpcTarget`, which must **skip** `clientId >= 100` (no real socket; don't self-keepalive the host into the void).

### 8.6 Timeout margin (open item 1 — LOCKED)
Beat rate **1 Hz**, liveness detection window **~5 s** (threshold 5). Transport timeout is the SLOWER backstop with real margin: **B0 interim sets UnityTransport `DisconnectTimeoutMS` 30000→6000** (immediate relief, sole detector before the layer); **B2, once the layer is primary, raises the backstop to ~12000** so liveness (~5 s) always ignites first and the transport (~12 s) is a genuine last-resort — not both firing in the same second.

### 8.7 Idempotency of HandlePlayerLeft (THE risk — Murat's mandated cases)
Two ignition sources (liveness + transport backstop) → `HandlePlayerLeft(clientId)` must be a total no-op on the second call, in **both orders**. Mandated tests: (a) liveness→transport; (b) **transport→liveness** (the order that usually breaks); (c) redundant call for a **lobby** leaver (no Character → no chaining attempt/NRE); (d) redundant call for a **mid-game** leaver (no double-chain, no double win-check, no replayed chain animation — the VISIBLE effect must be idempotent, not just the internal flag). Tracker-level exactly-once (§8.2) and handler-level idempotency are tested SEPARATELY (one EditMode POCO test, one direct-handler double-call test), not through each other.

### 8.8 Test plan (Murat — LOCKED). B1 "done" = these 12 EditMode POCO tests pass with `FakeLivenessClock` + `ManualLivenessPump`, zero `WaitForSeconds`:
1. `RecordBeat_ResetsMissedCounter_ForThatPeerOnly`
2. `Tick_IncrementsMissedBeats_ForAllTrackedPeers`
3. `Tick_BelowThreshold_DoesNotEmitPeerLost`
4. `Tick_ReachingThreshold_EmitsPeerLostExactlyOnce`
5. `Tick_PastThreshold_DoesNotReEmitPeerLost` (terminal removal)
6. `RecordBeat_OnAlreadyLostPeer_IsNoOp_DoesNotResurrect`
7. `RecordBeat_OnUnknownPeer_IsIgnored` (or the explicit auto-registration decision, then tested)
8. `SimulatedBot_ClientIdOver100_NeverDeclaredLost`
9. `ThresholdConversion_SecondsToBeats_RoundsAsCeil` (+ floor of 2)
10. `RemovePeer_StopsTicking` (voluntary leave → no late PeerLost)
11. `MultiPeer_IndependentCounters`
12. `PeerLost_CarriesCorrectClientId`
Plus the pump stall-guard test (`Pump_AbnormalElapsed_SkipsTick_NoMassMiss`) and the §8.7 idempotency tests. B2 PlayMode seam = ONE host-driven assertion (a client beat RPC reaches the server bridge and calls `RecordBeat(senderClientId)`) on the existing 2-NM fixture, poll-frames not WaitForSeconds, inject `ManualLivenessPump` (only the RPC round-trip is "real"). No CI test on real Facepunch — manual checklist at Steam-ship.

### 8.9 Out of scope (noted)
- **Phase timeouts** (a client that beats but never votes/acts, stalling phase resolution) are a SEPARATE seam — liveness proves the socket+beat, not player *progress*. VoteState already has a timer; other phases have their own timeouts. Not this chantier.
- Facepunch timeout mapping = B3 (Steam-ship), manual validation.
- Reconnection = out of scope (kills resurrection semantics — §8.2).
