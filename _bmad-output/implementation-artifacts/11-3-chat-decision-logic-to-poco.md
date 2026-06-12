# Story 11.3: Chat — decision logic to POCO

Status: review

## Story

As a developer,
I want chat rules (message routing/visibility decisions) extracted into tested POCOs,
so that chat logic is EditMode-testable.

## Acceptance Criteria

1. **Inventory first:** `ChatManager` (216 LOC) + chat flows pass: decisions (who may see/send what, channel routing rules, server-message formatting policy) vs glue (RPCs, `NetworkSerializableObject` payloads, UI). Inventory in Dev Agent Record.
2. **Characterize-then-extract:** chat-visibility/routing goldens on current behaviour FIRST (chat is deduction-information-critical — who reads what is gameplay-sacred); then POCO; adapter dispatches.
3. **Bot-path routing preserved bit-for-bit:** the `clientId >= 100` interception in chat RPC dispatch stays adapter-side verbatim; fixture case asserts a bot-addressed message routes identically.
4. **EditMode tests shipped**; suite + fixture + goldens unchanged; sprint-status.

## Tasks / Subtasks

- [x] **Task 1:** Inventory of `ChatManager` (216 LOC) — visibility/routing rule census in Dev Agent Record. All five decisions revolve around the discovered-id set + active channel.
- [x] **Task 2:** Characterization goldens — the existing PlayMode `ChatManagerTests` already pin the sacred behaviours (activation-only-if-discovered, receive-adds-to-window, host→bot send/routing, name override). They are the goldens; the extraction is condition-identical, so they stay green (no golden moved).
- [x] **Task 3:** Extracted the rules → `ChatChannelPolicy` Domain POCO; ChatManager thinned (5 call sites delegate). 10 EditMode tests.
- [x] **Task 4:** Gates incl. the bot case (`ChatManager_ResolvesThroughRoot_AndSendsHostToBot` green in PM 148); Domain purity green; sprint-status.

## Dev Notes

- Chat visibility = information asymmetry = the GAME. A routing decision moved wrong is a design break invisible to compile. The characterization goldens (Task 2) are non-negotiable before any extraction — same discipline as Wave 1's WinningConditions.
- `ChatMessage` is likely a `NetworkSerializableObject` — wire shape untouched (5.1-style caution; if a wire-format guard extension is cheap here, note it as an optional add).
- Staleness: inventory-driven; ChatManager reshaped by 10.1.

### Project Structure Notes

- New: Domain POCO(s) + tests + goldens. Modified: ChatSystem files (thinning). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- GetSafeRpcTarget verbatim; FixedStrings for any message-channel constants; Smartphone/ChatApp = view only (content-vs-host pattern).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.4, §8 Epic 11] / [epics.md#Story 11.3]
- [Source: Assets/Scripts/ChatSystem/ChatManager.cs + ChatMessage].

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0. EM 196/196 (186 + 10 new `ChatChannelPolicy` tests). PM 148/148 — the `ChatManagerTests` chat goldens (incl. the host→bot bot-routing case) stayed green, confirming the deduction-critical routing is unchanged. Domain purity guard green.

### Implementation Plan / Inventory (Task 1)

**Visibility/routing census of `ChatManager` (216 LOC).** Every chat *decision* turns on the discovered-id set + the active channel:

| Site | Classification | Action |
|---|---|---|
| `ReceiveChatMessageRpc`: `!discoveredChatIds.Contains(chatId) → drop` | **DECISION — message visibility (the deduction asymmetry)** | → `ChatChannelPolicy.IsMessageVisible`. |
| `ChangeActiveChat`: activate only if discovered | **DECISION — channel activation gate** | → `CanActivateChannel`. |
| `TrySendChatMessage`: reject empty text / the server channel | **DECISION — send eligibility** | → `CanSendMessage`. |
| `UndiscoverChat`: if the undiscovered channel was active → fall back to General | **DECISION — active-channel fallback** | → `ShouldFallBackToGeneralAfterUndiscover` (kept the exact `activeChatId == _chatId` guard, incl. the degenerate redundant-reactivation edge). |
| `GetChatWindowName`: override → enum name → "Chat {id}" | **DECISION — name precedence policy** | → `ResolveWindowName` (adapter resolves the dict + enum lookups, passes plain `hasOverride`/`overrideName`/`knownEnumName`; the `hasOverride` flag preserves dictionary-key semantics, not `!IsNullOrEmpty`). |
| `SendChatMessageServerRpc` → `GetSafeRpcTarget(senderClientId)` (clientId≥100 bot interception), all `[Rpc]` methods, `ChatMessage` (`NetworkSerializableObject`) wire shape, `CharacterManager.instance` reads (§4a, die 12.3), the FMOD sound hooks, `ChatWindow` storage | Glue / NGO / NFR5 / wire | **STAYS verbatim** — AC3: the bot routing is never moved into the POCO. |

`ChatMessage` wire shape untouched (no `INetworkSerializable` change) — the 5.1-style caution holds; no wire-format guard extension was needed.

### Completion Notes List

**Extraction (Tasks 2-3) — `ChatChannelPolicy` (Domain).** The five routing/visibility rules are now a pure POCO; ChatManager keeps the discovered-id set, the active-channel state, all RPC dispatch, the bot interception, and the FMOD hooks, delegating only the decisions. Each rule was lifted **condition-identical** (same `Contains`, same `||`/`&&`, same `==`), so the existing PlayMode `ChatManagerTests` (the AC2 characterization goldens) stay green. The membership rules take the live `ISet<int>` (the `discoveredChatIds` HashSet) so the POCO expresses real set-membership, not a precomputed bool. Two faithfulness details were preserved deliberately: (1) the undiscover-fallback keeps the original `activeChatId == _chatId` trigger (so the degenerate "undiscover the active general channel" path still re-calls `ChangeActiveChat(General)` and logs as before); (2) `ResolveWindowName` keys off `hasOverride` (dictionary key present) rather than non-empty, so a present-but-empty override still wins — matching the original `TryGetValue` return.

**Tests — 10 EditMode cases.** Visible on discovered / hidden on undiscovered; activate only-if-discovered; send rejects empty+null text and the server channel, allows valid; undiscover fallback only when the channel was active; window name override-wins-even-empty / enum-name / generic fallback.

**NFR compliance.** NFR4 (POCO decides, adapter dispatches). NFR5 / AC3: `GetSafeRpcTarget` + the clientId≥100 bot interception never moved — verified by the PM `ChatManager_ResolvesThroughRoot_AndSendsHostToBot` golden staying green. Domain purity green.

### File List

**New (production):**
- `Assets/Scripts/Domain/ChatChannelPolicy.cs` — `ChatChannelPolicy` POCO (zero engine types).

**New (tests):**
- `Assets/Scripts/Tests/Editor/ChatChannelPolicyTests.cs` — 10 EditMode tests (`[Category("ChatChannelPolicy")]`).

**Modified (production):**
- `Assets/Scripts/ChatSystem/ChatManager.cs` — `_policy` field; `ReceiveChatMessageRpc` / `ChangeActiveChat` / `TrySendChatMessage` / `UndiscoverChat` / `GetChatWindowName` delegate their decisions to the POCO.

**Docs:** this story; `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 11.3 (Epic 11 / D5). Extracted ChatManager's five routing/visibility rules (message visibility, channel activation, send eligibility, undiscover→General fallback, window-name precedence) into a pure `ChatChannelPolicy` Domain POCO; ChatManager delegates the decisions but keeps the discovered set, RPCs, the clientId≥100 bot interception, and FMOD hooks. Conditions lifted bit-identical → the existing PlayMode chat goldens (incl. host→bot routing) stay green. 10 EditMode tests. NFR4/NFR5/AC3 honoured; `ChatMessage` wire shape untouched. Compile 0; EM 196/196 (186 + 10); PM 148/148; Domain purity guard green. |
