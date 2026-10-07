---
title: 'Lobby "Prêt" ready-to-start system — per-player ready toggle replaces the Démarrer button, auto-start on all-ready, dev force-start'
type: 'feature'
created: '2026-07-16'
status: 'done'
baseline_commit: '49db633f'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-role-composition-rules.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-role-attribution-uitk.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The game starts from a single host-pressed **"Démarrer"** button (HUD `LobbyStartButton` on `LobbyStateUI.prefab`, and the mirror button in the tablet lobby footer). Poyo wants the online-game convention: **no start button** — every player has a **"Prêt" / "Pas prêt"** toggle, and the game starts only once **everyone agrees**.

**Approach:** Add a replicated per-player **ready** flag to the lobby census (`PlayerInfo.isReady` in `LobbyPlayerInfoHolder.playerInfos`). Each player toggles their OWN ready via a dedicated server RPC (trust the sender's id). The server (`LobbyState`) reacts to census/settings changes and **auto-starts** the moment **all census players are ready AND the composition gate passes** (the same `RoleAttributionState.ValidateComposition` from the composition-rules work). Simulated bots (`clientId >= 100`) are **auto-ready**. Both old "Démarrer" buttons are removed; the tablet footer shows a ready tally + the local player's toggle. A **dev-only "Démarrage forcé"** button in the "Autres options…" tab skips ONLY the all-ready condition (still enforces the composition gate).

## Locked decisions (Poyo, 2026-07-16)
1. **Auto-start immediately** when all connected players are ready AND the composition is valid — nobody has a start button.
2. **Simulated bots are auto-ready** (`clientId >= 100` — set `isReady = true` in `AddDebugPlayer`), else solo bot-debug can never reach all-ready.
3. **Dev "Démarrage forcé"** button in the empty "Autres options…" tab: **skips the all-ready check ONLY**; the composition gate still applies server-side. Host-only.
4. The ready state lives on the **census** (`LobbyPlayerInfoHolder.playerInfos`, bot-aware) — the roster the tablet already shows.

## Boundaries & Constraints

**Always:**
- **Server authority.** Ready is mutated **server-side only**. A client toggles via a dedicated `SetReadyServerRpc` (`SendTo.Server`) that trusts `rpcParams.Receive.SenderClientId` — a client can only ready ITSELF (no impersonation), exactly like `UpdatePlayerInfoServerRpc`. NEVER a whole-`PlayerInfo` replace for ready (that would clobber the profile) — mutate only the `isReady` field of the sender's entry.
- **Auto-start decision lives in `LobbyState`** (server), reacting to `playerInfos.OnListChanged` + `GameSettingsManager.OnSettingsChanged`. It calls a single guarded path that (a) requires ≥1 census player, (b) requires **every** census entry `isReady`, (c) requires the existing composition gate (`ValidateComposition`) to pass, then advances via the existing `Loop.NextGameState()`. Reuse the composition gate — do NOT reimplement it.
- **Bot-safe.** Any per-client RPC target uses `GetSafeRpcTarget`; use `IsLocalOrSimulated` where the host acts for a simulated identity. The ready toggle is `SendTo.Server` (no per-target wrap needed, like `SavePlayerInfoRpc`).
- **The toggle is available to EVERY player** (not host-only, unlike the role steppers). It targets the local client's own entry. The role grid stays host-only read/write.
- **`PlayerInfo` field-add follows the documented pattern** (`PlayerInfo.cs` note): add `isReady` + one `SerializeValue` line + one `Equals` term + one `HashCode.Combine` arg. Unmanaged (`bool`) keeps the `NetworkList` constraint.
- **Reuse the composition footer + validator.** The footer keeps the composition reason (from the rules work) and adds the ready tally + toggle. The "Démarrer" `Button` is removed.
- **Before mutating `LobbyStateUI.prefab`** (removing the HUD start button), copy it into repo-root `BackupToolkit/` (gitignored).
- **UITK conventions** (`RoleCardController`/controller precedent): guarded init, `Q<>` by name, BEM, `var(--cdp-*)` tokens, root `pickingMode=Ignore`.

**Ask First:**
- **Unready-on-settings-change:** should editing max/forced (or a preset) reset everyone's ready to avoid a surprise auto-start when the host tweaks a valid config while players are ready? Default this pass = **do NOT auto-unready** (simplest); confirm.
- Retiring vs. disabling the HUD `LobbyStartButton` GameObject (default = disable the button GameObject in the prefab, keep the component code).
- Whether the census/`CharacterManager` roster divergence (bots in census, not in `CharacterManager`; the composition gate counts `CharacterQuery`) needs reconciling here — default = out of scope (pre-existing, deferred), auto-start layers all-ready ON TOP of the unchanged gate.

**Never:**
- No owner-write NetworkVariable; ready is server-written. No `AudioSource` (FMOD), no `System.Threading.Tasks.Task` (UniTask).
- Do not gate the ready TOGGLE on composition validity (a player may ready up anytime; the game just waits for a valid compo).
- Do not change the composition rules / distributor (that is the merged `role-composition-rules` work — this only consumes its gate).
- Do not build a countdown / cancel window (auto-start is immediate — decision 1).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Toggle ready | Player taps "Prêt" | `SetReadyServerRpc` sets that client's `isReady=true`; button becomes "Pas prêt"; tally updates; replicates | Non-existent sender entry → no-op |
| Untoggle | Player taps "Pas prêt" | `isReady=false`; auto-start can no longer fire | N/A |
| All ready + valid | Last player readies, composition valid | Server auto-starts (`NextGameState`) | N/A |
| All ready + invalid compo | Everyone ready but composition gate fails | Does NOT start; footer shows the composition reason | Waits until compo fixed |
| Host fixes compo while all ready | Compo becomes valid with everyone already ready | Auto-start fires on the settings-changed reaction | N/A |
| Bot present | Simulated bot (`clientId>=100`) in census | Bot is `isReady=true` from creation (`AddDebugPlayer`); counts toward all-ready | N/A |
| Solo host + bots | 1 human + N auto-ready bots | Human readies → all-ready true → auto-start if compo valid | N/A |
| Player leaves while others ready | A not-ready player disconnects | Their census entry is removed; remaining may now be all-ready → re-evaluate → maybe start | Uses existing disconnect removal |
| Dev force-start | Host taps "Démarrage forcé" (Autres options) | Server runs the composition gate ONLY (skips all-ready); starts if valid | Non-host inert; invalid compo → refused with reason |
| Non-host toggle | Non-host player taps Prêt | Works (readies self); role grid stays read-only | N/A |
| Ready tally | Any ready change | Footer shows "X / N prêts" (N = census count) | N/A |
| 0 players | Empty census | No auto-start (guard ≥1 player; compo gate also fails) | N/A |

</frozen-after-approval>

## Code Map

**Reuse / read:**
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — `OnStartGameButtonPressed` (the composition gate, reuse as the compo check) + `OnStartStateServer`/`OnEndStateServer` (subscribe/unsubscribe the auto-start reaction).
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` — `ValidateComposition(playerCount)` (merged rules work) — the compo validity source.
- `Assets/Scripts/Network/Player/PlayerInfo.cs` — DTO field-add pattern.
- `Assets/Scripts/UI/LobbyRoles/{LobbyRolesUitkController,GameLobbyRolesDataSource,DemoLobbyRolesDataSource,ILobbyRolesDataSource}.cs` — footer + seam (add ready read/write/local + force-start).

**Mutate:**
- `Assets/Scripts/Network/Player/PlayerInfo.cs` — add `bool isReady` (+ serialize/Equals/hash).
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs` — `RequestSetReady(bool)` + `[Rpc(SendTo.Server)] SetReadyServerRpc` (trust sender id, mutate only `isReady` of the sender's entry, replicate via index-set, no-op skip); `AddDebugPlayer` sets `isReady=true`; a `AllReady()`/`ReadyCount()` helper over the census.
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — subscribe to census + settings changes in `OnStartStateServer`, unsubscribe in `OnEndStateServer`; `TryAutoStart()` (≥1 player + all census ready + compo gate); `ForceStart()` (compo gate only, host-only).
- `Assets/Scripts/UI/LobbyRoles/ILobbyRolesDataSource.cs` — add `bool GetLocalReady()`, `void RequestSetReady(bool)`, `int GetReadyCount()`, `int GetReadyTotal()`, `void RequestForceStart()`; drop/repurpose `RequestStart` (now force-start).
- `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs` — implement against `LobbyPlayerInfoHolder` (local id via `NetworkManager.LocalClientId`) + `LobbyState.ForceStart`.
- `Assets/Scripts/UI/LobbyRoles/DemoLobbyRolesDataSource.cs` — stub ready (local bool) + force-start no-op.
- `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` — `BuildFooter`: replace the "Démarrer" `Button` with the ready tally + a Prêt/Pas-prêt toggle; `BuildContent`: put the dev "Démarrage forcé" button in the `TabOptions` branch (host-only).
- `Assets/Prefabs/StateUI/LobbyStateUI.prefab` — disable the HUD `LobbyStartButton` GameObject (backup first).

**New tests:**
- `Assets/Scripts/Tests/PlayMode/.../LobbyReadyAutoStartTests.cs` — auto-start fires only on all-ready + valid compo; force-start bypasses ready but not compo; a not-ready player blocks start; bot auto-ready.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Network/Player/PlayerInfo.cs` -- add `bool isReady` (+ serialize/Equals/hash per the documented pattern) -- replicated per-player ready.
- [x] `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs` -- `RequestSetReady` + `SetReadyServerRpc` (sender-id-trusted, field-only mutation), `AddDebugPlayer` auto-ready, `AllReady()`/`ReadyCount()` -- server-authoritative ready + bot auto-ready.
- [x] `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` -- census/settings subscription + `TryAutoStart` (≥1 player, all ready, compo gate) + `ForceStart` (compo-only, host-only) -- the auto-start + force-start decisions.
- [x] `Assets/Scripts/UI/LobbyRoles/{ILobbyRolesDataSource,GameLobbyRolesDataSource,DemoLobbyRolesDataSource}.cs` -- ready read/write/local + counts + force-start seam -- UI ↔ backbone.
- [x] `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` -- footer ready tally + Prêt toggle (replaces Démarrer); force-start button in Autres options (host-only) -- the new player-facing flow.
- [x] `Assets/Prefabs/StateUI/LobbyStateUI.prefab` -- disable the HUD `LobbyStartButton` GameObject (BackupToolkit copy first) -- remove the old start button.
- [x] `Assets/Scripts/Tests/PlayMode/.../LobbyReadyAutoStartTests.cs` -- cover the matrix (auto-start gating, force-start, not-ready block, bot auto-ready) -- pin the server logic.

**Acceptance Criteria:**
- Given 3 connected players and a valid composition, when the last player taps Prêt, then the server advances the game state exactly once.
- Given all players ready but an invalid composition, when evaluated, then the game does NOT start and the footer shows the composition reason.
- Given the host taps "Démarrage forcé" with a valid composition while a player is NOT ready, then the game starts (ready skipped, compo enforced).
- Given the host taps "Démarrage forcé" with an INVALID composition, then the game does not start (compo still enforced).
- Given a simulated bot in the census, when the human host readies, then all-ready is satisfied without toggling the bot.
- Given any player un-readies after all were ready, then a subsequent evaluation does not start the game.

## Design Notes

Ready toggle uses a DEDICATED server RPC that flips only `isReady` on the sender's census entry (not the whole-`PlayerInfo` replace in `UpdatePlayerInfoServerRpc`, which would clobber name/steamId; `UpdatePlayerInfo` also carries the current `isReady` forward so a future profile edit can't un-ready). `TryAutoStart` and `ForceStart` share the compo check (`TryResolveValidComposition`); force-start skips the all-ready predicate.

**Auto-start = a per-tick POLL in `StateUpdateServer`, latched by `_started` to fire once** (changed from the originally-planned `OnListChanged`/`OnSettingsChanged` subscription after the 3-agent review: the callback approach had a holder-spawn-order race that silently dead-wired auto-start, and re-entered `NextGameState` from a `NetworkList` callback → double-advance). The poll lazily resolves the census, is immune to spawn order, and can't re-enter. It checks the composition SILENTLY (no per-frame warning spam).

**All-ready is evaluated over the game PARTICIPANTS, not the raw census** — `AllParticipantsReady` requires every spawned `Character`'s owner to have a ready census entry (via `GetPlayerInfo`, first-match — the same row `SetReadyServer` flips). This ties readiness to the players who actually receive roles: a just-connected player whose census entry is still in flight blocks the start (no silent drag-in), a duplicated NGO census row can't wedge it, and bots (no character) neither block nor are required. The census/`CharacterManager` count divergence stays pre-existing and out of scope; the gate simply keys off characters.

## Verification

**Commands:**
- `mcp__UnityMCP__run_tests` (PlayMode, filter `LobbyReadyAutoStartTests`) -- expected: green.
- `mcp__UnityMCP__run_tests` (EditMode, full) -- expected: green (no regression to the composition-rules suite).
- `mcp__UnityMCP__read_console` after each script edit -- expected: no compile errors.

**Manual checks:**
- Host + 1 client: both ready → game auto-starts; un-ready one → it waits; force-start from Autres options bypasses a not-ready client.

## Suggested Review Order

**Auto-start decision (the load-bearing server logic)**

- Entry point: the per-tick poll that starts the game once participants are ready + composition valid (latched once; silent compo check).
  [`LobbyState.cs:71`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L71)
- The readiness predicate keyed off the actual participants (characters), robust to connect-race / duplicate census / bots.
  [`LobbyState.cs:101`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L101)
- The poll is driven from the server tick (not an event callback — the review-driven change).
  [`LobbyState.cs:192`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L192)
- Dev force-start: skips all-ready, keeps the composition gate, host-only.
  [`LobbyState.cs:128`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L128)

**Replicated ready state**

- The dedicated sender-trusted RPC that flips only `isReady` on the sender's census entry.
  [`LobbyPlayerInfoHolder.cs:224`](../../Assets/Scripts/Network/LobbyPlayerInfoHolder.cs#L224)
- Bots auto-ready.
  [`LobbyPlayerInfoHolder.cs:213`](../../Assets/Scripts/Network/LobbyPlayerInfoHolder.cs#L213)
- The DTO field-add (serialize/Equals/hash).
  [`PlayerInfo.cs:36`](../../Assets/Scripts/Network/Player/PlayerInfo.cs#L36)

**Player-facing UI**

- Footer: ready tally + Prêt/Pas-prêt toggle (replaces Démarrer).
  [`LobbyRolesUitkController.cs:568`](../../Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs#L568)
- Dev force-start button in the Autres options tab (host-only).
  [`LobbyRolesUitkController.cs:383`](../../Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs#L383)
- The seam impl (local ready + force-start), host-gated.
  [`GameLobbyRolesDataSource.cs:125`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L125)
- The retired HUD Démarrer button (GameObject deactivated).
  [`LobbyStateUI.prefab`](../../Assets/Prefabs/StateUI/LobbyStateUI.prefab)

**Tests (supporting)**

- Auto-start gating (ready+valid → start, not-ready blocks, all-ready+invalid blocks), force-start, bot auto-ready.
  [`LobbyReadyAutoStartTests.cs:1`](../../Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/LobbyReadyAutoStartTests.cs#L1)
