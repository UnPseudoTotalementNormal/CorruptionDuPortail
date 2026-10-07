---
title: 'Fix join rejected by asymmetric ConnectionApproval (host true / client false)'
type: 'bugfix'
created: '2026-07-13'
status: 'done'
baseline_commit: 'af081af8c18e933a28b9f5d7ef0e009aeb7cea79'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/join-relay-bind-failure-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every joining client is disconnected at the NGO handshake with "Incomplete connection request message given config - possible NetworkConfig mismatch". Root cause (Confirmed): `NetworkConfig.ConnectionApproval` is asymmetric — the host sets it `true` via `ConnectionApprovalGate.Enable` (regression from commit `cb4c3b67`), but the client join path never enables it and the BootScene default is `ConnectionApproval: 0`. NGO ties the client's `ShouldSendConnectionData` to its own `ConnectionApproval`, so the client omits the ConnectionData the approval-enabled server tries to read → buffer underrun → `DisconnectClient`. Joins are 100% broken.

**Approach (Option A, Poyo-chosen):** Make `ConnectionApproval` symmetric by enabling it in the serialized BootScene NetworkManager so BOTH host and client inherit `true`. Keep `ConnectionApprovalGate.Enable` installing the host-side approval CALLBACK (the mid-game "Rejoindre une partie déjà en cours" reject stays intact). Add a regression guard so the flag can't silently revert.

## Boundaries & Constraints

**Always:** Host and client MUST agree on `ConnectionApproval`. The mid-game-reject behaviour of `ConnectionApprovalGate` (approve only in the pre-scene window OR lobby phase) must be preserved unchanged. The client needs only the FLAG (no approval callback on the client — the callback is server-only).

**Ask First:** Adding any client-side approval CALLBACK. Changing `ConnectionApprovalGate.ShouldApprove` logic. Touching any other NetworkConfig field (TickRate, EnableSceneManagement, ForceSamePrefabs, ConnectionData).

**Never:** Do NOT remove or weaken `ConnectionApprovalGate` / the mid-game reject. Do NOT touch the liveness layer, the connect-timeout change, or the duplicate-prefab entry (all exonerated, separate concerns). Do NOT set a non-empty `NetworkConfig.ConnectionData`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Client joins lobby | host approval=true, client approval=true (inherited) | Client's ConnectionRequest format matches; server approves (lobby phase); client syncs into lobby | N/A |
| Mid-game join attempt | GameManager present, not in lobby phase | Server REJECTS via `ConnectionApprovalGate.HandleApproval` with "La partie a déjà commencé." (unchanged) | Reason surfaced to client |
| Host self-connection | host at StartHost, GameScene not loaded yet | Approved (GameManager absent) — unchanged | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scenes/BootScene.unity:609` -- `ConnectionApproval: 0` on the NetworkManager `NetworkConfig` — the value to flip to `1`. The fix.
- `Assets/Scripts/Network/ConnectionApprovalGate.cs:43-44` -- host sets `ConnectionApproval=true` + installs `HandleApproval` callback; called host-only (`MainMenu.cs:471,495`). Keep as-is.
- `Assets/Scripts/UI/MainMenu.cs` -- `JoinWithFacepunch`/`JoinWithUnityRelay` (client `StartClient`, no approval enable). Reference only under Option A.
- NGO (read-only, verified): `NetworkConnectionManager.cs:734` (`ShouldSendConnectionData = ConnectionApproval`), `ConnectionRequestMessage.cs:79-136` (format divergence → `DisconnectClient`).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scenes/BootScene.unity` -- set the NetworkManager `NetworkConfig.ConnectionApproval` from `0` to `1` (line 609). Done via direct YAML edit (BootScene not open in editor); Unity reimported it (guard test passing proves the runtime value is now true). -- symmetric approval so host and client agree.
- [x] `Assets/Scripts/Tests/Editor/ConnectionApprovalConfigGuardTests.cs` -- NEW EditMode regression guard: opens `Assets/Scenes/BootScene.unity` (additive, `EditorSceneManager`), finds the `NetworkManager`, asserts `NetworkConfig.ConnectionApproval == true`; closes the scene in teardown. Tripwire so the value cannot silently revert to `0`. -- pins the invariant. GREEN.

**Acceptance Criteria:**
- Given the serialized BootScene NetworkManager, when the project loads, then `NetworkConfig.ConnectionApproval` is `true` (both host and client inherit it).
- Given a host in the lobby and a real remote client, when the client joins, then it is NOT rejected with "Incomplete connection request message given config" and reaches the lobby (Poyo manual 2-peer verification).
- Given a game already past the lobby phase, when a client attempts to join, then `ConnectionApprovalGate` still rejects it with "La partie a déjà commencé." (unchanged).
- Given the EditMode suite, when run, then the new guard test passes and nothing else regresses.

## Design Notes

Why the flag alone fixes it: NGO computes `ShouldSendConnectionData = NetworkConfig.ConnectionApproval` per side (`NetworkConnectionManager.cs:734`). With both sides `true`, the client writes `ConfigHash + ConnectionData` and the server reads the same shape — no underrun. Empty `ConnectionData` is fine; `HandleApproval` decides on lobby phase, not payload.

Robustness caveat (surfaced for the checkpoint): Option A is a serialized-value fix with no runtime code path, so its ONLY automated defense is the scene-load guard test. A more regression-proof alternative (Option B) would additionally set `ConnectionApproval=true` in the client join path (`JoinWithFacepunch`/`JoinWithUnityRelay`, flag only, no callback) so the fix survives a scene re-save that drops the value. Not implemented per Option-A choice; mention if you want the belt-and-suspenders.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after edit) -- expected: no new compile errors; scene edit persisted.
- `mcp__UnityMCP__run_tests` (EditMode, filter `ConnectionApprovalConfigGuard`) -- expected: guard green.
- `mcp__UnityMCP__run_tests` (EditMode full) -- expected: no regression.

**Manual checks (real MP, Poyo — per no-playtest-by-Claude):**
- 2 builds / MPPM: a client joins the lobby WITHOUT the "Incomplete connection request" reject; host console no longer logs it. Mid-game join still rejected with the French reason.

## Suggested Review Order

- The fix — one serialized value; host and client now both inherit approval=true.
  [`BootScene.unity:609`](../../Assets/Scenes/BootScene.unity#L609)

- Unchanged but load-bearing: the host still installs the approval callback + keeps the mid-game reject.
  [`ConnectionApprovalGate.cs:43`](../../Assets/Scripts/Network/ConnectionApprovalGate.cs#L43)

- Regression tripwire: asserts the serialized value stays true.
  [`ConnectionApprovalConfigGuardTests.cs:44`](../../Assets/Scripts/Tests/Editor/ConnectionApprovalConfigGuardTests.cs#L44)
