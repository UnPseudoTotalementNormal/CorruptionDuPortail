---
title: "Epic — Network Sync Hardening (zero-desync)"
status: draft
type: epic
owner: Poyo
created: 2026-10-04
branch: fix/network-sync-hardening
communication_language: Français
document_output_language: English
scope_decision: "All specs on one branch, delivered in order NET-00 → NET-11, test gate between specs"
---

# Epic — Network Sync Hardening (zero-desync)

## 1. Problem

Playtesters report that joining a game is unreliable, and that inside a game some players see a **different game
than the others**: missing pseudos, a blank white card, and earlier a duplicated technomancer card. In a social
deduction game **a single desync makes the match unplayable**: a player who sees the wrong name, role, vote or
knowledge plays a different game.

Owner directive (Poyo, 2026-10-04): *"solidifie tout l'aspect réseau du jeu, si y'a une seule desync dans n'importe
quel système c'est injouable"*.

### 1.1 Trigger evidence (playtest screenshot, 2026-10-04)

One client's board, 7 cards:
- **Card 3**: white image, no role name, **red faction logo**, no pseudo. `FactionType` default = `anomaly` (=0,
  red, `Domain/FactionType.cs:5`); `rolePortrait` default = 0, absent from `PortraitTable` → `Get` returns `null`
  (`Characters/PortraitTable.cs:24`) → white `Image`. **That client never received this character's role** (it holds
  the serialized default `Role`).
- **Cards 3, 4, 5, 7**: no pseudo, other players' pseudos fine. `Character.GetOwnerPseudo()` returns `""` silently
  when the `playerInfos` entry is missing (`Characters/Character.cs:174-178`). **That client's player roster lost
  entries.**

## 2. Design principles (the invariants every spec enforces)

1. **State, not events.** Anything a peer must *know* is replicated as **state** (NetworkVariable / full snapshot),
   never reconstructed from a stream of one-shot RPCs. RPCs carry *intents* (client → server) and *cosmetic
   moments* (animations, sounds) only. A missed/duplicated/reordered event must not be able to corrupt knowledge.
2. **Full snapshots, never index deltas, for any collection mutated around join/leave.** NGO `NetworkList<T>` has
   two holes: it double-delivers a same-tick `Add` to a synchronizing client (NGO issue #3280, no
   `WriteFieldSynchronization` override — confirmed at source in 2.12.0) and applies `Value`/`RemoveAt` deltas **by
   index** (`NetworkList.cs:291-334`). One duplicate + one later index write = silent corruption.
3. **The server decides, always.** No gameplay outcome is computed from a client's replica. Clients propose, the
   server validates against its own truth (uses left, awake, current state, target validity) and applies.
4. **Private knowledge lives in a server ledger** and is pushed to each viewer as an idempotent full slice
   (the `PlayerIconManager` pattern, already proven in this codebase). Clients replace, never accumulate.
5. **Ordering is explicit.** NGO sends RPCs immediately but NetworkVariable deltas at the end of the tick, so a
   client RPC handler reads stale NetworkVariables. Any handler that needs replicated state must either receive
   it as a parameter, react to `OnValueChanged`, or run behind the state-transition barrier (NET-06).
6. **Every desync is loud.** Tagged `[DESYNC]` / `[CHARLIST]` / `[ROSTER]` error logs ship in player builds so a
   `Player.log` proves or rules out a desync class on the next report.

## 3. Audit — findings

Severity: **C** = observed in playtest or certain under normal play · **H** = plausible under normal play ·
**M** = edge timing · **L** = latent / hygiene.

| # | System | Finding | Evidence | Sev | Spec |
|---|---|---|---|---|---|
| F1 | Player roster | `playerInfos` is a `NetworkList` mutated at join (`Add`), on ready toggle (index set) and on leave (`RemoveAt`). One burst-join duplicate on a client + any later index write overwrites/removes **another player's** entry on that client only → missing pseudos. Ready system multiplies index writes. | `Network/LobbyPlayerInfoHolder.cs:85,114,195,255` | C | NET-01 |
| F2 | Player roster | `SavePlayerInfoRpc` blindly `Add`s (no upsert), trusts client-supplied `playerClientId` (not `SenderClientId`), no retry if the answer never arrives. | `LobbyPlayerInfoHolder.cs:95-115` | H | NET-02 |
| F3 | Profile | Name cut at the first `#` for **Steam names too** (`SteamClient.Name`) → `"#Nohan"` becomes `""`. `PseudoInputField` edits the local copy only (`UpdateLocalPlayerInfo` has zero callers) → rename never replicates. | `Network/Player/LocalPlayerInfo.cs:20-34`, `UI/LoginMenu.cs:139-145`, `UI/Misc/PseudoInputField.cs` | M | NET-02 |
| F4 | Name display | Cards read the pseudo once (pull) and never refresh on roster change; empty string fallback. Nameplates are reactive (`AvatarNameplate.cs:125`), cards/InfoTable are not. | `Board/Card.cs:285,301,308,435,453`; `UI/InfoTable/*` | H | NET-03 |
| F5 | Characters list | `networkedCharacters` (`Add` on join, `RemoveAt` on lobby leave) — same NGO hole; the `[CHARLIST]` dedup masks duplicates but a later `RemoveAt` on a diverged replica removes the **wrong character** client-side. | `Characters/CharacterManager.cs:445,467-469`; deferred-work.md:191 | H | NET-04 |
| F6 | Avatars list | `_avatars` NetworkList, same join/leave pattern. | `Avatars/AvatarManager.cs:86,248,270-272` | H | NET-04 |
| F7 | Join/start | Auto-start keys off spawned Characters only. A client approved in lobby but still loading (sync allowed up to 90 s) has no Character yet → does **not** block the start → arrives in a started game as a ghost (no character, LobbyState connect handler already unsubscribed). | `GameLogic/GameStates/LobbyState.cs:97-122,170-176`; `Network/JoinHandshake.cs:35-36` | H | NET-05 |
| F8 | Join | No build/protocol version check at connection approval: mismatched builds join and silently fail to deserialize RPCs (e.g. `Type.GetType(AQN)` in `Role.NetworkSerialize`). | `Network/ConnectionApprovalGate.cs:51-75`; `Characters/Role.cs:96-104` | M | NET-05 |
| F9 | State machine | `OnStart/OnEndStateClient` arrive by RPC (immediate) but `currentGameStateIndex` and every NV written in the transition frame arrive at end of tick → handlers read stale NVs; client `Update` runs `StateUpdateClient` of the **old** state for ≥1 tick after its `OnEnd`. | `GameLogic/GameManager.cs:245-255,369-382` | M | NET-06 |
| F10 | Vote | No current-state guard on `OnPlayerVotedRpc` (late vote after tally); voter identity is client-supplied (`GetLocalClientId()` in the payload). | `GameStates/VoteState.cs:43-84`; deferred-work.md:23 | M | NET-06 |
| F11 | Chaining | Client animation loop enumerates the live `chainingPlayers` NetworkList across `await`s; the host's `Clear()` (on ITS animation end) truncates the enumeration on slower clients → skipped chain animations. | `GameStates/ChainingState.cs:55-80` | M | NET-06 |
| F12 | Roles | Role delivered by `GiveRoleToCharacterRpc(SendTo.Everyone)`; each peer awaits `GetCharacterAsync`, whose promise is only resolved by `RegisterSpawnedCharacter` (fired once at spawn). Character spawned but not yet in the list replica → **promise orphaned forever, role never applied** (card 3). Fakes: spawn + RPC in the same frame, list delta arrives after the RPC, and fakes never call `RegisterSpawnedCharacter` → certain orphan. Each peer also re-broadcasts `UpdateRoleRpc` + `CheckForPowersRpc` (N² fan-out). | `Characters/CharacterManager.cs:85-106,348-362`; `Characters/Character.cs:53-62,100-113`; `GameStates/RoleAttributionState.cs:82-90,139-158` | C | NET-07 |
| F13 | Powers list | Client `role.powers` is a hand-maintained list mutated by 3 event RPCs (`CheckForPowersRpc`, `RemovePowerFromCharacterPowerListRpc`, `OnReparentedClientRpc`) + `GetComponentsInChildren`; only converges if parent-sync and RPC arrive in the right order; never removes stale entries on `CheckForPowersRpc`. | `Character.cs:115-131`; `GameLogic/PowerManager.cs:109-170`; `Characters/Powers/Power.cs:485-526` | H | NET-08 |
| F14 | Power state | `PReincarnation.isPassive` flipped by an Everyone RPC on a plain field (event-state). | `Characters/Powers/PReincarnation.cs:113-117` | M | NET-08 |
| F15 | Power authority | Embrace of Shadows, Cursed Vision, Lack of Affection **decide on the client** (`RunClientDecisionEffects`) from the client's replica (roles!) then self-RPC the authoritative mutation (`CorruptPlayerServerRpc`, `RequireOwnership=false`). A desynced replica (F12) becomes a real, authoritative gameplay error. | `Characters/Powers/PEmbraceOfShadows.cs:49-70`; `PCursedVision.cs:30-46`; `PLackOfAffection.cs:29-63`; `Character.cs:181-186` | C | NET-09 |
| F16 | Power authority | Server effect RPCs (`OnCardClickedRpc`, …) and `OnUsedServerRpc` apply **without validation** (uses left, awake, chained, current state, target validity). Double-click / click-at-timer-end within RPC latency → effect applied twice or after sleep; `powerUseLeft` can go negative. | `Power.cs:338-373,393-398`; e.g. `PTruthChains.cs:44-48` | H | NET-09 |
| F17 | Knowledge | `GameInfoRevealer` knowledge exists **only** client-side, built by reveal RPCs; the server keeps no ledger for real players → nothing to reconcile against; `OnRolesAttributed` wipes the dict on an RPC whose order vs. early reveals is implicit. | `GameLogic/GameInfoRevealer.cs:20,52-64,188-241` | H | NET-10 |
| F18 | Chat | Every message is broadcast to every client and filtered by the client-local `discoveredChatIds`; whether a member keeps a message depends on whether its discovery RPC was processed first → two members can see different conversations; private channels leak to all clients; sender id client-supplied. | `ChatSystem/ChatManager.cs:105-112,126-130,177-201` | H | NET-11 |
| F19 | Observability | No desync detection outside `[CHARLIST]`; a report cannot be classified from `Player.log`. | — | H | NET-00 |

### 3.1 Audited and found SAFE (no spec)

| System | Why safe |
|---|---|
| `GameSettingsManager._settings` | Populated at spawn (before any join); later only index-`Value` sets — a same-tick sync+delta re-applies the same value at the same index (idempotent). No `Add`/`RemoveAt` around joins. |
| `MessageManager` lists, `chainingPlayers`, `PBoundByInk.alreadyTargetedClients`, `PCardsShuffling.discoveredClientIds` | Mutated mid-game only; mid-game joins are rejected (`ConnectionApprovalGate`) so the NGO join hole cannot fire. |
| `Character` flags (`isChained`, `isCorrupted`, `isAwakened`, …), `Power.powerUseLeft/ownerClientId/isCopiedPower` | NetworkVariables (full-value, late-join safe). |
| `PlayerIconManager` | Server ledger + full per-viewer slice push — the reference pattern for NET-10/NET-11. |
| Vote counts, awakening timer | Server pushes full snapshots (`OnRefreshPlayerVotesRpc`, `UpdateAwakeningTimerRpc`). |
| Timing (Awakening/Vote/Intro/Recap) | Host clock drives transitions; clients only display. |
| Session lifetime | One game per session (`ShutOffGame` → Shutdown → menu), so per-game subscription leaks cannot accumulate across games. |

### 3.2 Out of scope (recorded, not specced)

- Anti-cheat: roles of every player are already sent to every client (today via RPC, after NET-07 via NV). Friends
  game — accepted. Do not widen it further.
- Reconnection (owner ruling, epic-player-leave-stability §3).
- Fake-skip randomness is frame-rate dependent (`AwakeningState.cs:253-263`) — server-only, no desync; design-owned.
- Upstream NGO fix for #3280 — we route around it.

## 4. Specs and execution order

| Order | Spec | Fixes | Size | Network-critical |
|---|---|---|---|---|
| 0 | [spec-net-00-desync-observability](spec-net-00-desync-observability.md) | F19 | S | no |
| 1 | [spec-net-01-replicated-snapshot-and-roster](spec-net-01-replicated-snapshot-and-roster.md) | F1 | M | **yes** |
| 2 | [spec-net-02-profile-handshake](spec-net-02-profile-handshake.md) | F2, F3 | S | yes |
| 3 | [spec-net-03-reactive-pseudo-display](spec-net-03-reactive-pseudo-display.md) | F4 | S | no |
| 4 | [spec-net-04-characters-avatars-snapshot](spec-net-04-characters-avatars-snapshot.md) | F5, F6 | M | **yes** |
| 5 | [spec-net-05-join-start-gate](spec-net-05-join-start-gate.md) | F7, F8 | M | **yes** |
| 6 | [spec-net-06-state-transition-ordering](spec-net-06-state-transition-ordering.md) | F9, F10, F11 | M | **yes** |
| 7 | [spec-net-07-role-as-replicated-state](spec-net-07-role-as-replicated-state.md) | F12 | L | **yes** |
| 8 | [spec-net-08-powers-projection](spec-net-08-powers-projection.md) | F13, F14 | M | **yes** |
| 9 | [spec-net-09-server-authoritative-powers](spec-net-09-server-authoritative-powers.md) | F15, F16 | L | **yes** |
| 10 | [spec-net-10-knowledge-ledger](spec-net-10-knowledge-ledger.md) | F17 | L | **yes** |
| 11 | [spec-net-11-chat-server-routing](spec-net-11-chat-server-routing.md) | F18 | M | yes |

Dependencies: 01 → 02, 03, 04 · 06 → 07 (the barrier makes the role NV visible to state handlers) · 07 → 08 → 09 →
10 (server decisions feed the ledger) · 00 first so every later spec can be measured.

## 5. Delivery rules

- One commit per spec (conventional commit + body + `UX:` line when player-visible), English, no AI attribution.
  Commits only with Poyo's explicit go per change (memory: no commit without authorization).
- **Red-first**: every spec adds a failing test reproducing its desync class *before* the fix (2-NM loopback
  fixture `Tests/PlayMode/Desingleton/MultiClientGameFixture.cs`, UTP only — never Steam).
- Test gate between specs: EditMode + PlayMode full suites green before starting the next spec.
- Behaviour-preserving except where the spec's Intent says otherwise. Anything design-owned (wording, fallback
  names, UX) is flagged **Ask First** — never invented.
- Existing invariants stay: `GetSafeRpcTarget` on every targeted RPC, `IsLocalOrSimulated` for simulated bots
  (clientId ≥ 100), UniTask only, FMOD only.
- Verification of network behaviour is by `run_tests` (2-NM) + console; the 2-build playtest stays Poyo's job
  (memory: no playtest by Claude). Each network-critical spec ends with a **2-build playtest checklist**.

## 6. Verification environment

Decided (Poyo, 2026-10-04): Poyo opens a **second Unity Editor instance on the worktree**
(`.claude/worktrees/network-sync-hardening`); Claude drives that instance through the Unity CLI for compile checks and
test runs. The main checkout's Editor stays untouched.

## 6.1 Design decisions recorded (Poyo, 2026-10-04)

| Topic | Decision | Spec |
|---|---|---|
| Missing pseudo label | `"Joueur ?"` | NET-03 |
| Player who left mid-game | Name kept with a marker: `"<name> (parti)"` (marker text to confirm at review) | NET-03 |
| Version mismatch text | `"Version différente de l'hôte, mets ton jeu à jour."` | NET-05 |
| Loader stuck > 90 s | Host disconnects it so the lobby can start | NET-05 |
| Rejected power use | Silent UI reset, no message | NET-09 |
| Private chat history | No backlog: a new member sees only messages sent after it joined | NET-11 |

## 7. Definition of done (epic)

- All 12 specs `done`, EditMode + PlayMode green, every new red-first test green.
- A 2-build playtest with ≥ 4 players including a burst join (3 joins within 2 s), ready toggling, a lobby leave,
  a full game with copiers and a mid-game leave shows: identical pseudos/roles/cards on every client, zero
  `[DESYNC]` / `[CHARLIST]` / `[ROSTER]` lines in every `Player.log`.
