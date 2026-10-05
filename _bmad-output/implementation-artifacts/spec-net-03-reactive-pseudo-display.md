---
title: 'NET-03 — Names are reactive and survive a leave'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-01-replicated-snapshot-and-roster.md']
fixes: ['F4']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every surface that shows a player name **pulls it once** at render time: cards (`Card.cs:285,301,308,435,453`),
InfoTable (`InfoTableSystem.cs:118-122`, `GameInfoTableDataSource.cs:48`), chat sender (`ChatPanel.cs:171-173`), portal
title (`TakeDownThePortalTextTitle.cs:32`). If the roster entry arrives or is corrected later, the surface stays wrong
for the rest of the game. `GetOwnerPseudo()` returns `""` silently when the entry is missing. And on a **mid-game leave**
the server removes the leaver's roster entry (`LobbyPlayerInfoHolder.cs:74-88`) while the leaver's character stays on
the board (chained, per leave policy) → after any leave, **every** client loses that player's name.

**Approach:** (1) In-game, a leaving player's roster entry is **kept and flagged** (`hasLeft`), removed only while in the
lobby phase. (2) Name surfaces subscribe to `onRosterChanged` (NET-01) and re-apply the name without replaying any
animation. (3) `GetOwnerPseudo()` never returns empty: missing entry ⇒ `"Joueur ?"` + `[ROSTER] missing` tripwire; a left
player reads `"<name> (parti)"`.

## Boundaries & Constraints

**Always:**
- Card refresh re-applies only the pseudo text in the card's **current** display mode (a card currently showing no
  pseudo stays without one; no flip, no tween, no sound).
- `hasLeft` added to `PlayerInfo` wire struct (field + SerializeValue + Equals + HashCode); lobby counts/ready tally
  ignore `hasLeft` rows (none exist in lobby since lobby leave still removes).
- Unsubscribe on destroy/despawn for every new subscriber.

**Decided (Poyo, 2026-10-04):**
- Fallback label when the entry is missing: `"Joueur ?"`.
- A player who left mid-game keeps their name **with a marker**: `"<name> (parti)"` on every name surface (marker text
  proposed by Claude — confirm at review).

**Ask First:**
- Any other visual treatment of a left player (colour, icon) — design-owned.

**Never:**
- Re-trigger `ShowPseudoWithRevealedInfo(true)` (card flip) from a roster change.
- Remove a roster row mid-game.

## I/O & Edge-Case Matrix

| Scenario | State | Expected |
|---|---|---|
| Late roster entry | Card rendered before the entry | Name appears when entry lands, no flip |
| Mid-game leave | B disconnects during awakening | B's card/InfoTable/chat show "B (parti)" on every client |
| Lobby leave | B leaves before start | Row removed (unchanged behaviour) |
| Missing entry | Never received | `"Joueur ?"` + one `[ROSTER] missing clientId=X` |
| Card in no-pseudo mode | `ShowRoleOnly` card | Stays without pseudo after roster change |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:74-88` -- leave: remove in lobby phase, flag `hasLeft` in game (phase via `GameManager.IsInLobbyPhase`, scene-wired)
- `Assets/Scripts/Network/Player/PlayerInfo.cs:26-59` -- `hasLeft` field
- `Assets/Scripts/Characters/Character.cs:174-178` -- fallback + tripwire
- `Assets/Scripts/Board/Card.cs:107-124,250-314,378-394` -- subscribe `onRosterChanged`; track `_pseudoVisible` set by the Show* methods; `RefreshPseudo()`
- `Assets/Scripts/Board/CardComponents/CardPlayerVisualUpdater.cs:32-36` -- unchanged API
- `Assets/Scripts/UI/InfoTable/InfoTableSystem.cs:110-125`, `UI/InfoTable/GameInfoTableDataSource.cs:40-65` -- rebuild names on roster change
- `Assets/Scripts/ChatSystem/ChatPanel.cs:165-180` -- resolve sender name at render; re-render visible window on roster change
- `Assets/Scripts/UI/Misc/TakeDownThePortalTextTitle.cs:22-33` -- refresh on roster change
- `Assets/Scripts/Extensions/UlongExtensions.cs:17-27` -- same fallback as `GetOwnerPseudo`

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Replication/RosterLeaveKeepsNameTests.cs` -- NEW red-first 2-NM: game started, client B leaves → host + remaining client still resolve B's name (fails today: row removed)
- [ ] `Tests/PlayMode/Board/CardPseudoRefreshTests.cs` -- NEW: card rendered with missing entry shows fallback; entry upserted → text updates, no flip invoked
- [ ] Implement `hasLeft`, card/InfoTable/chat/portal-title subscriptions, fallback

**Acceptance Criteria:**
- Given a started game, when a player leaves, then their name followed by "(parti)" stays on every client's cards, InfoTable and chat history.
- Given a card rendered before its owner's roster entry, when the entry arrives, then the card shows the name without flipping.
- Given a missing entry, then "Joueur ?" shows and one `[ROSTER] missing` error is logged.

## Verification

- PlayMode: the 2 new suites red → green; full suites green
- 2-build playtest: a player quits mid-game → their name remains on every remaining client
