---
title: 'NET-11 — Chat: server-owned channel membership and server routing'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-01-replicated-snapshot-and-roster.md']
fixes: ['F18']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Channel membership (`discoveredChatIds`) exists only on each client, filled by targeted
`DiscoverChatRpc`/`UndiscoverChatRpc` (`ChatManager.cs:105-150`; callers `PBoundByInk.cs:102,132`,
`DiscoverChatExecutor.cs:15` — e.g. the anomaly channel granted at game start by `EyeOfTheVoidDecision`). Every chat
message is broadcast to **every** client (`ReceiveChatMessageRpc`, `ClientsAndHost`, `:177-201`) and each client filters
on its local membership. Whether a message is kept therefore depends on whether it reached that client before or after
its discovery RPC, so two members of the same channel can see different conversations. Private channels (anomaly chat,
"Lié par l'encre") are also sent to players who must not read them, and the sender id inside `ChatMessage` is
client-supplied (`TrySendChatMessage`, `:114-123`).

**Approach:** The server owns membership (per channel → set of clientIds, simulated bots included). A sent message is
validated (sender = `SenderClientId` or host-for-bot, sender is a member, channel writable) and routed **only to the
members at the moment the server processes it** (`GetSafeRpcTarget`). A membership change is pushed to the new member
on the same `ChatManager` object **before** any message routed to it, so reliable ordered delivery guarantees the client
knows the channel when its first message arrives. Clients keep their local channel list as a projection of what the
server pushed. A member sees **only messages sent after it joined the channel** (no history).

## Boundaries & Constraints

**Decided (Poyo, 2026-10-04):**
- No backlog: a new member of a private channel sees only messages sent after its membership was registered on the server.

**Always:**
- Same channels, names, overrides, sounds and undiscover fallback (`ShouldFallBackToGeneralAfterUndiscover`) as today.
- General + Server channels: implicit membership for everyone (no regression for public chat).
- Local system lines (`AddMessageLocal`) stay client-local (they are already per-viewer by construction).
- The cutoff for "after it joined" is the **server's** membership time, never the client's receipt time — two members
  who joined before a message always both see it.
- Membership mutations only from server code (executors, `PBoundByInk`); bots handled through `GetSafeRpcTarget` /
  host-keyed state like `PlayerIconManager`.

**Never:**
- Send a private-channel message to a non-member.
- Accept a client-supplied sender id.
- Drop a routed message on the client because its membership "is not known yet".

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| A and B both members, A writes | B receives it, regardless of when B's client processed its own discovery |
| B becomes a member after A wrote | B does not see that earlier message (by design) |
| Non-member | Never receives private messages |
| Forged sender id | Message attributed to the real sender |
| Undiscover (Bound by Ink ends) | Member removed server-side; client falls back to General if active |
| Bot member 101 | Host receives on its behalf, keyed by viewer |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/ChatSystem/ChatManager.cs:30,105-150,177-201` -- server membership; routed delivery to members; membership push before routed messages; delete client-side filtering of broadcasts
- `Assets/Scripts/ChatSystem/ChatMessage.cs` -- sender set by server
- `Assets/Scripts/Characters/Powers/PBoundByInk.cs:95-135`, `Characters/Powers/Runtime/Executors/DiscoverChatExecutor.cs` -- call server membership API
- `Assets/Scripts/Domain/` -- `ChatChannelPolicy` (existing) extended with a membership POCO, EditMode-tested
- UI: `ChatSystem/ChatPanel.cs` (render from pushed state)

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/ChatMembershipTests.cs` -- NEW (membership add/remove, routing set, implicit public channels)
- [ ] `Tests/PlayMode/Chat/ChatRoutingTests.cs` -- NEW red-first 2-NM: message sent right after the server registered B but before B's client processed its discovery → dropped today (red), delivered after; non-member never receives; forged sender corrected; message sent before B joined is not shown to B
- [ ] Implement, migrate callers

**Acceptance Criteria:**
- Given two members of a private channel, then both see exactly the same messages sent since both were members.
- Given a non-member, then no private message ever reaches its client.
- Given a player added to a channel, then it sees no message sent before it was added.

## Verification

- EditMode + PlayMode new suites; full suites green
- 2-build playtest: two anomalies chatting from the very first second of the game; a Bound-by-Ink pair; a third player sees neither
