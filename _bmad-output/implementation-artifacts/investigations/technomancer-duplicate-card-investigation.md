# Investigation: Technomancer card + role-bar icon duplicated on Mage Occulte client

## Hand-off Brief

1. **What happened.** During a multi-instance solo playtest, the Mage Occulte client (only) displayed the technomancer player ("saazz") twice — duplicate table card AND duplicate role-bar icon — meaning that client's replicated `networkedCharacters` list (CharacterManager) held the same character entry twice from game start.
2. **Where the case stands.** Root-cause **class** Deduced with high support: both UI surfaces + targeting funnel through the single `GetCharacters()` cache, itself a pure projection of the client's `NetworkList` replica — the duplicate therefore lives at the NGO replication layer, client-local. Exact trigger Hypothesized: NGO initial-sync + same-window delta double-delivery during near-simultaneous multi-instance joins; NGO was bumped 2.6.0 → 2.12.0 ten days before the incident (96b0a1f).
3. **What's needed next.** Ask the tester whether the bugged (Mage Occulte) instance was the host or a late joiner (host would refute the sync-race), collect its Player.log, and add a cheap dedup tripwire in `RebuildCharactersCache`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-07 (incident 2026-07-03 15:03, Discord report by "Wouh, Le Fantôme") |
| Status           | Active                                                                     |
| System           | Unity 6000.5.0f1 (recent upgrade from 6000.2.6f2), NGO + Facepunch, multi-instance build playtest on one PC (Windows) |
| Evidence sources | Tester screenshot (1 frame), Discord report, source code, git log          |

## Problem Statement

Tester report (verbatim, Discord 03/07/2026): "Uniquement pour le mage occulte, le technomancien a été dédoublé avec une double carte et un double icone (les autres n'ont que 3 joueurs in game) je n'ai eu besoin que de cibler l'un des technomanciens dédoublés pour gagner". User adds: frequent instability-like problems, but intermittent — only sometimes.

User hypothesis (registered as H0): "general instability" — vague; to be refined or refuted.

## Evidence Inventory

| Source   | Status                          | Notes     |
| -------- | ------------------------------- | --------- |
| Screenshot (Awakening/Éveil screen, Mage Occulte client) | Available | 4 player cards for 3 players: "sqgsd" (Va'ahl, Le Mage Occulte, badge Moi), "Yo" (Le Robot), "saazz" ×2 (face down, identical). Top bar: CHOSEN role card duplicated, ANOMALY ×1, MARGINAL ×1. Counters "interagi avec le robot: 1", "corrompus 1/2". Timer 294. |
| Player.log from bugged instance | Missing | Not collected |
| Deterministic repro | Missing | Intermittent |
| Source code | Available | To trace |
| Git history | Available | Recent Unity 6.5 upgrade (96b0a1f) |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | How the Awakening screen builds its player card list | High | Done | `AwakeningState.cs:224` → `BoardManager.ShowAllPlayerCards` → one card per `GetCharacters()` entry |
| 2 | How the top faction/role bar builds its icons | High | Done | Built ONCE at `GameIntroductionState.cs:65` from `GetCharacters()` → duplicate present since game start |
| 3 | CharacterManager registry/cache — can a character be listed twice on a client? | High | Done | Cache is a pure projection of the `NetworkList` replica; no in-project path adds a duplicate (guard at `CharacterManager.cs:402`) |
| 4 | Multi-instance/reconnect path — server double-spawn for same player | Medium | Done (deprioritized) | Would replicate to ALL clients — contradicts "others see 3" |
| 5 | NGO 2.12 NetworkList initial-sync/delta duplication (regression or edge case) | High | Open | Needs host/joiner answer + Player.log + repro or NGO issue tracker |
| 6 | Was the bugged instance host or late joiner? | High | Open | Discriminator for H2 — ask tester |

## Timeline of Events

| Time | Event | Source | Confidence |
| ---- | ----- | ------ | ---------- |
| 2026-07-03 15:03 | Bug observed + screenshot, multi-instance playtest | Discord | Confirmed |
| 2026-06-30 (approx) | Unity 6000.5.0f1 upgrade merged (PR #64) | git 322d6b1/96b0a1f | Confirmed |

## Confirmed Findings

### Finding 1: Duplication is client-local and consistent across two independent UI surfaces

**Evidence:** Screenshot; tester statement "les autres n'ont que 3 joueurs in game".

**Detail:** Both the table card list AND the faction role bar duplicate the same player, on one client only. Two independent renderers agreeing on the duplicate ⇒ the shared data source they both read contains the duplicate; not a UI-instantiation double-fire in one widget.

### Finding 2: Both UI surfaces are pure projections of `CharacterManager.GetCharacters()`

**Evidence:** `Assets/Scripts/Board/BoardManager.cs:169` (`ShowAllPlayerCards` — one `AddNewCard` per `GetCharacters().Where(!isFake)` entry; cards fully destroyed then recreated per state, `BoardManager.cs:166,210-213`); `Assets/Scripts/GameLogic/GameStates/GameIntroductionState.cs:65` (`charactersBar.ResetCharactersBar(CharacterQuery.GetCharacters())` — bar built ONCE per game, destroy-then-rebuild, `CharactersBar.cs:141-146`).

**Detail:** Neither surface can invent an entry: the bar is rebuilt from scratch (children destroyed first), the cards are destroyed then re-instantiated per state. A duplicate in both ⇒ `GetCharacters()` returned the entry twice — at GameIntroduction time (bar) AND still at Awakening time (cards). The duplicate is **persistent**, not a transient race in one animation.

### Finding 3: `GetCharacters()` is a straight projection of the replicated `NetworkList`

**Evidence:** `Assets/Scripts/Characters/CharacterManager.cs:144-163` (`RebuildCharactersCache`: `Clear()` then one append per `networkedCharacters` entry that resolves; unresolved refs are *skipped*, never duplicated); `CharacterManager.cs:312-325` (`GetCharacters` returns a defensive copy).

**Detail:** The cache can only *lose* entries (unresolved refs), never gain one. A duplicate in the cache requires a duplicate entry in that client's `networkedCharacters` replica (`NetworkList<NetworkBehaviourReference>`, `CharacterManager.cs:171`).

### Finding 4: No in-project code path can add a duplicate server-side for the same clientId

**Evidence:** `CharacterManager.cs:400-432` (`AddNewCharacter` guards `_characters.Any(ownerClientId == _clientId)`; on the server the just-spawned refs always resolve so the guard reads a complete cache); writers of `networkedCharacters` are only `AddNewCharacter` (`:424`) and `RemoveCharacter` (`:444-450`); characters added on connection at `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:15-22,60-63`.

**Detail:** A server-side double character (e.g. reconnect leaving a stale entry) would replicate to **all** clients — contradicted by "the others see 3 players". Deprioritized.

### Finding 5: NGO was bumped 2.6.0 → 2.12.0 ten days before the incident

**Evidence:** git `96b0a1f` (2026-06-23, merged PR #64 `322d6b1`) — `Packages/manifest.json`: `com.unity.netcode.gameobjects 2.6.0 → 2.12.0` (Multiplayer Tools paired 2.2.6 → 2.2.8). Incident: 2026-07-03.

**Detail:** The replication layer that owns the suspect `NetworkList` changed 6 minor versions right before the playtest. NGO history contains exactly this bug class: fix shipped in 1.12.0 — "when initially synchronizing a client, if a NetworkVariable has a pending state update it will serialize the previously known value(s) … so the pending updates aren't duplicate values on the newly connected client side"; also open issue #2454 "NetworkList contains duplicate entries when its NetworkObject becomes visible to Client". Nothing in the 2.7–2.12 changelog explicitly re-fixes or mentions this path.

### Finding 6: Characters are added to the list exactly at client-connection time

**Evidence:** `LobbyState.cs:15-17` (`OnClientConnected → AddNewCharacter`).

**Detail:** In a multi-instance solo playtest the user joins all instances back-to-back, so one instance's connection **synchronization window overlaps** the server-side list-add triggered by another instance's join — precisely the initial-sync + pending-delta window of the NGO 1.12-class bug.

## Deduced Conclusions

### Deduction 1: The duplicate lives in the Mage Occulte client's `networkedCharacters` replica, at the NGO replication layer

**Based on:** Findings 1, 2, 3, 4.

**Reasoning:** Both UIs project `GetCharacters()` (F2); the cache cannot invent entries (F3); a server-side duplicate would be visible to everyone (F4) but others saw 3 players. Also consistent: both duplicate cards displayed the same pseudo "saazz" (pseudo resolved per clientId) and targeting either duplicate satisfied the win condition — same clientId behind both entries.

**Conclusion:** That client's `NetworkList` replica held the technomancer's entry twice, from before GameIntroduction until at least the Awakening — a client-local replication divergence, not a game-logic bug.

## Hypothesized Paths

### Hypothesis 0 (user): "general instability"

**Status:** Refined into H2 — not wrong, but the concrete mechanism is a replication-sync race, which also explains why it only happens *sometimes* (timing window of near-simultaneous joins).

### Hypothesis 1: Client-side character registry double-add (in-project code)

**Status:** Refuted

**Theory:** CharacterManager cache/registry could append the same character twice.

**Resolution:** `RebuildCharactersCache` clears then projects the NetworkList 1:1 (`CharacterManager.cs:144-163`); `GetCharacters` copies. No append path outside the projection. Refuted by code inspection.

### Hypothesis 2: NGO NetworkList initial-sync + pending-delta double-delivery on the joining client (primary)

**Status:** Open

**Theory:** The Mage Occulte instance connected while the server was adding another joining player's character to `networkedCharacters` (same tick/synchronization window — F6). The connection synchronization payload already contained the entry AND the pending delta add was also delivered → the replica holds it twice. Known NGO bug class (fixed for 1.12.0, NetworkList visibility variant #2454); possible regression or uncovered edge in 2.12.0 (F5).

**Supporting indicators:** Client-local + persistent (matches replica divergence); intermittent (timing window); multi-instance solo = joins within the same seconds; replication layer bumped 6 versions 10 days before; duplicated player is another *joiner*, not the local player.

**Would confirm:** Bugged instance was a late joiner (not host) + Player.log / tagged log showing `OnListChanged` add for an id already present, or a repro joining a client during a burst of `AddNewCharacter` calls; an NGO issue report for 2.x matching this.

**Would refute:** Bugged instance was the **host** (host never receives a synchronization payload → this mechanism impossible); or logs showing the duplicate appearing long after join.

### Hypothesis 3: Server-side double character (reconnect / double-add)

**Status:** Refuted (provisionally)

**Theory:** Disconnect/reconnect left a stale character + a new one server-side.

**Resolution:** Would replicate to all clients — tester states the other instances showed 3 players. Kept only if that statement turns out false (single-frame evidence exists only for the bugged instance).

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| Host-or-joiner status of the bugged instance | Confirms/refutes H2 outright | Ask the tester (Wouh, Le Fantôme) |
| Player.log of the bugged instance | Timing of the duplicate add; errors | `%USERPROFILE%\AppData\LocalLow\<company>\CorruptionDuPortail\Player.log` — collect right after a repro (file is overwritten per run: previous run = `Player-prev.log`) |
| Replica contents at bug time | Confirms duplicate entry + same clientId | Tagged `[CHARLIST]` log in `OnNetworkedCharactersChanged`/`RebuildCharactersCache` logging count + ownerClientIds + NetworkObjectIds |
| NGO 2.12 known-issue confirmation | Upstream vs our-code confidence | Search NGO GitHub issues for NetworkList duplicate on 2.x |

## Source Code Trace

| Element | Detail |
| ------- | ------ |
| Error origin | Not in project code — duplicate enters at the `NetworkList` replication layer (`networkedCharacters`, `Assets/Scripts/Characters/CharacterManager.cs:171`) on the joining client |
| Trigger | Client connection synchronization overlapping a server-side `networkedCharacters.Add` (joins in the same window — `LobbyState.cs:15-22`) |
| Condition | Near-simultaneous multi-instance joins (solo playtest pattern); intermittent = timing window |
| Related files | `Board/BoardManager.cs:156-183` (cards), `GameLogic/GameStates/GameIntroductionState.cs:65` + `Board/UI/CharacterBar/CharactersBar.cs:129-240` (bar), `Characters/CharacterManager.cs:144-171,400-432`, `GameLogic/GameStates/LobbyState.cs:15-27,60-64`, `Packages/manifest.json` (NGO 2.12.0) |

## Conclusion

**Confidence:** Medium

**Confirmed:** the duplication is client-local, persistent from game start, and both UI surfaces + targeting are faithful projections of the client's `networkedCharacters` `NetworkList` replica — so the replica itself held the technomancer's entry twice (Deduction 1). In-project double-add paths are refuted (H1, H3).

**Hypothesized (primary, H2):** NGO initial-synchronization + pending-delta double-delivery on a client that joined while another player's character was being added — a documented NGO bug class, plausibly regressed or re-exposed by the 2.6.0 → 2.12.0 bump ten days before the incident. Needs the host/joiner answer + logs to Confirm.

The "souvent des problèmes d'instabilité … que parfois" impression matches this diagnosis: a narrow, timing-dependent replication window that multi-instance solo playtests (burst joins on one machine) hit far more often than real distributed play.

## Recommended Next Steps

### Fix direction

Two independent layers (defense in depth):

1. **Self-healing projection (cheap, ships regardless of upstream):** dedup in `RebuildCharactersCache` — skip an entry whose resolved `Character` (or `ownerClientId`) is already in `_charactersCache`, and log loudly when a duplicate is dropped (that log doubles as the permanent tripwire). One clientId can never legitimately appear twice (guard `CharacterManager.cs:402`), so dedup is safe by construction.
2. **Upstream:** check NGO issue tracker for a NetworkList duplication regression in 2.7–2.12; if found, pin/bump accordingly (paired with Multiplayer Tools per project rule).

### Diagnostic

- Ask the tester: was the Mage Occulte instance the **host** or did it join another instance's lobby? (host ⇒ H2 dead, reopen H3 and re-verify "others see 3").
- Add `[CHARLIST]` tagged logs (project convention: one filterable tag) in `OnNetworkedCharactersChanged` + `RebuildCharactersCache` on clients: count, ownerClientIds, NetworkObjectIds. Collect `Player.log` on next repro.
- Repro attempt: 3 build instances, join the last two as fast as possible (the burst-join window); repeat ~10 lobbies.

## Reproduction Plan

Setup: 3 standalone build instances on one PC (same as tester). Host on instance A. Trigger: join B then C back-to-back (< 1 s apart) so C's connection synchronization overlaps B's `AddNewCharacter` (or vice versa). Expected on bugged run: the later joiner's replica shows N+1 entries with one clientId doubled (visible immediately in the `[CHARLIST]` log — no need to reach the Awakening). Verification of fix 1: dedup log fires instead of a doubled card/bar.

## Side Findings

- `LobbyState.cs:64` subscribes `OnClientDisconnectCallback` in `OnStateCreated` and never unsubscribes it (unlike `OnClientConnectedCallback`, removed at `OnEndStateServer`, `LobbyState.cs:76`) — mid-game disconnects therefore call `Command.RemoveCharacter` while `GameManager.OnPlayerDisconnectedServer` (`GameManager.cs:554`) also reacts. Possibly intended, worth a design check. Confirmed (code).
- `CharactersBar.ResetCharactersBar` receives `GetCharacters()` **unfiltered** (`GameIntroductionState.cs:65`) while `BoardManager.ShowAllPlayerCards` filters `!isFake` (`BoardManager.cs:169`) — fake characters would appear in the bar but not on the table. Not this bug (both surfaces duplicated), but an inconsistency. Confirmed (code).
- `RebuildCharactersCache` resolves `NetworkBehaviourReference.TryGet` without passing a `NetworkManager` (`CharacterManager.cs:150`) → resolves against `NetworkManager.Singleton`; fine in builds, wrong graph under the multi-NM test fixture. Hypothesized impact only in tests. 
