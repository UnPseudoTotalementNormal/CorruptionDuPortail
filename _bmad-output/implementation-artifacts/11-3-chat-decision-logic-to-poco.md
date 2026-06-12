# Story 11.3: Chat — decision logic to POCO

Status: ready-for-dev

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

- [ ] **Task 1:** Inventory; visibility/routing rule census.
- [ ] **Task 2:** Characterization goldens (PlayMode where NGO-coupled).
- [ ] **Task 3:** Extract POCO(s); EditMode tests; adapters thinned.
- [ ] **Task 4:** Gates incl. fixture bot case; sprint-status; commits.

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

### Debug Log References

### Completion Notes List

### File List
