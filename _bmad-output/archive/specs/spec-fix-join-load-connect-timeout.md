---
title: 'Fix joining client kicked during slow GameScene load (decouple handshake vs sync timeout)'
type: 'bugfix'
created: '2026-07-13'
status: 'done'
baseline_commit: 'af081af8c18e933a28b9f5d7ef0e009aeb7cea79'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/join-load-timeout-kick-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A joining client on a slow PC is torn down before it finishes loading `GameScene`. The single client-side connect deadline `ConnectTimeoutSeconds = 10s` in `MainMenu.WaitForClientConnectedOrTimeout` is gated on FULL NGO scene synchronization (`IsConnectedClient` flips true only at `SynchronizeComplete`); when the load exceeds 10s the client throws "Connection timed out" and `Shutdown()`s itself. (Confirmed root cause — not the liveness heartbeat.)

**Approach:** Split the one deadline into two phases keyed on the public `NetworkManager.SceneManager.OnSynchronize` event (fires client-side once the server has approved the client and started syncing): a SHORT approval deadline that only applies until sync starts (fast-fail a dead/silent host), then a GENEROUS total deadline that lets an honest slow load finish. Extract the phase decision into a pure Domain POCO with EditMode tests; keep the NGO polling seam thin in `MainMenu`.

## Boundaries & Constraints

**Always:** Keep the existing fast-fail paths intact — a server rejection (`DisconnectReason`, e.g. "La partie a déjà commencé.") and a torn-down NetworkManager (`!IsListening` / null) must still fail immediately with the existing wording. Unsubscribe the `OnSynchronize` handler in a `finally` (no subscription leak). Decision logic must be a pure, engine-free POCO (mirror `LivenessThreshold`/`LivenessPumpPolicy`).

**Ask First:** Changing the chosen timeout values (approval 10s, total 90s). Touching the transport `DisconnectTimeoutMS` (12000) or `LivenessConfig`.

**Never:** Do NOT change the liveness layer, `LivenessConfig`, or transport config — they are not the cause. Do NOT touch the host path (host drives the load itself and never waits). Do NOT make the wait unbounded. No `System.Threading.Tasks.Task` (UniTask only).

## I/O & Edge-Case Matrix

Pure decision `ConnectHandshakePolicy.Evaluate(elapsedSeconds, syncStarted, isConnected, sessionAlive, approvalDeadline, totalDeadline)` → `{ Connected, Waiting, Failed(reason) }`:

| Scenario | Input / State | Expected Output | Error Handling |
|----------|--------------|-----------------|----------------|
| Connected fast | isConnected=true | Connected | N/A |
| Session ended (reject/teardown) | sessionAlive=false | Failed(SessionEnded) | caller surfaces DisconnectReason |
| Dead/silent host | syncStarted=false, elapsed≥approvalDeadline | Failed(ApprovalTimeout) | "host did not respond" |
| Slow honest load | syncStarted=true, elapsed=45s (<total), !connected | Waiting | keep waiting |
| Stuck sync | syncStarted=true, elapsed≥totalDeadline, !connected | Failed(TotalTimeout) | "load took too long" |
| Sync just started, pre-approval-deadline | syncStarted=true, elapsed=3s | Waiting | approval deadline no longer applies |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/UI/MainMenu.cs` -- client join flow; `ConnectTimeoutSeconds` const (:58), `WaitForClientConnectedOrTimeout` (:209-237), fast-path + throw/teardown caller (:160-182). ONLY the client path.
- `Assets/Scripts/Domain/LivenessThreshold.cs` / `LivenessPumpPolicy.cs` -- pattern reference: pure static Domain decision POCO + XML docs (C# 9, no record).
- `Assets/Scripts/Tests/Editor/LivenessThresholdTests.cs` -- pattern reference; its Editor test asmdef already references `CorruptionDuPortail.Domain`.
- NGO signals (read-only, verified): `NetworkSceneManager.OnSynchronize(ulong)` public event = server started syncing us; `IsConnectedClient` flips at `SynchronizeComplete`.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Domain/ConnectHandshakePolicy.cs` -- NEW pure static POCO: `ConnectWaitState Evaluate(double elapsedSeconds, bool syncStarted, bool isConnected, bool sessionAlive, double approvalDeadlineSeconds, double totalDeadlineSeconds)` returning an enum/struct `{ Connected, Waiting, Failed }` + a `ConnectFailReason { SessionEnded, ApprovalTimeout, TotalTimeout }`. Precedence exactly per the I/O matrix. -- isolates testable timing logic from NGO.
- [x] `Assets/Scripts/UI/MainMenu.cs` -- replace `ConnectTimeoutSeconds` with `ApprovalTimeoutSeconds = 10f` + `SyncTotalTimeoutSeconds = 90f`; rewrite `WaitForClientConnectedOrTimeout` to: capture a wall-clock start (`Time.realtimeSinceStartupAsDouble`), subscribe `NetworkManager.Singleton.SceneManager.OnSynchronize` (set a local `_syncStarted` flag; guard null SceneManager), poll each frame via `UniTask.Yield`/`WaitUntil` feeding `ConnectHandshakePolicy.Evaluate`, return on `Connected`/`Failed`, unsubscribe in `finally`. Map `TotalTimeout` to a distinct "load took too long" message; keep DisconnectReason preference for the session-ended/approval cases. -- the thin engine seam. (also removed now-unused `using System.Threading;` + added `BuildJoinFailureMessage`.)
- [x] `Assets/Scripts/Tests/Editor/ConnectHandshakePolicyTests.cs` -- NEW EditMode tests covering every I/O matrix row + precedence (connected-wins-over-timeout, session-ended-wins-over-waiting). 11 tests. -- lock the decision core.

**Acceptance Criteria:**
- Given a joining client whose GameScene sync takes 45s (sync started), when it loads, then it completes the join and is NOT kicked (previously kicked at 10s).
- Given a dead/silent host (no approval, no `OnSynchronize`), when a client joins, then it fails within ~`ApprovalTimeoutSeconds` with the existing timeout/DisconnectReason wording.
- Given the server rejects the join (game in progress), when the client attempts, then it fails immediately surfacing `DisconnectReason` (unchanged behavior).
- Given the EditMode suite, when run, then `ConnectHandshakePolicyTests` pass and no other test regresses.

## Design Notes

Precedence in `Evaluate` (first match wins): `isConnected` → Connected; `!sessionAlive` → Failed(SessionEnded); `!syncStarted && elapsed ≥ approvalDeadline` → Failed(ApprovalTimeout); `elapsed ≥ totalDeadline` → Failed(TotalTimeout); else Waiting. `syncStarted` merely CANCELS the approval deadline; the total deadline is an absolute safety cap so a genuinely stuck sync can't hang forever.

Subscribe to `OnSynchronize` at the very top of the wait (runs right after `StartClient()`); a network `Synchronize` needs a server round-trip so it cannot arrive before the sub. If the SceneManager is momentarily null, guard and treat `_syncStarted=false` until subscribed — the total deadline still bounds the worst case. Elapsed is evaluated only on frames that run; after a multi-second freeze the first resumed frame sees true wall-clock elapsed and decides correctly (matches the old real-timer semantics).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after edit) -- expected: zero compile errors; `ConnectHandshakePolicy` type resolves.
- `mcp__UnityMCP__run_tests` (EditMode, filter `ConnectHandshakePolicy`) -- expected: all new tests green.
- `mcp__UnityMCP__run_tests` (EditMode full) -- expected: no regression vs baseline.

**Manual checks (real MP, Poyo — per no-playtest-by-Claude):**
- 2 builds, join from a slow client (or artificially heavy GameScene): client seats in lobby instead of being kicked mid-load; dead-host join still fails fast.

## Suggested Review Order

**Decision core (start here)**

- Entry point — the pure two-phase decision; read the precedence, it is the whole fix.
  [`ConnectHandshakePolicy.cs:81`](../../Assets/Scripts/Domain/ConnectHandshakePolicy.cs#L81)

- The result struct handed back each poll (C# 9, IEquatable, no record).
  [`ConnectHandshakePolicy.cs:35`](../../Assets/Scripts/Domain/ConnectHandshakePolicy.cs#L35)

**Engine seam (the behaviour change)**

- The rewritten wait: realtime clock + OnSynchronize sub → policy → unsubscribe in finally.
  [`MainMenu.cs:209`](../../Assets/Scripts/UI/MainMenu.cs#L209)

- The load-bearing signal: subscribe to NGO OnSynchronize to flip `_syncStarted`.
  [`MainMenu.cs:233`](../../Assets/Scripts/UI/MainMenu.cs#L233)

- Caller: bool → ConnectFailReason; distinct message per phase.
  [`MainMenu.cs:166`](../../Assets/Scripts/UI/MainMenu.cs#L166)

- Two constants replacing the single 10s deadline.
  [`MainMenu.cs:63`](../../Assets/Scripts/UI/MainMenu.cs#L63)

**Tests (last)**

- 11 EditMode rows: matrix + precedence + inclusive boundaries.
  [`ConnectHandshakePolicyTests.cs:1`](../../Assets/Scripts/Tests/Editor/ConnectHandshakePolicyTests.cs#L1)
