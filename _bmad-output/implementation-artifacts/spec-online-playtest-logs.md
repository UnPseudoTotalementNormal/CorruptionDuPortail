---
title: 'Online playtest logs: structured game log streamed live to Firestore, remote kill switch, Claude reader'
type: 'feature'
created: '2026-10-20'
status: 'done'
baseline_commit: 'bee0938763fb8ac3bb194d2b4455a77792c24fdc'
context: ['{project-root}/_bmad-output/project-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Playtest bug reports (Wouh, Discord) carry only the tester's words and screenshots; the game's own log stays on his PC and is overwritten next launch, so Claude can't see what really happened.

**Approach:** Playtest builds write a structured, context-rich event log (named events + auto context + every Unity log/exception + phase state snapshots), keep it locally and stream it live (~20 s batches) to a dedicated Firebase project; a remote switch turns it off instantly, a compile define strips it from public builds; `tools/logs/logs.py` lets Claude find a game and read a merged, filtered timeline.

## Boundaries & Constraints

**Always:**
- Firebase project `corruption-du-portail` (Blaze, europe-west1): Firestore + anonymous Auth via REST (`UnityWebRequest`, no Firebase SDK).
- Compiled in only when `CDP_PLAYTEST_LOGS` is defined; `CdpBuild` adds it to CI playtest builds (Relay + Steam), never ProjectSettings. Every logging entry point is `[Conditional("CDP_PLAYTEST_LOGS")]`, so call sites vanish (arguments included) from builds without it.
- Remote `config/logging` gates everything: read before the first upload and every 60 s; `enabled:false` stops uploads within 60 s and drops the pending queue; unreadable config = local file only.
- Autoplay runs (`-autoplay`) and the Editor never upload.
- Host and every client share one `gameId` per game (server-written NetworkVariable set when leaving the lobby).
- Chat messages logged in full (playtest decision, 2026-10-20).
- Logging never throws into gameplay, never allocates per frame, never blocks the main thread on network (UniTask).
- Firestore TTL (`expireAt`, 60 days) on every log document.

**Ask First:** any new Firebase product or paid quota; logging anything outside the game (files, other processes); changing the security rules to allow client reads.

**Never:** Firebase Unity SDK; logs in Editor/autoplay uploads; a bot token or admin credential in the client; logging from Domain (pure); per-frame events; F8 bug-report window (separate spec after mockups).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Live stream | playtest build, config enabled | batch doc every ~20 s + session index doc updated (lastSeen, counts) | N/A |
| Kill switch | `enabled:false` while playing | no upload after next config poll, queue dropped, local file kept | N/A |
| Offline | no network / auth fails | local NDJSON keeps everything; retry with backoff | never surfaces to player |
| Crash | game killed mid-session | next launch uploads the previous session's unsent tail, marked `crashed:true` | N/A |
| Spam | same line ×500 in 2 s | one line + `repeat:500` | per-category cap per minute, `log.dropped` counter |
| Verbose player | player id in `verbosePlayers` | Debug-level events uploaded for that player only | N/A |
| Big batch | > 900 KB | split into several docs (Firestore 1 MiB cap) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Editor/Build/CdpBuild.cs` -- `extraScriptingDefines`: add `CDP_PLAYTEST_LOGS` (`-cdpLogs off` to omit).
- `Assets/Scripts/GameLogic/GameManager.cs` -- `SwitchGameState` (phase transitions + snapshot), `onPlayerRejoinedServer`, new `gameId` NV.
- `Assets/Scripts/Characters/Powers/Power.cs` -- `StartUse`/`Cancel` (client), `OnUsedServer`, `EmitVerdictServer` (refusals).
- `Assets/Scripts/Characters/Powers/Runtime/EffectDispatcher.cs` -- every executed effect.
- `Assets/Scripts/GameLogic/GameStates/VoteState.cs` -- `OnPlayerVotedRpc`.
- `Assets/Scripts/ChatSystem/ChatManager.cs` -- `SendChatMessageServerRpc`.
- `Assets/Scripts/Network/ConnectionApprovalGate.cs` -- approve/reject (+reason), synchronized, left; `ClientDisconnectHandler.cs` client side.
- `Assets/Scripts/Board/Card.cs`, `Board/UI/Board3DButton.cs`, `GameLogic/StateUI.cs` -- player clicks, screens shown.
- `Library/PackageCache/com.unpseudo.autoplay*/Runtime/AutoplayCommandLine.cs` -- `IsRequested(args)` to detect autoplay.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Diagnostics/Logs/` (new, `Game` asmdef) -- `LogEntry`, `LogBuffer` (dedup, per-category caps, batching, NDJSON), `RemoteLogConfig` (parse/defaults), `FirestoreJson` (typed-value encoding) as pure classes; `GameLog` static facade (`Event(cat, name, params (string,object)[])`, `Snapshot`, `Mark`) with `[Conditional]`; `GameLogContext` (session/game/seat/host/phase/night/role/version/flavor auto-attached) -- core, testable without network.
- [x] `Assets/Scripts/Diagnostics/Logs/PlaytestLogRuntime.cs` -- `#if CDP_PLAYTEST_LOGS` bootstrap (`RuntimeInitializeOnLoadMethod`): skip if Editor/autoplay; hardware + session start; `Application.logMessageReceivedThreaded` (all levels, stack for Error/Exception); perf sample every 30 s (fps avg/min, memory, RTT); local file `persistentDataPath/logs/` (keep 10 sessions); upload loop (anonymous sign-in + refresh, chunk POST, index PATCH, config poll, crash-tail recovery).
- [ ] Call sites listed in Code Map -- one `GameLog.Event` per meaningful action; host `GameLog.Snapshot` at each phase start (alive/chained, seats→roles, votes, night number).
- [x] `firebase/` (new: `firebase.json`, `firestore.rules`, `firestore.indexes.json`) -- rules: authed create on `sessions/{sid}` and `sessions/{sid}/chunks/*` with uid match and size caps, update index only own, no client read except `config/logging`; deploy + TTL policy + initial `config/logging`.
- [x] `tools/logs/logs.py` + `tools/logs/README.md` -- stdlib, token from `gcloud auth print-access-token`; `list [--player --since --game]`, `timeline <game|session> [--level --cat]` (merged by time, seat-prefixed), `around <time> [±s]`, `errors`, `grep`, `tail` (live); compact one-line output; local cache.
- [x] `Assets/Scripts/Tests/EditMode/PlaytestLogs/` -- tests for LogBuffer dedup/caps/split, config parsing (missing/disabled/verbose), Firestore encoding, NDJSON round-trip.
- [x] `_bmad-output/project-context.md` -- rule: new gameplay action → `GameLog.Event`, never inside Domain; define only via CdpBuild.

**Acceptance Criteria:**
- Given a CI playtest build in a 3-player game, when it ends, then `logs.py timeline <game>` shows all three players' events interleaved with phase snapshots, votes, powers (incl. refusals and reasons), chat, connections.
- Given a build without `CDP_PLAYTEST_LOGS`, when inspected, then no `GameLog` call site, Firebase URL or API key is in the player assemblies.
- Given the Editor or an autoplay run, when played, then nothing reaches Firestore.

## Spec Change Log

### 2026-10-20 — review round 1 (blind hunter, edge case hunter, acceptance auditor), all findings patched

- Upload integrity: the byte range of a chunk is saved (`pendingLength`) before sending and resent unchanged after a
  lost answer, so `ALREADY_EXISTS` (409, verified on the live rules) really covers those bytes; one upload pass at a
  time (`SemaphoreSlim` shared with the quit upload, which cancels the loop first); a range that can never be accepted
  (one line over the cap, a `Rejected` chunk) is skipped and logged instead of blocking the session; meta saved
  atomically; recovered sessions retried only on passing failures; pruning keeps unsent sessions (up to 30).
- Auth: one anonymous account per PC (sign-up behind a lock file, `auth.json` replaced atomically), a new account only
  when the refresh token is really dead (400 INVALID_REFRESH_TOKEN…), 403 no longer drops the token, a session written
  under another uid is given up (`log.session.orphan`).
- Switch: missing doc or unreadable content = off; unreachable 3 min = unknown (local only); polled every ~50 s and
  before each chunk; off at quit drops the session's waiting lines; `flushSeconds` 5..60, `capPerMinute` 50..5000;
  backoff on failures (up to 300 s).
- Autoplay: a session's `uploadAllowed` is stored, a local-only run is never recovered by a later launch.
- Game safety: snapshot/context/argument computation wrapped in `GameLog.Guarded` (a logging bug can never abort
  `SwitchGameState`); context = seat (ulong, fakes whole), role, phase, day (also on clients: refreshed on
  `onNewDayPassed`), game id set before the phase on spawn, declared before `currentGameStateIndex`, cleared when back in
  the lobby and on despawn; seat refreshed on rejoin / possession; client-side `client.started/stopped`.
- Buffer: errors never capped; identical lines merge across drains (last line held back while it can grow); minute
  never rolls back; parameters capped (still JSON).
- Build: `-cdpLogs on|off` mandatory (anything else fails), Build.yml passes `on`.
- Rules: field allow-lists, sizes, bounded expiry, seq/path match, chunks write-once, only `config/logging` readable;
  `tools/logs/check_rules.py` checks them live.
- logs.py: lobby lines in game timelines, clock-skew correction, `--utc`, midnight-safe `around`, paginated sessions,
  incremental cache + `batchGet`, `tail` queries only new chunks, token refresh, `switch` on a missing doc / clearing
  lists, `list --game`, role in the line label.
- Added `GameLog.Mark` (for F8) and the `playtest-logs` skill.
- KEEP: `[Conditional]` facade + `#if` runtime (verified: no log string, URL or key in a build without the define).
- Open, human decision: `-playtest-logs-upload` lets an autoplay run upload (used to verify the pipeline end to end;
  sessions marked autoplay, hidden from `list`). The frozen Never says autoplay never uploads: keep the switch or drop it.
- Tests live in `Assets/Scripts/Tests/Editor/PlaytestLogs` (the project's EditMode folder).

## Design Notes

Event line (NDJSON, short keys keep Claude's reads cheap):
`{"t":"21:40:12.345","s":3,"ph":"Vote","n":2,"lv":"I","c":"vote","e":"vote.cast","p":{"from":3,"to":5}}`
Firestore: `sessions/{sid}` index {uid, player, steamId|ugsId, version, flavor, host, gameIds[], start, lastSeen, errors, warnings, crashed, expireAt}; `sessions/{sid}/chunks/{seq}` {seq, t0, t1, gameId, lines (NDJSON string), expireAt}. Timeline merge = query chunks by `gameId` across sessions (collection-group index).

## Verification

**Commands:**
- `tools/autoplay/unityctl.sh compile` -- expected: 0 errors with and without `CDP_PLAYTEST_LOGS`.
- `tools/autoplay/unityctl.sh editmode PlaytestLogs` -- expected: all green.
- `firebase deploy --only firestore --project corruption-du-portail` -- expected: rules + indexes deployed.
- `python tools/logs/logs.py list --since 1h` after a smoke upload from a dev build with the define -- expected: session listed, timeline readable.
