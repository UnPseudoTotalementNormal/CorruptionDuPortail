---
title: 'NET-02 — Profile handshake: profile in the connection payload, keyed upsert, Steam name fix'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-01-replicated-snapshot-and-roster.md']
fixes: ['F2', 'F3']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A client's profile reaches the server through a pull round-trip started on `OnClientConnected`:
server `AskForPlayerInfoRpc` → client `SavePlayerInfoRpc` → blind `Add` (`LobbyPlayerInfoHolder.cs:90-115`). The entry is
keyed on a **client-supplied** `playerClientId`, there is no upsert (a second answer duplicates), and nothing retries if
the answer never lands — the player then simply has no name for the whole session. Separately, the profile name is cut
at the first `#` for **every** source (`LocalPlayerInfo.cs:20-34`), but the `#`-discriminator format exists only for UGS
names; a Steam name (`SteamClient.Name`, `LoginMenu.cs:139-141`) containing `#` is truncated, possibly to `""`. A rename
via `PseudoInputField` never replicates (`UpdateLocalPlayerInfo` has no caller).

**Approach:** Send the profile **inside the NGO connection request** (`NetworkConfig.ConnectionData`, a versioned
`ConnectionPayload`), parsed by the approval callback and stored server-side per `ClientNetworkId`; the roster entry is
upserted by the server itself when that client finishes synchronizing. No round-trip, no client-supplied id, atomic with
approval. Keep a sender-keyed `UpdatePlayerInfoServerRpc` for runtime renames and wire `PseudoInputField` to it. Strip
the `#` discriminator only for UGS names; sanitize and byte-safely truncate every name.

## Boundaries & Constraints

**Always:**
- `ConnectionPayload` is a small versioned binary struct (payload version byte + playerName + playerFullName +
  steamId, and a `buildVersion` field consumed by NET-05). Set on **every** client start path:
  `MainMenu.cs:243`, `LobbySelectionPanel.cs:446`, `RelayConnector.cs:96` (one shared helper, set before `StartClient`).
- Server key = approval's `ClientNetworkId` / RPC `SenderClientId`. Never trust an id inside a client payload.
- Host profile: inserted server-side from `LocalPlayerInfoHolder` at holder spawn (no RPC to itself).
- Bots (`AddDebugPlayer`, ≥100): unchanged.
- Name sanitation in one pure Domain helper: trim, collapse whitespace, empty → existing default (`"Player" + n`, the
  current `LocalPlayerInfoHolder.BuildDefault` behaviour), truncate to `FixedString64Bytes` capacity **on a UTF-8
  code-point boundary** (never throw — `FixedString` throws in Editor when over capacity).
- Malformed / missing payload (old client) → still approved by this spec (NET-05 decides rejection), name = default,
  `[ROSTER] missing payload from X` warning.

**Ask First:**
- Any rename UI or wording change.
- Rejecting a connection for a missing payload (that is NET-05's call, with Poyo).

**Never:**
- Keep the `AskForPlayerInfoRpc` pull as the primary path (it may remain only if a test needs it; preferred: delete).
- Strip `#` from Steam names.

## I/O & Edge-Case Matrix

| Scenario | Input | Expected | Error handling |
|---|---|---|---|
| Normal join | payload name "Alice#1234" (UGS) | roster "Alice", fullName "Alice#1234" | N/A |
| Steam name with # | "#Nohan" (Steam) | roster "#Nohan" | N/A |
| Empty/whitespace name | "   " | roster default "Player####" | N/A |
| 70-byte emoji name | > 61 UTF-8 bytes | truncated on code-point boundary, no exception | N/A |
| Missing payload | old build | default name + `[ROSTER] missing payload` | warn |
| Client spoofs id in rename | `UpdatePlayerInfoServerRpc` with other id | sender's own row updated only | N/A |
| Rename in lobby | PseudoInputField submit while connected | every replica shows new name | N/A |
| Sync never completes | client approved then drops | pending profile discarded on disconnect | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Network/ConnectionApprovalGate.cs:56-76` -- parse `_request.Payload` → `ConnectionPayload`; store pending profile by `ClientNetworkId`
- NEW `Assets/Scripts/Network/ConnectionPayload.cs` (+ pure parser/writer, EditMode-tested)
- NEW `Assets/Scripts/Domain/PlayerNameSanitizer.cs` -- trim/default/UTF-8-safe truncate/discriminator strip (UGS only)
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:44-56,90-115,142-166` -- server upserts from pending profile on client-connected; delete Ask/Save pull; host self-insert; keep sender-keyed update RPC
- `Assets/Scripts/Network/Player/LocalPlayerInfo.cs:20-34` -- `CreateNewClientData(name, isUgsName)`; sanitizer
- `Assets/Scripts/UI/LoginMenu.cs:136-146` -- pass `isUgsName: !_useSteamAuth`
- `Assets/Scripts/UI/Misc/PseudoInputField.cs:16-27` -- sanitize + `UpdateLocalPlayerInfo()` when connected
- Client start paths: `Assets/Scripts/UI/MainMenu.cs:243`, `UI/LobbyUI/LobbySelectionPanel.cs:446`, `Network/RelayConnector.cs:96`
- Tests: `Tests/PlayMode/LobbyPlayerInfoHolderTests.cs`, `Tests/Editor/LocalPlayerInfoTests.cs`

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/PlayerNameSanitizerTests.cs` + `ConnectionPayloadTests.cs` -- NEW (matrix rows 1-5, round-trip, malformed bytes)
- [ ] `Tests/PlayMode/Replication/ProfileHandshakeTests.cs` -- NEW 2-NM red-first: client with name X connects → server roster has X under the client's real id with no RPC round-trip; a forged-id rename updates only the sender
- [ ] Implement payload helper on all 3 start paths, approval parsing, server upsert on connect, host self-insert, delete pull RPCs
- [ ] Sanitizer + Steam fix + PseudoInputField wiring

**Acceptance Criteria:**
- Given a client whose name is "#Nohan" on Steam, when it joins, then every replica shows "#Nohan".
- Given a client that joins, when it finishes synchronizing, then its roster entry exists on the server in the same frame as `OnClientConnected`, keyed by its real clientId, without any RPC from it.
- Given a client that sends a rename with another player's id, then only the sender's entry changes.

## Verification

- EditMode: `PlayerNameSanitizerTests`, `ConnectionPayloadTests`
- PlayMode: `ProfileHandshakeTests` red → green, full suites green
- 2-build playtest: a Steam account whose name contains `#` + a UGS account; both names correct everywhere
