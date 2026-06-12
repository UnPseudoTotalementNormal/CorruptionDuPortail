# Story 10.1: Inject ChatManager (fan-in 17)

Status: ready-for-dev

## Story

As a developer,
I want `ChatManager` consumers to receive it injected (behind `IChatService` where a narrow slice helps),
so that the highest-fan-in remaining singleton stops being a global.

## Acceptance Criteria

1. **Recipe §7 applied to ChatManager** (replicated NetworkBehaviour singleton, 216 LOC, fan-in 17): usage census → optional `IChatService` slice (extract ONLY if the census shows a clean read/send split; otherwise inject concrete — record the call) → root accessor → consumers migrated per lane → static narrowed/annotated.
2. **Chat behaviour unchanged on the fixture:** messages, channels (`ChatWindowIDs`), server messages, and the bot routing (`ChatManager` RPCs carry `GetSafeRpcTarget` territory — bodies untouched).
3. **Known consumers** (recon: PTruthChains' chat line, SendMessagePanel 5 hits, ChatWindow, powers' chat messages — census at dev time) migrated in batches; registry/guards green.
4. **Gated:** suite + fixture + goldens unchanged per batch; boot smoke includes sending a chat message host→bot context.

## Tasks / Subtasks

- [ ] **Task 1:** Census + slice decision (Dev Agent Record).
- [ ] **Task 2:** Root accessor + (optional) interface; inert commit.
- [ ] **Task 3:** Batch migration per lane (powers via `Power` base field extension; scene UI lane A; spawned lane C).
- [ ] **Task 4:** Static narrowing + recorded leftovers; gates; sprint-status; commits.

## Dev Notes

- Epic 10 cadence: one proven recipe (Epics 7-9), smaller targets. The per-story judgment is the SLICE decision (interface or concrete) — D-NFR6: interface where logic/tests benefit, concrete for presentation leaves.
- `ChatManager.SendChatMessageServerRpc` is called from server AND client contexts — verify the migrated access path resolves identically in both (fixture case).
- Staleness: census-driven; fan-in 17 is the 2026-06-11 figure.

### Project Structure Notes

- Modified: `ChatManager.cs` (static narrowing), `CompositionRoot.cs`, consumer files, registry; optional new `IChatService.cs`. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- `Network/`-surface rule: ChatManager keeps its NGO plumbing; consumers see the service surface only. GetSafeRpcTarget verbatim.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7 recipe, §8 Epic 10] / [epics.md#Story 10.1]
- [Source: Assets/Scripts/ChatSystem/ChatManager.cs].

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
