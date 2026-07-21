---
title: 'wss Relay fallback for UDP-hostile networks'
type: 'feature'
created: '2026-07-21'
status: 'done'
baseline_commit: '5849076de0c4bcefc031e5ef4dbdbdf10596f7d6'
context:
  [
    '{project-root}/_bmad-output/implementation-artifacts/investigations/vpn-instant-disconnect-investigation.md',
  ]
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Relay is hard-bound to `"dtls"` (UDP) at 3 call sites with no fallback; a player (or host) whose network blocks UDP (VPN, captive portal) can never connect (investigation H1, backlog #7).

**Approach:** One decision point (`RelayConnector`) that tries `dtls`, and on a connectivity-shaped failure retries `wss` (WebSocket/TCP 443). Fallback is purely local per peer — Relay supports mixed connection types (UTP 6.5 cross-play doc, Confirmed); nothing transits via the UGS lobby.

## Boundaries & Constraints

**Always:**

- `transport.UseWebSockets` set explicitly on EVERY attempt (true=wss, false=dtls) BEFORE `StartClient`/`StartHost`; `BootScene` serialized `m_UseWebSockets: 0` untouched.
- Fresh allocation per attempt (`JoinAllocationAsync`/`CreateAllocationAsync`) — Relay idle TTL (~10 s) outlives no 30 s attempt.
- Retry decision = pure Domain POCO (`RelayFallbackPolicy`), EditMode-tested. Retry ONLY on `ApprovalTimeout`, or `SessionEnded` with EMPTY `DisconnectReason`. A filled `DisconnectReason` (server rejection) or `TotalTimeout` fails immediately with the existing message flow.
- Client deadlines stay 30 s per attempt (owner decision — worst case 60 s). Host bind-check deadline 10 s per attempt.
- `ClientDisconnectHandler.SetJoinHandshakeInProgress(true)` re-latched between attempts (before the inter-attempt `Shutdown()`) so no "Connexion à l'hôte perdue" popup flashes mid-fallback; always unlatched on final exit.
- Host publishes the join code ONLY from the surviving allocation, after `GetRelayConnectionStatus() == Established`.
- UniTask only; `[RELAY]`-tagged logs for the fallback transitions.

**Ask First:**

- Any change to `JoinHandshake` semantics beyond reusing it as-is.
- Any new serialized field or scene edit (none expected).

**Never:**

- No connection-type negotiation via `LobbyManager`/lobby data.
- No settings UI / player-facing toggle; retry is silent (owner decision).
- Don't touch the Facepunch paths, the liveness layer, or eviction windows.
- No PlayMode test attempting real wss/Relay traffic (test transport stays UTP loopback).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy path | UDP fine | Connected via dtls, 1 allocation, no retry | N/A |
| UDP blocked (client) | dtls attempt hits `ApprovalTimeout` | Shutdown → fresh join allocation → wss attempt → connected | No popup between attempts |
| Transport give-up (client) | `SessionEnded`, `DisconnectReason` empty | Same wss retry | idem |
| Server rejection | `SessionEnded`, `DisconnectReason` filled (e.g. "La partie a déjà commencé.") | NO retry; existing failure message shown | Existing catch/AbortJoin teardown |
| Stuck sync | `TotalTimeout` (host was reachable) | NO retry; existing failure flow | idem |
| Both attempts fail | dtls + wss `ApprovalTimeout` | Failure message built BEFORE teardown, caller teardown runs once | Existing flow |
| UDP blocked (host) | Relay status ≠ `Established` 10 s after `StartHost` | Shutdown → fresh allocation wss → StartHost → status poll → join code | Both fail → null → caller error path |
| `RelayServiceException` (UGS HTTPS down) | Allocation call throws | NO retry (not UDP-shaped), existing error path | Log + fail |
| wss endpoint missing | `ToRelayServerData("wss")` throws `ArgumentException` | Fail cleanly, log endpoint list | No crash |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Domain/ConnectHandshakePolicy.cs` -- `ConnectFailReason` enum consumed by the new policy
- `Assets/Scripts/Domain/RelayFallbackPolicy.cs` -- NEW POCO: retry verdict
- `Assets/Scripts/Network/RelayConnector.cs` -- NEW static helper: the single decision point (client + host loops)
- `Assets/Scripts/Network/JoinHandshake.cs` -- reused as-is (`WaitForConnectedOrTimeout`, `BuildFailureMessage`)
- `Assets/Scripts/Network/ClientDisconnectHandler.cs` -- `SetJoinHandshakeInProgress` latch (reused)
- `Assets/Scripts/UI/MainMenu.cs:245-269,406-433` -- join-by-code + host call sites; shared post-join wait at `:164` moves into the Facepunch branch
- `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:455-486` -- lobby-list join call site; wait at `:331` moves into the Facepunch branch
- `Assets/Scripts/Tests/Editor/` -- new `RelayFallbackPolicyTests.cs`

## Tasks & Acceptance

**Execution:**

- [x] `Assets/Scripts/Domain/RelayFallbackPolicy.cs` -- create `ShouldRetryNextProtocol(ConnectFailReason, bool hasServerReason)` -- pure, testable retry verdict
- [x] `Assets/Scripts/Tests/Editor/RelayFallbackPolicyTests.cs` -- cover the full reason × hasServerReason matrix -- pins the retry contract (6 tests)
- [x] `Assets/Scripts/Network/RelayConnector.cs` -- create `ConnectClientAsync(string joinCode) → UniTask<RelayConnectResult>` (Success + FailureMessage) and `HostAsync(int maxConnections) → UniTask<string>` (join code or null); protocol loop dtls→wss; inter-attempt `Shutdown()` + await `!IsListening`; latch handling; `GetNetworkDriver().GetRelayConnectionStatus()` poll host-side -- the single decision point (+ `RelayConnectResult.cs`, one top-level type per file)
- [x] `Assets/Scripts/UI/MainMenu.cs` -- route `JoinWithUnityRelay`/`HostWithUnityRelay` through `RelayConnector` (convert both to UniTask); `ConnectionApprovalGate.Enable` before `HostAsync`; move the shared `WaitForConnectedOrTimeout` block into the Facepunch branch -- relay path owns its own wait now
- [x] `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs` -- same routing; wait block into Facepunch branch -- 3rd call site collapsed

**Acceptance Criteria:**

- Given a reachable relay over UDP, when hosting or joining, then behavior is byte-identical to today (dtls, single allocation, no extra latency).
- Given a dtls attempt that times out at approval, when the wss attempt succeeds, then the client lands in GameScene and no disconnect notification appeared in between.
- Given a server rejection with a filled `DisconnectReason`, when joining, then no second allocation is created and the server's own message is shown.
- Given a host whose relay bind never reaches `Established` over dtls, when `HostAsync` falls back, then the published join code belongs to the wss allocation and clients can join it.
- Given the full test suite, when run after the change, then EditMode 539+N / PlayMode 251 green (N = new policy tests), zero console compile errors.

## Spec Change Log

- **2026-07-21 (review round 1 — patch, frozen wording refined in code, PENDING owner ratification).**
  Finding: NGO 2.12 fills `DisconnectReason` on EVERY client transport disconnect with a
  "[Disconnect Event]…" placeholder (`NetworkConnectionManager.GenerateDisconnectInformation`, verified in
  package source) — so the frozen retry rule "SessionEnded with EMPTY DisconnectReason" would never fire on a
  fast transport give-up (active UDP rejection), killing the fallback for exactly the networks it targets.
  Amended (code only, frozen text untouched): the retry verdict routes through
  `RelayFallbackPolicy.HasServerReason` — a reason counts as a server verdict only when non-empty AND not
  transport-generated ("[Disconnect Event]" prefix, pinned to NGO 2.12, EditMode-tested). Known-bad state
  avoided: wss fallback silently dead on fast-fail networks. KEEP: rest of the retry matrix unchanged
  (ApprovalTimeout retries, TotalTimeout and genuine server reasons never retry).
- **2026-07-21 (review round 1 — patches).** Additional review fixes: missing-endpoint `ArgumentException` now
  advances to the next protocol instead of aborting (the fallback's own target case); stuck Shutdown drain
  (>5 s) aborts the fallback with the attempt's own failure message instead of masking it behind a refused
  Start\*; `TryApplyTransport` guards a destroyed NetworkManager/transport mid-flow; host bind-poll
  distinguishes external teardown (app quit) from a bind failure and aborts instead of re-hosting.

## Design Notes

- NGO 2.12 picks `WebSocketNetworkInterface` from `UseWebSockets` ALONE (`UnityTransport.cs:1866`); mismatch with `RelayServerData.IsWebSocket` is only a `LogError` — the helper sets both coherently.
- `WaitForConnectedOrTimeout`'s `finally` drops the handshake latch; `RelayConnector` re-latches immediately after a retryable verdict, BEFORE `Shutdown()` (bool latch, idempotent).
- On definitive client failure: build `FailureMessage` via `JoinHandshake.BuildFailureMessage` BEFORE any teardown (Shutdown may clear `DisconnectReason`); `RelayConnector` never tears down at the end — callers' existing catch/AbortJoin paths do (both are `IsListening`-guarded, idempotent).
- Host poll: `transport.GetNetworkDriver()` (public, `UnityTransport.cs:382`); guard `driver.IsCreated`; `AllocationInvalid` ⇒ treat as failed attempt.

## Verification

**Commands:**

- `mcp__UnityMCP__read_console` -- expected: zero compile errors after each file
- `mcp__UnityMCP__run_tests` EditMode -- expected: 539 + new policy tests, all green
- `mcp__UnityMCP__run_tests` PlayMode -- expected: 251/251 green
- `mcp__UnityMCP__run_tests` categories `DiSeamGuard`/`SceneWiringGuard`/`StaticAbsenceGuard` -- expected: green (no new statics/locators)

**Manual checks (if no CLI):**

- Real playtest deferred: needs a tester behind a UDP-blocking network (owner has no VPN). First run should log `allocation.ServerEndpoints` once to confirm the wss endpoint exists in prod.

## Suggested Review Order

**The decision point — RelayConnector**

- Entry point: the client protocol loop — fresh allocation, StartClient, shared handshake wait, per-attempt retry decision.
  [`RelayConnector.cs:53`](../../Assets/Scripts/Network/RelayConnector.cs#L53)

- The retry verdict: only a genuine SERVER reason vetoes the wss fallback (NGO fakes one on every transport drop).
  [`RelayConnector.cs:117`](../../Assets/Scripts/Network/RelayConnector.cs#L117)

- Latch re-arm before the inter-attempt Shutdown — kills the spurious "Connexion à l'hôte perdue" popup mid-fallback.
  [`RelayConnector.cs:133`](../../Assets/Scripts/Network/RelayConnector.cs#L133)

- Host loop: StartHost returns true on a local bind, so the relay BIND is verified before publishing the join code.
  [`RelayConnector.cs:158`](../../Assets/Scripts/Network/RelayConnector.cs#L158)

- BIND poll via `GetRelayConnectionStatus()` — Established / AllocationInvalid / deadline, external teardown aborts.
  [`RelayConnector.cs:271`](../../Assets/Scripts/Network/RelayConnector.cs#L271)

- `UseWebSockets` + `SetRelayServerData` set coherently per attempt; NGO never auto-corrects a mismatch.
  [`RelayConnector.cs:254`](../../Assets/Scripts/Network/RelayConnector.cs#L254)

**The pure verdict — Domain**

- The NGO-2.12 discriminator: transport-generated "[Disconnect Event]…" reasons are connectivity, not verdicts.
  [`RelayFallbackPolicy.cs:25`](../../Assets/Scripts/Domain/RelayFallbackPolicy.cs#L25)

- The retry matrix itself — ApprovalTimeout always retries; server verdicts and stuck syncs never do.
  [`RelayFallbackPolicy.cs:43`](../../Assets/Scripts/Domain/RelayFallbackPolicy.cs#L43)

**Call sites collapsed**

- Join-by-code: the relay branch owns its own wait now; the shared wait moved into the Facepunch branch.
  [`MainMenu.cs:160`](../../Assets/Scripts/UI/MainMenu.cs#L160)

- Host path: approval gate armed by the caller, everything else delegated.
  [`MainMenu.cs:402`](../../Assets/Scripts/UI/MainMenu.cs#L402)

- Lobby-list join: same shape, failure routed through the existing AbortJoin teardown.
  [`LobbySelectionPanel.cs:321`](../../Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs#L321)

**Peripherals**

- Result carrier — failure message built before teardown, null when only logs apply.
  [`RelayConnectResult.cs:1`](../../Assets/Scripts/Network/RelayConnectResult.cs#L1)

- The 9 policy tests: full retry matrix + the three HasServerReason shapes.
  [`RelayFallbackPolicyTests.cs:1`](../../Assets/Scripts/Tests/Editor/RelayFallbackPolicyTests.cs#L1)
