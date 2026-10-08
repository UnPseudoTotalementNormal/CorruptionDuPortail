# Autoplay bug hunt — night of 2026-10-07 → 08

Goal (Poyo): autoplay until 07:00, find as many bugs as possible (any kind), fix them, then a big table + a PR.
Worktree `bridge-cse_01CrCeVBmaqCtry9M2iaqvw8`, base Dev 27bfc373. Own headless editor + windowed dev builds.

New angles tonight (not covered by the previous nights):
- **Table sizes**: every other campaign played 8 seats; the designer's presets exist for 5..14
  (`tools/autoplay/sweep_table_sizes.py`, new).
- **Unity Gaming Services**: the lobby polling error left open by T2 (`deferred-work.md`), the cloud lobby's
  lifecycle when the host ends the game (`host-leaves-relay`, new).
- Code audit of the open "potential" items from the previous night (N3, N6).

## Findings

### L1 — "Connexion au lobby perdue: Object reference not set…" pops mid-game (Medium, UGS)
The UGS SDK throws a `NullReferenceException` inside `WrappedLobbyService.TryCatchRequest` (line 572: an
`HttpException<ErrorStatus>` whose `ActualError` is null — an error response without a readable body) on a
transient `GetLobbyAsync` failure. `LobbyManager.StartLobbyPolling` caught it OUTSIDE its loop: the first failure
ended the polling for good and raised `OnLobbyError`, which `ClientDisconnectHandler` shows as a notification. In the
T2 session's Relay runs this notification appeared **17 times**, mid-game (day 1 recap and later), on host and clients.
The heartbeat had the same shape (one failed ping → no more heartbeats → the lobby expires ~30 s later).
**Fix:** polling and heartbeat retry inside their loop; a failure is a warning; only 3 consecutive failures stop them,
and the player is told only when no network session is running (in game, the cloud lobby is bookkeeping; the real
link is NGO, whose loss `ClientDisconnectHandler` already reports).

### L2 — "Impossible de quitter le lobby: lobby not found" on every client when the host ends the game (Medium, UGS)
On a graceful end (`ShutOffGameRpc`) the host deletes the cloud lobby while each client removes itself from it. When
the deletion lands first (it did for both clients, `host-leaves-relay` seed 710, FAIL before the fix), the client
logs an error, shows the notification, and keeps `currentLobby` + its polling alive on a deleted lobby.
**Fix:** `LeaveLobby` / `DeleteLobby` forget the lobby (stop polling + heartbeat) whatever the service answers, and
treat `LobbyNotFound` as success (the wanted outcome).

### V1 — 13-14 seats: the third line of cards is off screen (High, gameplay at big tables)
Cards go 6 per line, 2 lines fit above the power bar. At 13-14 seats the third line (2 players) is drawn under the
power bar / skip button and off the bottom of the screen (capture `002-3_AwakeningState_day_1.jpg`, 14 seats,
seed 501): those players' cards cannot be read or clicked. **Fix:** `CardLayout.FitScale` (pure, EditMode-tested):
when the cards need more depth than two full-size lines, cards AND spacings shrink uniformly just enough (a smaller
spacing also fits more per line, wrapping so no card passes the board's right edge: 13-14 cards → 2 lines of 7 at
0.84); set at once, not tweened. 12 seats and fewer are untouched.

### V3 — The next line of cards hides the vote panels (Medium, reported by Poyo during the night)
Measured on `Card.prefab` (card z −4.40…+4.40): the vote panel's text sits at −5.64…−4.39 when shown; on hover the
"Voter" button comes out at −5.52…−4.52 (the text then slides to −6.89…−5.64). With a line spacing of 9 the next line's
top edge is at −4.6: it covered the text and the button. **Fix:** line spacing 10.3 (the next card must start under
−5.64: 10.04 + a gap); up to 12 seats the cards keep their full size, the second line sits 1.3 lower (still on screen).
Proof: `vote-hover-*` captures (new, the voter's view while hovering the card) show every panel clear.

How the value was found (kept because two first attempts were wrong, and the test that caught them matters):
- 11.5 with the grid lifted towards the characters bar: the first line's vote buttons went out of the seated
  first-person view's reach (`real-input-actions`: `input.miss … reason=out-of-reach`, 5 misses).
- 11.5 with the cards shrunk to 0.877 to keep the old depth: intermittent `reason=no-effect` on the far line (5 of 9
  runs). Instrumented (`[VOTEDBG]` logs + the EventSystem raycast stack now journaled with every real click): the button
  WAS the only thing under the reticle, but at that size and distance (~15 units) the click slid off it between press
  and release. A/B on the same build with the Dev layout: 3/3 PASS. Hence full size up to 12 seats.

### V2 — 12+ seats: the characters bar runs past the plank (Medium, visual)
The top bar (faction groups of role portraits, HorizontalLayoutGroup, 1206 px) overflows from 12 seats (5 groups) and
14 seats: the last group is drawn off the plank, over the robot counter panel (the Mask does not clip the portraits'
nested Canvases). **Fix:** after the layout rebuild the row is scaled down to the plank's width when wider.

### R1 — Robot counter: NullReferenceException at every night's end, counter stuck at 0 (Medium)
`RobotBoardInfo` cached `RoleTargetSystem` in its `OnNetworkSpawn`, but the singleton is only set in
`RoleTargetSystem`'s own `OnNetworkSpawn` and in-scene objects spawn in no guaranteed order: when the board spawned
first, the field stayed null all game (13-seat game, seed 508: 8 NREs, one per night; "Nombre de personnes ayant
interagi avec le robot" never updated). It is the only consumer of a spawn-time singleton cached at spawn (audited
ChatManager, BoardManager, ChainingManager, MessageManager, LobbyPlayerInfoHolder, StatesCanvas: all set in `Awake`).
**Fix:** `RoleTargetSystem` publishes its singleton in `Awake` too (like the six others), so it exists before any spawn; the board keeps resolving it in `OnNetworkSpawn` (the lane-C rule pinned by `DiSeamNoLocatorGuardTests`, which caught a first resolve-at-use attempt) and null-guards it.

### N3 — Portal step opened after it was left (Potential → fixed)
`TakeDownThePortalState.WaitForCardsToBeVisible` waits 3 s then opens the Mage's selection without checking the step
is still current (the Mage leaving, or a leave victory, advances the loop meanwhile): it would highlight cards in the
next phase. Same guard as N2 (state index captured before the awaits).

### N6 — `CharactersBar` / `PowerUsageManager` never unsubscribed (Potential → fixed)
`OnDestroy` unsubscribes (same family as N5 `PowersBar`).

### D1 — Vote panel tweens outlive the board (Low)
A client whose host left during the vote logged a DOTween "Tween startup failed (NULL target)" on the way to the menu
(`host-leaves-relay`): the vote panel's `DOAnchorPos` tweens were not tied to their object. **Fix:** `SetLink`
(the B10 / N4 family).

### N12 — Vote panels opened after the vote ended (Potential → fixed, code review)
`VoteState.ActivateVoteUI` awaits the cards' show animation, then wires every vote panel, without checking the vote is
still on (a leave victory or the host ending the game meanwhile): the panels would open on the next phase's board,
wired to a closed vote. Guard on this peer's own `OnEndStateClient` (an epoch counter), not on a replicated value.

### N13 — Ending animation outlives the board (Potential → fixed, code review)
`GameEndingState.GameEndingAnimation` waited 1 s and placed the winners' cards with no tie to the board's lifetime
("Terminer la partie" in that window = NRE on the unloaded board) and no guard for a winner without a character. Both
awaits now end with the board; a missing character is skipped.

### T2 (tool) — new evidence for real clicks
`vote-hover-<voter>-<target>` capture when a real-input voter hovers its target (the voter's own view of the panel);
every `input.click` now carries `stack=[…]`, the EventSystem's own raycast (UI + physics) under the click.

### T3 (tool) — `analyze_rejoin.py` crashed on factice-role knowledge
Since the fake-role hints (10-07), an anomaly's knowledge holds entries like `1>…613 fake=10 role=La Dryade`; the
analyzer split them on spaces and crashed, failing `client-crash-relaunch` (campaign, seed 34) although the rejoin was
perfect. Values with spaces are now kept whole; only numeric levels are compared.

### T1 (tool) — `expect-host-loss` never armed over Relay
The Relay join path did not set `everConnected`, so a client that lost its host reported "session died" instead of
`host.lost`. Fixed.

## Coverage runs (all green once fixed)

| Run | What | Result |
|---|---|---|
| `sweep_table_sizes.py` 5,14,6,12,7,10,9,11,13 (seeds 500-508) | whole host + bots game per seat count | 9/9 completed; 13 seats: 8 NRE (R1); captures showed V1 (14) and V2 (12, 14) |
| same, 13 + 14 after R1/V1/V2 (seeds 508, 509) | | 2/2, 0 error; all 14 cards on the board, bar inside the plank |
| `sweep_random_net.py` 10 games, 5..14 seats, seeds 600-609, every other one over Relay, chat | 2 real clients, whole games, desync check | 10/10: 0 error, 0 desync on 30 processes |
| Relay game seed 1001 (8 seats, chat, to victory) | | 0 error, 0 desync |
| 14 seats, 2 clients, seed 520 (after V1/V2) | client view at the biggest table | 0 error, 0 desync |
| `host-leaves-relay` seed 710 | L2 | FAIL before (2 clients: "lobby not found"), PASS after (race happened on both clients, now silent) |
| `real-input-big-table` seed 780 | real clicks at 14 seats | PASS (seed 777 ended on night 1: Mage chained at night, nothing to vote, not a bug) |
| `real-input-actions` (3 clients, seed 777) after V3 (spacing 10.3, full size) | every vote a real reticle click | 3/3 PASS (before: 11.5 lifted → out-of-reach; 11.5 at 0.877 → 5/9 runs with a lost click; Dev layout A/B 3/3) |
| `real-input-big-table` seed 780 (14 seats, 2 clients) after V1-V3 | | PASS |
| table sizes 12 + 8 (seeds 530-531) after V3 | top-down view: gap between lines, second line still on screen | 2/2, 0 error |
| `campaign.sh 2 1 3` (57 runs: every scenario + 3 random games) on the build with V1-V3, L1, L2, R1, V2, N3, N6, D1 | full regression | **55/57**: the 2 failures are tool-side — `client-crash-relaunch` (T3, analyzer crash, fixed and re-run below) and `real-input-actions` (`lobby-ready … not-laid-out`, the known N11 lobby flakiness; every vote landed) |
| `real-input` random network games, 6/9/11/13/7/14 seats (seeds 640-645) | every action by real clicks at every size | 6/6: 0 error, 0 desync; real clients' only misses = role-picker cards off screen (O2) |
| After the PR: `heavy-loss` at 14 seats, `mass-rejoin` at 14, `net-sync-3clients` at 14, `relay-game` at 13, `rejoin-while-chained` at 12 (3 clients) | network stress at the biggest tables | 5/5 PASS |
| Phase sweeps at 13 seats (`sweep_phases.py`, 4 templates × 7 phases, seeds 860-866) | leave / rejoin / host leave / host crash at every phase of the day at a big table | host leave 7/7, host crash 7/7; leave 6/7 + rejoin 5/7: seed 865 = the Robot wins at the end of day 1 (game over before the reserved seat expires or the rejoin: not covered, legit victory); seed 861 (rejoin at the awakening, FAIL 3/3): same cause — the Robot hacked the Dr Gloubi the template forces everyone to vote, so it wins when he is chained at the end of day 1 (`WOmniscienceHackedCharacter`, design-owned) and the host's run ends before the rejoin completes. Not covered, not a bug |
| Final soak on the PR build: `sweep_random_net.py` 10 games, 5..14 seats, seeds 700-709, every other one over Relay | | **10/10, empty harvest** (0 error, 0 desync, no miss) |
| `replay-net` at 13 seats, `rejoin-at-night` at 14, `real-input-tour` at 14, `full-game-ending` at 5 (2 clients each) | existing regressions at the table-size extremes | 4/4 PASS |

## Tool-side noise seen in the harvest (not game bugs)

- `power.timeout` in real-input runs: the host possesses several bots awake in the same layer; switching to the next
  one closes the previous one's picker (`picker.closed … before the pick`). One screen for several seats: harness only.
- `OperationCanceledException` journaled as `input.error picker`: the driver's own picker wait cancelled at a phase end.
- `lobby-ready … not-laid-out` / `lobby-role-card … no-effect`: the known lobby-tablet flakiness (N11, 10-07).
- Phase templates at 12+ seats: the Robot is in the preset and, when it hacks the forced vote target (Gloubi), wins at
  the end of day 1 — later phases are then not covered. Backlog: a `steal`-like dev seam for the Robot's hack target,
  or `role-holder` keeping the Robot off the forced target.

## Observations for the game designer (not changed)

| # | Observation | Evidence |
|---|---|---|
| O1 | Tablet (InfoTable) at 13+ roles: the role headers break in the middle of words ("Le Repent/i", "Techno/mancie/n", "L'Orph/eline"); the ≥ 9-roles tier (18 px + tooltip) is a design choice, so the typography is left to the GD | `real-input-tour` at 14 seats, `005-tour-tablet.jpg` |
| O2 | Role-picker cards partly off screen at 16:9 (known since 10-05), more often at 14 seats | `input.miss picker … reason=off-screen` warnings |
| O4 | 14 seats, host seat: one vote button of the near line was hidden behind the power bar's 3D models from the seated view (`reason=occluded`, host-seat warning only; the possessed bots vote from the host's seat) | `real-input-big-table`, seed 780 |
| O3 | The local card's "Moi" tag shows mirrored ("ioM") during the first-night flip of one's own card | `002-3_AwakeningState_day_1.jpg` (12 / 14 seats) |

## Big table

| # | Severity | Area | What the player saw / what broke | Found by | Root cause | Status |
|---|---|---|---|---|---|---|
| V1 | **High** | Board, 13-14 seats | The third line of cards (2 players) off screen, under the power bar: those cards could not be read or clicked | `sweep_table_sizes.py` (new), captures | Fixed 6 cards per line, 2 lines visible | **Fixed** (`CardLayout.FitScale`: 2 lines of 7 at 0.84, inside the board's edges); 13/14 seats + `real-input-big-table` PASS |
| V3 | Medium | Board, vote | The next line of cards hid the vote panels (text, and the "Voter" button on hover) | Poyo (during the night), measured on `Card.prefab` | Line spacing 9 < card 8.8 + panel overhang | **Fixed** (spacing 10.3, full size up to 12 seats); `real-input-actions` 3/3, `vote-hover` captures |
| L1 | Medium | UGS lobby | "Connexion au lobby perdue: Object reference not set…" popped mid-game (17× in one evening's Relay runs); polling dead for the rest of the session | T2 deferred item + logs of 10-07 | SDK NRE on a transient error; caught outside the loop + shown to the player | **Fixed** (retry in loop, warning only, notify only without a running session) |
| L1b | Medium | UGS lobby | One failed heartbeat stopped all heartbeats → lobby expired ~30 s later | Code review with L1 | Same shape as L1 | **Fixed** |
| L2 | Medium | UGS lobby | "Impossible de quitter le lobby: lobby not found" on every client when the host ends the game; clients kept polling a deleted lobby | `host-leaves-relay` (new): FAIL 2/2 clients | Host deletes the lobby while clients leave it; `LobbyNotFound` treated as an error and state not cleared | **Fixed**; PASS (race reproduced, now silent) |
| R1 | Medium | Robot counter | NRE at every night's end; "Nombre de personnes ayant interagi avec le robot" stuck at 0 all game | `sweep_table_sizes.py`, 13 seats (8 NREs) | `RoleTargetSystem` singleton published on spawn; in-scene spawn order not guaranteed | **Fixed** (published in `Awake`, spawned instance wins); 13 seats 0 error; EditMode guard + PlayMode Desingleton 45/45 |
| V2 | Medium | Characters bar, 12+ seats | Faction groups ran past the plank, over the robot panel | Captures (12 and 14 seats) | Row wider than the plank; Mask does not clip nested Canvases | **Fixed** (row scaled to the plank width) |
| N3 | Potential | Portal step | Selection could open after the step was left (Mage left / leave victory) | Code review (open item of 10-07) | Await without "still current" check | **Fixed** |
| N12 | Potential | Vote | Vote panels could open after the vote ended | Code review | Same family | **Fixed** (epoch on `OnEndStateClient`) |
| N13 | Potential | Ending | Ending animation could NRE if the game is ended within 1 s; no guard for a winner without character | Code review | Await not tied to the board | **Fixed** |
| N6 | Potential | UI subscriptions | `CharactersBar`, `PowerUsageManager` never unsubscribed | Code review (open item of 10-07) | Missing `OnDestroy` | **Fixed** |
| D1 | Low | Vote panel | DOTween "NULL target" on a client when the host leaves during the vote | `host-leaves-relay` logs | Tweens not linked to their object | **Fixed** (`SetLink`) |
| T1 | Tool | Autoplay Relay | `expect-host-loss` never armed over Relay | `host-leaves-relay` | `everConnected` not set on the Relay join path | **Fixed** |
| T2 | Tool | Autoplay evidence | No view of the vote panel; no EventSystem view of a click | Investigating V3 | — | **Added** `vote-hover-*` captures, `stack=[…]` on every real click |
| T3 | Tool | `analyze_rejoin.py` | Crashed on factice-role knowledge → false FAIL of `client-crash-relaunch` | Campaign | Space in values | **Fixed** |
| O1 | Design | Tablet, 13+ roles | Role headers break mid-word | `real-input-tour` at 14 seats | ≥ 9-roles tier (18 px + tooltip) | Noted for the GD |
| O2 | Design | Role picker | Cards partly off screen at 16:9 (more at 14 seats) | real-input warnings | Known since 10-05 | Noted |
| O3 | Design | Own card | "Moi" tag mirrored during the first flip | Captures | Tag flips with the card | Noted |
| O4 | Design | 14 seats, host seat | A near-line vote button hidden by the power bar's 3D models from the seated view | `real-input-big-table` warning | Geometry at 14 seats | Noted |
