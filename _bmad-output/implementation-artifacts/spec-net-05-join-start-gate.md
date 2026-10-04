---
title: 'NET-05 — Join/start gate: no ghost joiners, no mismatched builds'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-02-profile-handshake.md']
fixes: ['F7', 'F8']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-join-started-game-gate.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-fix-join-load-connect-timeout.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-ready-system.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem 1 — ghost joiner.** Approval happens when the connection request arrives (lobby phase → approved), but the
joiner only becomes a participant (Character spawned by `LobbyState.OnClientConnected`) at NGO `SynchronizeComplete`,
which may take up to 90 s on a slow PC (`JoinHandshake.SyncTotalTimeoutSeconds`). The auto-start
(`LobbyState.AllParticipantsReady`, `:97-122`) only iterates **spawned Characters**, so a still-loading joiner does not
block it. If everyone else is ready, the game starts; the loader then completes sync into a started game where
`LobbyState`'s connect handler is already unsubscribed (`:170-176`) → no Character, no role, a ghost that receives every
RPC. Reported as "des gens ont du mal à rejoindre".

**Problem 2 — mismatched builds.** Approval checks only the phase (`ConnectionApprovalGate.cs:51-75`). Two different builds
can play together; NGO's config hash only covers the prefab list, not code. Any wire change (e.g. a renamed
`WinningCondition` type serialized by assembly-qualified name in `Role.NetworkSerialize`, `Role.cs:96-104`) then silently
fails to deserialize on one side.

**Approach:** (1) Server tracks a **synchronizing set** (approved → added; `OnClientConnected` or disconnect → removed);
auto-start and force-start require it empty. (2) A client completing sync after the lobby phase is disconnected with the
existing `GameInProgressReason`, so it lands back in the menu with a clear message. (3) The NET-02 `ConnectionPayload`
carries `Application.version`; approval rejects a build mismatch with an explicit reason.

## Boundaries & Constraints

**Always:**
- Synchronizing set is server-only state inside a small POCO (`JoinPhaseTracker`) unit-tested in EditMode.
- Ready tally UI may show "en attente d'un joueur qui charge" — **wording Ask First**; until approved, no UI change, the
  gate simply waits.
- Version rule: reject only when **both** peers are player builds and `Application.version` differs. An Editor peer
  (version irrelevant in dev / MPPM) logs `[JOIN-GATE] version mismatch (editor, allowed)` and is approved.
- Rejection reasons surface through the existing `JoinFailureMessage` path (`Domain/JoinFailureMessage.cs`).
- Bots (≥100) never touch the tracker.

**Decided (Poyo, 2026-10-04):**
- Version-mismatch rejection text, verbatim: `"Version différente de l'hôte, mets ton jeu à jour."`
- A loader still synchronizing after 90 s (`JoinHandshake.SyncTotalTimeoutSeconds`) is disconnected by the host so the
  lobby is never blocked indefinitely; it returns to the menu with a message.

**Ask First:**
- Wording of the "kicked: loading too long" message (none exists today).
- Whether CI should keep game-ci's default semantic versioning (needed for distinct `Application.version` per build;
  `ProjectSettings` `bundleVersion: 1.0.2` is static for local builds).

**Never:**
- Approve a mid-game connection (existing gate stays).
- Spawn a Character for a post-start arrival.

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| All ready, one joiner still loading | No start until the joiner finishes sync (or leaves / times out) |
| Joiner finishes sync after start (race) | Server disconnects it with "La partie a déjà commencé."; menu shows it |
| Joiner disconnects while loading | Removed from synchronizing set; start can proceed |
| Joiner still loading after 90 s | Host disconnects it; removed from set; start can proceed |
| Builds differ (both players) | Rejected with version reason |
| Editor ↔ build | Approved + warning log |
| Missing payload (pre-NET-02 build) | Treated as version mismatch (rejected) when the host is a player build |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Network/ConnectionApprovalGate.cs:51-76` -- version comparison from `ConnectionPayload`; add approved id to tracker
- NEW `Assets/Scripts/Domain/JoinPhaseTracker.cs` (pure) -- Approved/SyncCompleted/Disconnected → `HasSynchronizingClients`, `ExpiredLoaders(now, 90 s)`
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:66-140` -- `TryAutoStart` / `ForceStart` require `!HasSynchronizingClients`
- `Assets/Scripts/GameLogic/GameManager.cs` (server `OnClientConnectedCallback`) -- post-lobby sync completion → `DisconnectClient(id, GameInProgressReason)`
- `Assets/Scripts/Domain/JoinFailureMessage.cs` -- version-mismatch reason mapping
- Tests: `Tests/PlayMode/GameLogic/GameStates/LobbyReadyAutoStartTests.cs`, `LobbyStateStartGuardTests.cs`; join gate tests from spec-join-started-game-gate

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/JoinPhaseTrackerTests.cs` + version-rule tests -- NEW
- [ ] `Tests/PlayMode/Join/GhostJoinerTests.cs` -- NEW red-first 2-NM: host ready, client approved but sync artificially delayed (hold `SynchronizeComplete`) → today the game auto-starts (red); after fix it waits; release sync after a forced start → client disconnected with reason
- [ ] Implement tracker, gate, post-start disconnect, version check

**Acceptance Criteria:**
- Given a joiner still loading, when every connected participant is ready, then the game does not start until the joiner completes sync or leaves.
- Given a joiner whose sync completes after the start, then it is disconnected with "La partie a déjà commencé." and returns to the menu.
- Given two player builds with different `Application.version`, then the join is rejected with "Version différente de l'hôte, mets ton jeu à jour."
- Given a joiner that has not finished synchronizing 90 s after approval, then the host disconnects it and the lobby can start.

## Verification

- EditMode + PlayMode new suites; full suites green
- 2-build playtest: throttle one client (heavy background load) while others ready → start waits; two different CI builds → clear rejection
