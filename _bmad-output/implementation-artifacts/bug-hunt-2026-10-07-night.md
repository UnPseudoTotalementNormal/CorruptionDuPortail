# Bug hunt 2026-10-06/07 night — new autoplay tests

Goal (Poyo, 2026-10-06 21:30): run NEW autoplay tests overnight, note every problem and error seen, fix only what is
certainly a real bug, keep the rest noted; stop at 07:00 with one big table.

Build under test: Dev `dd16abcf` (PR #109) + the night's tool changes (worktree `bridge-cse_019jTNaPFC1iRJugvHDGaerD`).

## Working notes (filled during the night)

- 21:47 `full-game-victory` (new): whole net game to its victory, 3 clients + chat. PASS seed 301 (anomalies win day 5,
  every seat corrupted), 0 desync, 0 error.
- Environment: disk C: full at start (0 B). Freed 4.2 GB (Visual Studio installer leftovers in %TEMP%). Old
  AutoplayRuns of merged worktrees (~23 GB) NOT deleted (denied): Poyo to clean them.
- 21:49-22:08 chain sweep (new, `sweep_chain_roles.py`: each of the 14 roles held by a real client and chained at the
  first vote, game goes on 2 days): 11 PASS (Occulte, Extension, Technomancien, Croupière, Orpheline, Gloubi, Dryade,
  Repenti, Oracle, Imposteur, Incomplet). Robot: run killed by the machine (see N1). Messager / Chasseuse: one-shot
  hash difference on `corrupted` at `AwakeningState day=1` only, equal again from the next phase, no in-game
  `[DESYNC]` → sampling artefact (T2), not a game desync.
- N1 (environment): 21:53 clients of BOTH parallel runs died together (`Could not allocate memory: System out of
  memory!`, 0xC0000005 / 0x80000003) while the disk was full (no page file growth); the container restarted too.
  From then on: one lane only.
- T2 (tool, fixed): `state.hash` compared character flags during the awakening without input levers; actions landing
  between the host's and a client's sample (1.5 s after each one's phase start) showed as a desync. Flags now left out
  of the awakening hash (the next phase and the in-game tripwire still compare them).
- T3 (tool, new lever): `linger-end <s>` stays on the ending screen, captures `ending`, exports `boardCards` and
  `winners` (`GameEndingState.WinningTeams`, read-only); `analyze_ending.py` checks them on every process.
- 22:17 `full-game-ending` (new): PASS seed 302: chosen win day 7, the 4 processes received the same winners
  `chosen:[0,1,2,3,100,103]`, board = winners. Ending screen observations (capture `097-ending-9.50s.png`): the
  awakening's "Arrêter l'éveil" button is still drawn bottom right on the ending screen (to check, O1); a host-only
  "Terminer la partie" button exists that no test ever clicked (→ new lever `replay`).
- **N2 (GAME BUG, fixed)** 22:21 `last-anomaly-leaves` (new) FAIL seed 640: the Mage (last anomaly, real client)
  leaves on day 2, its 10 s grace expires during `VoteRecapState`, the leave victory re-check jumps to
  `GameEndingState` (chosen win)… and ONE FRAME LATER the game is back in `0:LobbyState` on every process, stuck
  there (run failed after 150 s; 26 000 `CharacterController.Move called on inactive controller` errors from
  `AvatarMovementController:237` in that lobby). Root cause: `VoteRecapState.OnStartStateClientAsync` (and
  `ChainingState.HandleChainingStateClientAsync`, `GameManager.WaitAFrameAndNextGameState`) call
  `NextGameState()` after their awaits without checking they are still the current state; advancing from
  `GameEndingState` (last index) wraps the loop to the lobby. Any out-of-band jump during those async flows hits it
  (a leave victory during the vote recap / a chaining / a portal skip). Fix: those continuations advance only if
  their state is still active (`IsStateActive()` / index unchanged), else log `[LEAVE] … not advancing`.
- 22:23 `rejoin-while-chained` (new): a player chained on day 1 drops on day 2 and tries to rejoin: no seat reserved,
  refused "La partie a déjà commencé.", button hidden. That is what spec-rejoin-01 says ("already chained player
  drops: nothing to reserve") → scenario now checks it, PASS. Design question D1: a chained player may still vote
  (owner ruling) but loses that right for good after any disconnect.
- 22:24 chain sweep, Robot re-run alone: PASS → chain sweep 12/14 PASS, 2 = T2 artefact, 0 game bug.
- **N4 (GAME BUG, fixed)** 22:26 `rejoin-at-chaining` (new) FAIL seed 660: client1 drops as the day-1 chaining
  starts; back in the main menu its chaining animation is still awaiting and runs on the destroyed board:
  `NullReferenceException` in `CardPlayerVisualUpdater.SetChainedOverlay` (from `ChainingState.DoCardChainingAnimation`
  line 124, unobserved UniTask exception) + a DOTween "Tween startup failed (NULL target)". Same family as B10. The
  rejoin itself worked (seat back, intact). Fix: the animation's waits are tied to the board's and the card's
  lifetime (`GetCancellationTokenOnDestroy`, `AttachExternalCancellation`), its tweens `SetLink` to the card; the
  client loop stops when the board is gone and goes on (server still advances) when only that card was rebuilt.
- N3 (potential, NOT fixed, not observed): `TakeDownThePortalState.OnCharacterClickServer` / `OnRoleClickServer` (RPC
  handlers) and `WaitForCardsToBeVisible` (3 s await, then subscribes the Mage's clicks) do not check the portal step is
  still the current state: a late click RPC after an out-of-band jump would call `NextGameState()` from another state
  (same wrap as N2). Guard with `IsStateActive()` when touched.
- O1 (design, not a bug): the awakening's "Arrêter l'éveil" button stays drawn (greyed) in every phase, ending screen
  included.
- 22:26-22:38 `idle-table` (new) PASS: 4 days with nobody acting, every phase ends on its timer, no watchdog alert.
  `full-table-lag-victory` (new) PASS: host + 7 clients, 100 ms / 30 ms / 1 % loss, chat, anomalies win, same
  winners `anomaly:[0,1]` and board on 8 processes, 0 desync.
- Tests: EditMode 581/581. PlayMode 251/252: the explicit full-game test caught a regression in my FIRST N2 fix
  (`IsStateActive()` in `ChainingState`): the loop holds TWO `ChainingState` instances (`ChainingState`,
  `ChainingState2`) and `DoStateMethodRpc` dispatches by type name to the first one, so the flow of the index-9
  chaining runs on the index-5 instance → never "active" → the chaining stalled. Guard changed to a TYPE check on the
  current state (VoteRecapState too). Re-run: the chaining passes; the test stops at `TakeDownThePortalState`, the known
  batchmode limit (`WaitAFrameAndNextGameState` → end of frame never comes), as before tonight.
- Trap worth a project-context rule: a `GameState` must not use `IsStateActive()` from a flow started by
  `DoStateMethodRpc` (dispatch by type name → first instance of the type).
- Queue 2 (build with N2 + N4 fixes, `replay`): `last-anomaly-leaves` seed 640: the grace now expired at day 3's
  awakening, the game went to `GameEndingState` and STAYED there (chosen win, same winners on every process): PASS
  once the check stopped requiring day 2 (re-evaluated: PASS on the fixed run, FAIL on the 22:17 one). The exact
  N2 path (expiry DURING the recap) is forced by the new `last-anomaly-leaves-recap` (time scale 1, quit 1 s into the
  day-2 recap, 2 s grace, checks the host logged the recap's "not advancing").
  `rejoin-at-chaining` seed 660: PASS (no NRE any more) → N4 verified.
- New levers written for host loss: `host-quit-at <phase>` (host leaves by its Leave button = ShutOffGameRpc),
  `host-crash-at <phase>` (host process killed), client `expect-host-loss` (journals `host.lost`, waits for the main
  menu, `host.lost.menu menu=… notification=shown|hidden text=…`, capture `host-lost`, run completed with
  `host-lost=` fact). Scenarios `host-leaves-mid-game`, `host-crash-mid-game`.
- 22:50 `replay-net` / 22:55 `real-input-full-game` (new lever `replay`): the host ended game 1 ("Terminer la
  partie": real click works, `round.shutoff at=ending via=click`), every process went back to the main menu, the host
  hosted game 2 and played it to its end with the 3 clients connected… but the clients reconnected while the host was
  still loading GameScene: NGO `Server Scene Handle already exist!`, clients stuck (known trap, memory
  `reference_ngo_join_during_host_scene_load`, no prod risk since players join a lobby already up). T4 (tool, fixed):
  between rounds a client waits for the host's round-N `session.ready` in the host's journal before reconnecting.
  Real-input misses seen (warnings, known): role-picker cards off screen at 16:9 (`reason=off-screen`), a possessed
  bot's reticle vote `no-effect` on the host.
- 22:58 `client-leaves-at-vote` (regression after the leave-flow fixes): PASS.
- 23:00-23:14 regressions on the fixed build: `client-disconnect-reserved`, `rejoin-at-night`, `mass-rejoin` PASS;
  `full-game-ending` seeds 303 / 304 PASS; `last-anomaly-leaves` seed 641 PASS.
- 23:18 `replay-net` (build with T4): the replay works: both games played to their end on the 4 processes, every
  client reconnected after the host's round-2 `session.ready`, same winners at the second ending, 0 desync.
  One error left → **N5 (GAME BUG, fixed)**: when the host ends the game ("Terminer la partie" → GameScene unloads),
  `NullReferenceException` in `PowersBar.CreatePowerBar` from `Power.OnNetworkDespawn → RebuildOwnerPowerList →
  Character.CheckForPowersLocal → PowersBar.OnPowersUpdated`: `PowersBar` subscribes to `onGameStarted`,
  `onLocalIdentityChanged` and the local character's `onPowersUpdated` / `onRoleUpdated` and never unsubscribes, so a
  destroyed bar is still called while the characters' powers despawn. Fix: `PowersBar.OnDestroy` unsubscribes (B2
  family).
- N6 (potential, NOT fixed, not observed): same missing-unsubscribe family as N5 / B2: `CharactersBar` (Start:
  `onCharactersListUpdated +=`, no `-=`) and `PowerUsageManager` (Start: `powersBar.onPowerClicked`,
  `onLocalIdentityChanged +=`, no `-=`). Harmless as long as the publisher dies with them; to fix when touched.
- 23:20 `last-anomaly-leaves-recap` (new, forced N2 race) PASS seed 642: host log "Reserved seat of 1 expired after
  2 s" → "last anomaly — victory resolved" → "VoteRecapState: no longer the current state when its recap ended — not
  advancing"; trace `VoteRecapState day=2 > GameEndingState day=2` and nothing after. N2 fixed on its exact path.
- **N7 (GAME BUG, fixed)** 23:21 `host-leaves-mid-game` (new) seed 700: the host leaves at day 2's vote, the 3
  clients go back to the main menu WITHOUT the host-lost notification (as expected, `notify=False`), but the host
  throws while its GameScene unloads: `NullReferenceException` in `ChatManager.RevokeChannelServer` from
  `PBoundByInk.OnNetworkDespawn → RevokeInkChannelServer` (the despawn hook added for B9): `CharacterManager.instance`
  is already null (its despawn ran first; despawn order at teardown is arbitrary). Any session end (Leave, "Terminer
  la partie") with an ink channel open hits it. Fix: `RevokeChannelServer` updates the membership but tells nobody
  when the CharacterManager / chat manager is gone; `RevokeInkChannelServer` skips without a chat manager.
- 23:23 `host-crash-mid-game` (new) PASS seed 710: host killed at day 2's awakening; the 3 clients show
  "Connexion à l'hôte perdue" over the main menu (`host.lost.menu menu=reached notification=shown`), no client error.
- 23:24 `late-join-refused` (new): not covered (tool): the 2-day game ended before the newcomer's 70 s delay → 45 s, 4
  days.
- 23:34 `real-input-full-game` (new) PASS seed 680: a whole game by real input (3 clients), ending screen, the host
  CLICKS "Terminer la partie", everyone back to the menu, a second game played and ended; same winners everywhere,
  0 desync, no input error (client misses = warnings only).
- Tests after N5 / N7: EditMode 581/581.
- 23:38-23:41 on the N5 + N7 build: `replay-net` PASS (two games in a row, no error: N5 verified),
  `host-leaves-mid-game` PASS (N7 verified), `late-join-refused` (new) PASS: a newcomer 45 s late is refused
  "La partie a déjà commencé.", the game goes on.
- 23:41 phase sweeps started (`sweep_phases.py`, new): the same scenario at each of 7 phases (intro, awakening, its
  recap, vote, vote recap, chaining, day-2 awakening) for: a client leaving for good, a client rejoining, the host
  crashing, the host leaving.
- 23:41-23:52 phase sweep `phase-leave` (a client leaves for good at each phase, seat chained after 8 s): 6/7 PASS.
  **N8 (GAME BUG, fixed)** at `VoteRecapState day=1` (seed 824): the leaving client's own recap flow kept running on
  its unloaded board → `Erreur dans VoteRecapState.OnStartStateClientAsync: NullReferenceException` in
  `SetVoteCanvasVisibility` (`GetComponentInChildren` on a destroyed card). N4 family. Fix: the recap's waits are tied
  to the board's lifetime (silent stop on cancellation), destroyed cards skipped. Every other phase: leaver chained,
  game goes on, 0 desync, 0 error.
- 23:52-00:03 phase sweep `phase-rejoin` (a client drops at each phase and rejoins by the menu 3 s later): 6/7 PASS,
  seat intact everywhere (analyze_rejoin), 0 desync; the 7th = N8 again at `VoteRecapState day=1` (build without the
  fix): reproduced 2/2.
- 00:03-00:12 phase sweep `phase-host-crash` (host killed at each phase): 7/7 behave right: every client shows
  "Connexion à l'hôte perdue" over the main menu, no client error. One run counted FAIL (intro, seed 840) only
  because client1 was killed by the launcher before writing its report. Host loss is noticed by the liveness after
  ~14-15 s in every phase (`LivenessConfig.Default` = 15 s timeout, by design); the launcher gave clients only 20 s
  after the host's exit. T5 (tool, fixed): 40 s.
- 00:12-00:20 phase sweep `phase-host-leave` (host leaves by its Leave button at each phase): 5/7 PASS (clients back
  to the menu without notification, no error). `VoteRecapState`: N8 on every process (the host's leave unloads every
  board mid-recap). **N9 (GAME BUG, fixed)** at `AwakeningState day=1` (seed 861): on the HOST, 2×
  `NullReferenceException` in `TargetUtils.GetTargetsForCharacters` from `PowersBar.Update → PDroolyHealing.CanUse`:
  `ShutOffGame` resets the session registries (`CompositionRoot.ResetSessionStatics`) right after
  `NetworkManager.Shutdown()`, and the power bar still polls `CanUse` until the scene unloads →
  `CompositionRoot.For(...).GameInfoRevealer` is null. Fix: `PowersBar.Update` does nothing once the session is
  stopping (`ShutdownInProgress`, not listening, or CharacterManager despawned).
- Queue 5 (build 00:21: N8, N9 first guard, T5). Targeted re-checks: `phase-leave` @VoteRecap seed 824 PASS,
  `phase-rejoin` @VoteRecap seed 804 PASS (N8 verified both ways), `phase-host-leave` @VoteRecap PASS (N8 on every
  process verified), `phase-host-crash` @Intro seed 840 PASS (T5 verified). `phase-host-leave` @Awakening seed 861
  still threw once: same teardown window, other poller (`PowerBarObject3D.Update → Power.CanUse`). N9 fix moved to
  the source: `Power.CanUse` returns false once the session is stopping (`ShutdownInProgress` / not listening), the
  `PowersBar.Update` guard kept. EditMode 581/581; PlayMode before that change 251/252 (only the known batchmode
  portal-step limit).
- 00:28-00:51 full phase sweeps on the fixed build, other seeds: `phase-leave` 7/7 PASS, `phase-rejoin` 7/7 PASS.
- 00:51-01:07 `phase-host-leave` 7/7 PASS. `phase-host-crash` 5/7: at both awakenings a CLIENT threw the N9
  `TargetUtils` NRE (host lost → `ClientDisconnectHandler.ReturnToMenu` resets the registries → the power bar still
  polls `Power.CanUse` for a frame). Same path the source-level `Power.CanUse` guard covers (not in that build yet).
- 01:07-01:25 chain sweep again on the fixed build (seed 1100): 14/14 OK (T2 fix verified: no awakening artefact).
- Queue 6 (build 01:26 with the `Power.CanUse` guard): `phase-host-crash` 7/7 PASS, `phase-host-leave` 7/7 PASS
  (N9 verified on host and clients).

## Final table (all problems met tonight)

Status: **Fixed** = code changed and the failing scenario re-run green on the fixed build · **Noted** = not fixed on
purpose (not proven, design, or out of scope) · **Tool** = autoplay / environment, not the game.

### Game bugs

| # | Severity | Area | Problem | Found by | Root cause | Status / proof |
|---|---|---|---|---|---|---|
| N2 | **High** | Game loop / leave | Last anomaly leaves, its grace expires during the vote recap: the game shows the ending one frame, then goes BACK TO THE LOBBY on every screen and stays stuck there (26 000 errors/run) | `last-anomaly-leaves` (new) | Async state flows (`VoteRecapState`, `ChainingState`, `WaitAFrameAndNextGameState`) call `NextGameState()` after awaits without checking they are still current; from `GameEndingState` (last index) the loop wraps to `LobbyState` | **Fixed** (type check on the current state; `IsStateActive()` was wrong: 2 `ChainingState` instances, caught by PlayMode). `last-anomaly-leaves` ×3 seeds + `last-anomaly-leaves-recap` (exact race forced) PASS, FAIL on the old build |
| N7 | Medium | Chat / end of session | Host ends or leaves the game with an ink channel open → `NullReferenceException` in `ChatManager.RevokeChannelServer` | `host-leaves-mid-game` (new) | `PBoundByInk` despawn hook (added for B9) revokes after `CharacterManager.instance` despawned | **Fixed**; `host-leaves-mid-game`, `phase-host-leave` 7/7 PASS |
| N5 | Low | Power bar / end of session | Every host "Terminer la partie" → `NullReferenceException` in `PowersBar.CreatePowerBar` | `replay-net` (new) | `PowersBar` never unsubscribes (no `OnDestroy`); characters' powers despawn after it and call it | **Fixed** (`OnDestroy` unsubscribes); `replay-net` ×2 PASS |
| N9 | Low | Power bar / end of session | Host leaves / host crashes during an awakening → `NullReferenceException` in `TargetUtils` (host, then clients) | `phase-host-leave`, `phase-host-crash` (new sweeps) | Session registries reset before GameScene unloads; power bars still poll `Power.CanUse` | **Fixed** (`Power.CanUse` false while the session stops + `PowersBar.Update` guard); both sweeps 7/7 PASS |
| N8 | Low | Vote recap / leave | A client leaving / dropping (or every process when the host leaves) during the vote recap → `NullReferenceException` in `VoteRecapState.SetVoteCanvasVisibility` | `phase-leave`, `phase-rejoin`, `phase-host-leave` | Recap flow keeps running on the unloaded board | **Fixed** (waits tied to the board's lifetime); recaps re-run PASS, sweeps 7/7 |
| N4 | Low | Chaining / rejoin | A client dropping during the chaining animation → `NullReferenceException` + DOTween error on the destroyed board | `rejoin-at-chaining` (new) | Chaining animation not tied to the board / card lifetime (B10 family) | **Fixed** (cancellation tokens + `SetLink`); `rejoin-at-chaining` PASS |
| B5r | Low | UI tweens | Residual DOTween "Target or field is missing/null" (29 messages in 8 of 114 late runs, mostly at session teardown / rejoin) | `harvest_issues.py` | Tweens without `SetLink` beyond the B5 batch | **Noted** (B5 residue; safe mode catches them) |
| N3 | Potential | Portal step | Portal click RPC handlers and the 3 s card wait do not check the portal step is still current | Code review after N2 | Same pattern as N2 | **Noted** (not observed) |
| N6 | Potential | UI subscriptions | `CharactersBar`, `PowerUsageManager` subscribe without ever unsubscribing | Audit after N5 | Same family as N5 / B2 | **Noted** (not observed) |

### Design questions / observations (game designer's call)

| # | What | Status |
|---|---|---|
| D1 | A chained player who disconnects can never rejoin (spec-rejoin-01: "nothing to reserve"), so they lose their vote for good although chained-but-present players may vote | **Fixed 2026-10-07** (Poyo: fix it): the chained leaver's seat is reserved too, they rejoin still chained; `rejoin-while-chained` now checks the rejoin, PlayMode `ChainedClient_Drops_SeatReserved_RejoinsStillChained` |
| O1 | The awakening's "Arrêter l'éveil" button stays drawn (greyed) in every phase, ending screen included | **Noted** |
| O2 | Host loss is noticed by clients after ~14-15 s (liveness timeout 15 s, by design) | **Noted** |
| O3 | Real input: role-picker cards off screen at 16:9; the vote's Skip button out of the seated reticle's reach (known since 10-05) | **Noted** |

### Tool / environment problems

| # | Problem | Status |
|---|---|---|
| N1 | Disk C: full at start (0 B): two parallel lanes' clients died together (`System out of memory`), the session container restarted | Freed 4.2 GB (VS installer leftovers in %TEMP%); deleting old `AutoplayRuns` of merged worktrees (~23 GB) was refused → **Poyo to clean**; one lane only afterwards |
| T2 | `state.hash` compared character flags during the awakening → false desyncs (actions between samples) | **Fixed** (flags out of the awakening hash); chain sweep 14/14 |
| T3 | No test could see the ending screen (run stopped at `GameEndingState`) | **New lever** `linger-end` + `analyze_ending.py` |
| T4 | `replay`: clients reconnected while the host still loaded GameScene (NGO "Server Scene Handle already exist", known, no prod risk) | **Fixed** (clients wait for the host's round-N `session.ready`) |
| T5 | Launcher gave clients 20 s after the host's exit: too short after a host crash (liveness 15 s) | **Fixed** (40 s) |
| T7 | `role-holder client` accepts ANY client: scenarios needing the role on client1 (the leaver) were met by chance only | **Fixed** (new `role-holder seat:<id>`) |
| T8 | `idle-table` hit the player's own 600 s game timeout (game still progressing) | **Fixed** (`-autoplay-timeout 1400`); re-run PASS |
| T9 | `join-spawn-during-load`: the host's "every client joined" wait counted the bot seated during the load → filled the table before the held client's Character existed ("expected 8 players, got 7") | **Fixed** (wait counts early bots); seeds 94/95/96 PASS |
| N11 | `real-input-lobby` flaky (tablet "Prêt" `not-laid-out` / `no-effect`, Imposé "+" `scroll-stuck` / `app-closed`) on tonight's AND the untouched Dev build (A/B) | **Noted** (tool robustness under load; give the lobby steps more layout time / retries) |
| T6 | PlayMode full-game test stops at the portal step in batchmode (`WaitAFrameAndNextGameState` → end of frame never comes) | **Noted** (known batchmode limit, unchanged) |

### New tests and tools written tonight

Levers: `linger-end`, `replay` (package `IAutoplayRounds`), `host-quit-at`, `host-crash-at`, `expect-host-loss`.
Analyzer: `analyze_ending.py`. Sweeps: `sweep_chain_roles.py` (`--holder client|host|bot`), `sweep_phases.py` + 4
templates (`phase-leave`, `phase-rejoin`, `phase-host-crash`, `phase-host-leave`). Scenarios: `full-game-victory`,
`full-game-ending`, `last-anomaly-leaves`, `last-anomaly-leaves-recap`, `rejoin-while-chained`, `rejoin-at-chaining`,
`idle-table`, `full-table-lag-victory`, `replay-net`, `real-input-full-game`, `host-leaves-mid-game`,
`host-crash-mid-game`, `late-join-refused`. Docs: `tools/autoplay/REFERENCE.md`, skill table, project-context rule
(async state flows must re-check the current state by type).
- 01:43-02:29 queue 6 variety: `replay-net` 691, `full-game-ending` 305/306/307, `full-table-lag-victory` 671,
  `real-input-full-game` 681, `host-crash-mid-game` 711 PASS; chain sweep with the HOST holding each role 14/14 OK.
  Two scenario problems (tool, not game): `last-anomaly-leaves-recap` 643 drew the Mage straight onto client 3
  (`role-holder client` accepts any client, the leaver is client 1 → situation not met); `host-leaves-mid-game` 701:
  the chosen won on day 2 before the day-2 vote where the host was to leave. T7 (fixed): new `role-holder seat:<id>`
  (exact seat), used by the 3 client-1 scenarios; the host now leaves at day 1's vote.
- 02:30 final campaign (every scenario except Relay + 3 random games, final build). Failures so far:
  `idle-table`: the PLAYER's own game timeout (600 s default) ran out at day 4's vote while the game was progressing
  (the 22:36 run had finished just under it) → T8 (scenario, fixed): `-autoplay-timeout 1400`.
  `join-spawn-during-load` (seed 94): host fails "expected 8 players, got 7" right after the clients joined (a bot was
  seated while client1's load was held 25 s): one non-fake Character missing ON THE HOST. PASS on 10-05; not
  reproduced yet → re-run seeds 94/95/96 after the campaign before any theory (N10, open).
  Read of the host's wait: `CountPlayers() >= clients + 1` counted the bot seated during the load, so the host saw
  "every client in" while client1's Character (spawned when its 25 s-held load ends) did not exist yet, filled the
  table and failed 10 s later; timings match (bot +5 s, clients 2/3 +15 s, client1 +25.7 s). T9 (tool race, fixed):
  the wait counts the early bots too. Re-runs queued to confirm.
- Campaign `real-input-lobby` (seed 777) FAIL: host `lobby-scroll reason=scroll-stuck`, then the "Imposé +" click
  for Imposteur missed (`reason=app-closed`, fell back to direct), client3's "Prêt" click `no-effect` (direct
  fallback). Passed on 10-05 / 10-06; nothing tonight touched the lobby → looks like UI timing under load; re-run ×2
  queued (N11, open).
- 02:30-04:24 **final campaign 45/48 PASS** (`AutoplayRuns/campaign-20261007-023022/summary.md`): every older scenario
  (rejoins, joins, chat, lag, picker, real input, watchdog breakage tests, Orpheline, Mage portal, shuffle) and every
  new one, + 2 random build games + 1 random net game, all green except `idle-table` (T8), `join-spawn-during-load`
  (T9) and `real-input-lobby` (N11, re-checks queued).
- 04:24-04:46 re-checks on the T9 build: `join-spawn-during-load` seeds 94/95/96 PASS (T9 verified), `idle-table` PASS
  (T8 verified). `real-input-lobby` A/B: tonight's build 2/5 PASS (777 ×2 FAIL then PASS, 778 FAIL, 779 PASS); the
  untouched Dev build of 21:09 1/2 (777 PASS, 778 FAIL, same client "Prêt" `not-laid-out` / `no-effect` miss) →
  N11 = flaky tool step (UITK tablet layout / role-list scroll under machine load), NOT a regression. Noted.
- 04:47-05:05 `sweep_client_powers.py` on the final build: 13/13 roles OK (every active power used by a real client,
  no timeout, no desync, no error). Final tests: EditMode 581/581, PlayMode 251/252 (T6 only).
- 05:05-05:32 last variety on the final build: `full-game-ending` 308/309/310, `full-table-lag-victory` 672,
  `replay-net` 692 PASS; day-2 phase sweeps (awakening recap, vote, vote recap): rejoin 3/3, leave 3/3, host crash
  3/3 PASS.

### State of the worktree (nothing committed — Poyo's OK needed)

- Game fixes: `GameManager.cs` (N2), `VoteRecapState.cs` (N2, N8), `ChainingState.cs` (N2, N4), `PowersBar.cs` (N5,
  N9), `Power.cs` (N9), `ChatManager.cs` + `PBoundByInk.cs` (N7), `GameEndingState.cs` (read-only `WinningTeams`).
- Autoplay: package `AutoplayRunner.cs` / `IAutoplayGame.cs` (`replay`, `IAutoplayRounds`), `launch-net.ps1` (T5);
  adapter `CdpAutoplayGame.cs`, `AutoplayDriver.cs`, `AutoplayDriverLobby.cs`; tools, scenarios, templates, docs.
- Editor import noise, NOT to commit: `FMODStudioCache.asset`, `PC_RPAsset.asset`,
  `UniversalRenderPipelineGlobalSettings.asset`, `LiberationSans SDF - Fallback.asset`, `GraphicsSettings.asset`,
  `ProjectSettings.asset`, `fmod_editor.log`, `__pycache__/`.
- Runs: `AutoplayRuns/` of this worktree (~2 GB, gitignored); `AutoplayRuns/night-summary.log` = every verdict of the
  night in order.
- 05:32-05:55 last queue: `phase-host-leave` day-2 phases 3/3, `full-game-ending` 311/312, `real-input-full-game` 682,
  `replay-net` 693, `last-anomaly-leaves-recap` 644: all PASS.
- 05:55-06:19 random full games (no day limit) on the final build: 3 host + 7 bots, 3 network games with 5 real
  clients: 6/6 PASS.

### Totals

260 autoplay runs tonight (host + bots and network up to 8 processes). Final build: campaign 45/48 (3 = tool, 2 fixed
and re-verified, 1 flaky on the untouched Dev build too), 4 phase sweeps 7/7 + 4 day-2 sweeps 3/3, chain sweep
14/14 (client) + 14/14 (host), client powers 13/13, 6/6 random full games, EditMode 581/581, PlayMode 251/252 (T6).
Every game bug found tonight (N2, N4, N5, N7, N8, N9) is fixed and its scenario green on the final build.
