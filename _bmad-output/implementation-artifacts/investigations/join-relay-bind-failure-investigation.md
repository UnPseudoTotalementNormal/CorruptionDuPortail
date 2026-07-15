# Investigation: "Can't join a game now" — Relay BIND transport failure (+ a duplicate NetworkPrefab)

## Hand-off Brief

1. **What happened.** A client reaches the lobby then the session dies; the console shows a host-side Unity Relay `BIND` transport failure that shuts the host down, plus a separate deterministic NGO `duplicate GlobalObjectIdHash` for the Ugues prefab.
2. **Where the case stands.** The recent `fix-join-load-connect-timeout` change is REFUTED as the cause (the client fully synchronized — liveness started — so the connect wait succeeded). Primary blocker = Unity Relay `BIND` failure (host transport, infra-level). Secondary Confirmed bug = `MarqueHurluberluges` listed twice in `DefaultNetworkPrefabs.asset`.
3. **What's needed next.** Confirm whether the Relay failure is every-time or intermittent (flake vs config), and fix the duplicate prefab entry regardless. Both are independent of the connect-timeout fix.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-13                                                                 |
| Status           | Active (root cause of the join-blocker Deduced = Relay infra; 2nd bug Confirmed) |
| System           | Unity 6000.2.6f2, NGO 2.12.0, editor MPPM "2 player" + Unity Relay/UnityTransport |
| Evidence sources | Live Unity console, screenshot, DefaultNetworkPrefabs.asset, git log       |

## Problem Statement

User: "j'arrive pas du tout à correctement join une game maintenant." Screenshot console: Relay BIND failure + host shutting down. Perceived as a regression ("maintenant"), suspected to follow the connect-timeout fix.

**Premise challenged:** the connect-timeout fix is NOT implicated (Deduction 1). The visible blocker is a Relay transport failure, orthogonal to the client connect wait.

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| Screenshot console | Available | "Failed to establish connection with the Relay server (server didn't answer any BIND)"; "Transport failure! Relay allocation needs to be recreated"; "[Netcode] Host is shutting down due to network transport failure of UnityTransport"; "[LEAVE][PHASE3] OnTransportFailure: pureClient=False expected=False -> notify=False" |
| Live Unity console (read_console) | Available | Client DID connect: "[LEAVE][PHASE3] Client started (pureClient=True)", "[LIVENESS] service started (role=client, enrolled=1)"; later "OnClientStopped ... expected=False -> notify=True" (host lost). Also "NetworkPrefab (MarqueHurluberluges) has a duplicate GlobalObjectIdHash source entry value of: 1532222859!" |
| `Assets/DefaultNetworkPrefabs.asset` | Available | GUID `204fb94d3cde965429951945c1b973f1` present at lines 18 AND 158 (only duplicate of 30 entries) |
| git log | Available | Duplicate introduced in `7bd162ef feat(roles): add Ugues power-thief role` — NOT today's enablement, NOT the connect-timeout change |
| Relay failure frequency (every-time vs flake) | Missing | Decisive for flake-vs-config; needs user repro count |
| Full timestamped console of ONE failing join | Partial | Screenshot + live read are two different moments; a single clean capture would nail the timeline |

## Timeline of Events (reconstructed)

| Time | Event | Source | Confidence |
| --- | --- | --- | --- |
| — | Client joins lobby, "Client started (pureClient=True)" | live console | Confirmed |
| — | Client finishes GameScene sync → "[LIVENESS] service started (role=client)" | live console | Confirmed |
| 15:51:10 | Lobby heartbeat sent (Unity Lobby service keepalive) — normal | screenshot | Confirmed |
| 15:51:14 | Host: Relay "server didn't answer any BIND" → UnityTransport failure → host shuts down | screenshot | Confirmed |
| 15:51:14 | Host OnTransportFailure (pureClient=False) | screenshot | Confirmed |
| after | Client: "OnClientStopped ... expected=False -> notify=True" (host lost) | live console | Confirmed |

## Confirmed Findings

### Finding 1: The client fully connected before the failure
"[LEAVE][PHASE3] Client started (pureClient=True)" + "[LIVENESS] service started (role=client, enrolled=1)". The client-role liveness starts in `CompositionRoot.Awake`, which runs only after GameScene finished loading and spawning — so the client completed NGO synchronization and was in the lobby (screenshot: Player 2 shows "Attribution de rôle"). The connect wait returned success.

### Finding 2: Host-side Unity Relay BIND transport failure kills the session
Screenshot: "Failed to establish connection with the Relay server (server didn't answer any BIND)" → "Transport failure! Relay allocation needs to be recreated, and NetworkManager restarted" → "[Netcode] Host is shutting down due to network transport failure of UnityTransport" → "[LEAVE][PHASE3] OnTransportFailure: pureClient=False expected=False -> notify=False". This is a UnityTransport/Relay carrier-level failure on the HOST, distinct from the NGO connection/scene layer.

### Finding 3: `MarqueHurluberluges` is registered twice in the NetworkPrefab list
`Assets/DefaultNetworkPrefabs.asset` lists GUID `204fb94d3cde965429951945c1b973f1` at line 18 AND line 158 — the only duplicate among 30 entries. Hence NGO: "NetworkPrefab (MarqueHurluberluges) has a duplicate GlobalObjectIdHash source entry value of: 1532222859!" and "Removing invalid prefabs...". Introduced in `7bd162ef` (Ugues prefab add), committed (clean tree).

## Deduced Conclusions

### Deduction 1: The connect-timeout fix (fix-join-load-connect-timeout) is not the cause
**Based on:** Finding 1.
**Reasoning:** The change only affects the client's connect WAIT; the client is proven to have connected and synced (liveness started). The failure occurs AFTER a successful join, on the HOST's transport. A client-side wait cannot cause a host relay BIND failure.
**Conclusion:** Refuted. Reverting the connect-timeout change would not fix this.

### Deduction 2: The join-blocker is the Relay BIND failure, not the duplicate prefab
**Based on:** Findings 2, 3 + git history.
**Reasoning:** The duplicate prefab predates the symptom (in the repo since `7bd162ef`), and NGO tolerates it symmetrically (logs + removes the invalid entry) — it does not fail the transport. The session dies specifically from a UnityTransport/Relay BIND failure, a different layer entirely.
**Conclusion:** Relay BIND failure = the blocker (Deduced, infra-level). Duplicate prefab = a real but separate deterministic defect (Confirmed), not the join-blocker.

## Hypothesized Paths

### Hypothesis 1: Relay BIND failure is a transient Unity Relay service / editor-MPPM flake
**Status:** Open
**Theory:** "server didn't answer any BIND" is a known transient Relay/UnityTransport condition (service hiccup, allocation churn, MPPM virtual-player networking). Intermittent.
**Would confirm:** It succeeds on retry / different times; not every attempt fails.
**Would refute:** It fails EVERY attempt now → points to a config/allocation regression (e.g. transport settings, relay region, allocation lifetime) rather than a flake.

### Hypothesis 2: The duplicate prefab destabilizes late-join / spawn under some conditions
**Status:** Open (low)
**Theory:** A duplicate GlobalObjectIdHash could interact badly with client synchronization in edge cases.
**Would confirm:** Join failures persist after fixing the transport but before removing the duplicate.
**Would refute:** Removing the duplicate changes nothing about join stability (expected — NGO already removes it).

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | --- | --- |
| Relay failure frequency | Flake (H1) vs config regression | User retries join ~5×; record how many fail |
| One clean timestamped console for a single failing join | Confirms exact ordering | Clear console, one join attempt, capture full log |
| Whether Steam/Facepunch transport also fails | Isolates Relay-specific vs general | Try a Steam build/host instead of editor Relay |

## Source Code Trace

| Element | Detail |
| --- | --- |
| Blocker origin | UnityTransport/Relay layer (not project code): "server didn't answer any BIND" → `OnTransportFailure` handled by `ClientDisconnectHandler` ([LEAVE][PHASE3]) |
| Duplicate-prefab origin | `Assets/DefaultNetworkPrefabs.asset:18` and `:158` — same GUID `204fb94d3cde965429951945c1b973f1` |
| Trigger | Host creates a Relay allocation (`MainMenu.HostWithUnityRelay`), then BIND to the relay server gets no response |
| Related files | `Assets/Scripts/Network/ClientDisconnectHandler.cs` (transport-failure reaction), `Assets/Scenes/BootScene.unity` (UnityTransport config), `Assets/DefaultNetworkPrefabs.asset` |

## Conclusion

**Confidence:** Medium (blocker Deduced as Relay-infra; duplicate-prefab defect Confirmed).

The reported "can't join now" is a host-side **Unity Relay BIND transport failure** that shuts the host down after the client has already joined — an infra/transport-layer condition, most likely a transient Relay/editor-MPPM flake (needs frequency confirmation). The **connect-timeout fix is refuted** as the cause (the client fully synchronized). Separately, a **Confirmed deterministic bug** exists: the `MarqueHurluberluges` (Ugues) prefab is listed twice in `DefaultNetworkPrefabs.asset` (lines 18 & 158, since commit `7bd162ef`), producing the duplicate GlobalObjectIdHash error; NGO tolerates it today but it should be fixed.

## Recommended Next Steps

### Fix direction
- **Duplicate prefab (trivial, do it):** remove one of the two `MarqueHurluberluges` entries from `Assets/DefaultNetworkPrefabs.asset` (keep line 18, delete the line-158 block, or re-run NGO's "Generate Default NetworkPrefabs List" which de-dups). Verify the list drops to 29 unique entries and the console error clears.
- **Relay BIND failure (diagnose before coding):** first establish frequency. If intermittent → Unity Relay flake; a host-side allocation retry/recreate on `OnTransportFailure` would harden it, but it is not a code defect. If every-time → inspect Relay region/allocation config and the UnityTransport relay data wiring in `MainMenu.HostWithUnityRelay`; also test the Facepunch/Steam transport path to isolate Relay-specific failure.

### Diagnostic
Clear the console, attempt one host+join, capture the full timestamped log. Retry ~5× to score flake-vs-deterministic. Try a Steam-transport host to see if BIND failure is Relay-only.

## Reproduction Plan

- Setup: editor MPPM "2 player", Unity Relay transport, host in main editor + virtual Player 2.
- Trigger: host a lobby, join with Player 2.
- Observed: Player 2 reaches the lobby (Attribution de rôle), then within seconds the host logs a Relay BIND failure and shuts down; Player 2 sees host-lost.
- To characterize: repeat and count failures; compare against a Steam-transport host.

## Follow-up: 2026-07-13

### New Evidence
- Relay BIND failure did NOT recur on retry (user) — it was a one-off flake, NOT the persistent blocker.
- Fresh console (join "ahaha" → lobby "aaaaaa"), NO relay error: `[LEAVE][PHASE3] Client started (pureClient=True)` → `[LIVENESS] service started (role=client, enrolled=1)` → `OnClientStopped(wasHost=False): pureClient=True expected=False -> notify=True` → lobby left. Same drop-after-sync pattern, repeatable.

### Additional Findings
- **Finding 4 (Confirmed): the duplicate NetworkPrefab is BENIGN.** NGO on a duplicate source hash logs an error and `return false` — it merely SKIPS the second entry (`Library/PackageCache/com.unity.netcode.gameobjects@aaabf07f880c/Runtime/Configuration/NetworkPrefabs.cs:304`). No throw, no disconnect. So the `MarqueHurluberluges` duplicate is NOT the join-blocker (still worth removing for cleanliness).
- **Finding 5 (Confirmed): the client is FULLY connected before the drop.** `[LIVENESS] service started` fires in `CompositionRoot.Awake`, which runs only after GameScene finished loading + spawning on the client — so NGO synchronization completed and the client was in the lobby. It is then dropped unexpectedly (`OnClientStopped ... notify=True` via `ClientDisconnectHandler.HandlePotentialHostLoss`, `Assets/Scripts/Network/ClientDisconnectHandler.cs:224`).

### Updated Hypotheses
- **Hypothesis 3 (Open, primary now): a fully-synced client is dropped seconds after join by a host-side disconnect or transport drop.** Not liveness (no `[LIVENESS] client declared the host lost` log, and the drop is faster than the 5-beat/5 s window). Not the connect-timeout fix (no post-join effect — REFUTED, Deduction 1). Not the duplicate prefab (Finding 4). **Would confirm:** NGO Developer-level log showing the disconnect reason / `Disconnecting client` on host, or a host-side exception during the client's `LobbyState.OnClientConnected` → `AddNewCharacter` / avatar spawn. **Would refute:** the reason turns out to be a transport keepalive timeout (relay) after all.

### Backlog Changes
- Need NGO `LogLevel = Developer` on the NetworkManager to surface the disconnect reason, + one clean single-attempt console capture. This is the decisive missing evidence.

### Follow-up #2: 2026-07-13 — user reframes to the liveness merge
- **User:** join worked BEFORE; not a today-commit; suspects the older **liveness/heartbeat** merge (matches memory: liveness layer merged to Dev but "NEEDS real 2-build playtest" — never done).
- **Finding 6 (Confirmed): `HandlePlayerLeft` NEVER disconnects a client at the NGO level.** `grep DisconnectClient` = zero matches in the whole repo. The server-liveness terminal decision routes `PeerLost → GameManager.HandlePlayerLeft` (`Assets/Scripts/GameLogic/GameManager.cs:644-647`), which removes/chains the Character but does NOT close the client's transport connection. **⇒ the server-role liveness cannot cause the client's `OnClientStopped`.** REFUTES "host kicks the client via liveness."
- **Hypothesis 4 (Open, PRIMARY): the CLIENT-role liveness falsely declares the HOST lost ~5 s after join.** The only liveness path that stops the client's own NGO: `LivenessService` (client role) enrolls the host on `CompositionRoot.Awake`, ticks the host's miss counter at 1 Hz, and if it does not receive host keepalives for 5 beats calls `ClientDisconnectHandler.NotifyLivenessHostLost` → `HandlePotentialHostLoss("LivenessHostLost")` → `ReturnToMenu` → `Shutdown`. Fits the ~seconds-after-join drop and the user's "heartbeat kicks players." **Would confirm:** a `[LIVENESS] client declared the host lost (no keepalives)` log + a `[LEAVE][PHASE3] LivenessHostLost: ...` line in a clean Developer-level capture (note: if this fires first, the subsequent `OnClientStopped` is latched by `_handlingLoss` and does NOT re-log — so the earlier console showing `OnClientStopped` as the source is either a transport-first drop OR the `LivenessHostLost` line was outside the truncated window). **Would refute:** the clean capture shows `OnClientStopped` with NO preceding liveness log ⇒ a transport/relay drop, not liveness. **Open sub-question if confirmed:** WHY host keepalives fail to reach a healthy just-joined client in editor MPPM (bridge spawn/readiness timing, RPC routing via `GetSafeRpcTarget`, or MPPM RPC delivery).

## Follow-up #3: 2026-07-13 — ROOT CAUSE CONFIRMED (ConnectionApproval mismatch)

### Decisive evidence (Developer-level host console + user repro)
Host console, clean single attempt: `[LIVENESS] service started (role=server, enrolled=0)` → `[Netcode] Incomplete connection request message given config - possible NetworkConfig mismatch.` (`ConnectionRequestMessage.cs:133`) → `[LEAVE] Player 1 left in lobby — removing character.` The server-liveness NEVER enrolled the client (`enrolled=0`); the client is rejected at the FIRST handshake message, before any liveness. Client game view stays on the CORRUPTED main menu (never reaches the lobby).

### Finding 7 (Confirmed): asymmetric `NetworkConfig.ConnectionApproval` → every join rejected
1. `NetworkConnectionManager.cs:734`: `ShouldSendConnectionData = NetworkManager.NetworkConfig.ConnectionApproval` — the client writes ConnectionData in its ConnectionRequest iff ITS OWN `ConnectionApproval` is true.
2. `Assets/Scenes/BootScene.unity:609`: `ConnectionApproval: 0` — the serialized NetworkManager default is FALSE.
3. `Assets/Scripts/Network/ConnectionApprovalGate.cs:43` sets `ConnectionApproval = true`, and `ConnectionApprovalGate.Enable` is called ONLY on the host paths (`Assets/Scripts/UI/MainMenu.cs:471`, `:495`) — NEVER on the client join path (`JoinWithFacepunch`/`JoinWithUnityRelay`).
4. ⇒ Host `ConnectionApproval=true`, client `ConnectionApproval=false`. Client `Serialize` (`ConnectionRequestMessage.cs:79-87`) writes only `ConfigHash` (8 B); server `Deserialize` in the approval branch (`ConnectionRequestMessage.cs:129`) does `TryBeginRead(sizeof(ConfigHash)=8 + sizeof(int)=4 = 12 B)` → underrun → `LogWarning("Incomplete connection request message given config - possible NetworkConfig mismatch")` → `DisconnectClient(senderId)` (`:133-136`).
5. Deterministic: EVERY client is rejected at the connection-request handshake.

### Finding 8 (Confirmed): the regression is commit `cb4c3b67`
`git log` — `ConnectionApprovalGate.cs` was introduced by `cb4c3b67 fix(network): reject clients joining a game already in progress`. Before it, `ConnectionApproval` was false on BOTH sides (matched) → joins worked. After it, host-only enable → asymmetric config → all joins rejected. This is the "worked before / an older commit broke it" the user reported — it is the JOIN-GATE commit, NOT the liveness commit.

### Exonerations (all Confirmed)
- **connect-timeout fix (fix-join-load-connect-timeout):** unrelated — rejection is at the handshake, before the client wait matters.
- **liveness/heartbeat layer:** `enrolled=0` on the host; `HandlePlayerLeft` has no `DisconnectClient` anywhere in the repo; the client-role false-host-lost path never runs because the client never connects. (Subagent trace noted a SEPARATE latent liveness join-window race — no grace tied to the first received keepalive — worth a future story, but NOT this bug.)
- **duplicate `MarqueHurluberluges` prefab:** NGO logs + skips it (`NetworkPrefabs.cs:304`), benign.
- **Relay BIND failure:** a one-off flake that did not recur.

### Updated Conclusion
**Confidence: High.** Root cause = asymmetric `NetworkConfig.ConnectionApproval` (host true, client false) introduced by `cb4c3b67`, which makes NGO reject every client's connection request as "Incomplete ... possible NetworkConfig mismatch" and `DisconnectClient` it at the first handshake message. Deterministic; blocks all joins.

### Fix direction (trivial)
Make `ConnectionApproval` symmetric on both peers. The client needs the FLAG true so it sends the ConnectionData the server expects; it does NOT need the server-only approval callback. Options:
1. **Serialized default (simplest, recommended):** set `ConnectionApproval: 1` on the BootScene NetworkManager so BOTH host and client inherit `true`; the host still installs the approval CALLBACK via `ConnectionApprovalGate.Enable` (keep the gate for the mid-game reject). Client sends empty `ConnectionData` (approved by lobby-phase check, payload unused). Verify with a real 2-peer join.
2. **Client-path enable:** in `JoinWithFacepunch`/`JoinWithUnityRelay` (before `StartClient`), set `NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true` (flag only, no callback needed on the client).
Either removes the format divergence. Add a test/guard that host and client agree on `ConnectionApproval`.

### Note
`cb4c3b67`'s host-only playtest almost certainly hid this (a host never sends itself a ConnectionRequest; only a real remote client trips it) — the same host-only-blindspot pattern flagged elsewhere in this project.

## Side Findings

- Pre-existing console noise unrelated to the blocker: `GameAssetHolder: DontDestroyOnLoad only works for root GameObjects` (`Assets/Scripts/GameAssetHolder.cs:21`), `Failed to get lobbies: NRE` (`LobbySelectionPanel.cs:165`), `Échec de quitter le lobby: lobby not found` (`LobbyManager.cs:320`), "The referenced script (Unknown) ... is missing", FMOD port-in-use, DOTween safe-mode captures. None implicated in the transport failure; worth a separate cleanup pass.
