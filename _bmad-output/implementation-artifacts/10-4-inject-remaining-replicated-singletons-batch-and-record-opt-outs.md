# Story 10.4: Inject the remaining replicated singletons (batch) + record opt-outs

Status: ready-for-dev

## Story

As a developer,
I want StatesCanvas, MessageManager, GameAudioManager, ChainingManager, LobbyPlayerInfoHolder migrated or explicitly opted out,
so that the replicated-singleton census closes with every survivor recorded.

## Acceptance Criteria

1. **Each of the 5 gets a decision:** migrate per recipe §7 (census → root accessor → consumers per lane → static narrowed) OR recorded verify-don't-force opt-out with reason. No silent skips.
2. **`GameAudioManager` is the explicit opt-out candidate** (global audio façade; FMOD no-op-safe constraint; consumed from everywhere including non-injectable contexts). If opted out: recorded in the architecture doc §4 census with the reason; its static stays whitelisted; consumers untouched.
3. **`ChainingManager`** consumers were partially migrated in 7.4 (pass-through slice) — this story finishes its direct-static consumers (e.g. `PTruthChains:43` — note: PTruthChains is in the guard registry; migrating its ChainingManager read makes its registry entry FULLY clean).
4. **`LobbyPlayerInfoHolder`** is itself NGO-spawned — its consumers use lane C/B; its own 1 instance-hit rerouted.
5. **Census table updated** in architecture doc §4 (each of the 8 replicated singletons: migrated / opt-out + reason).
6. **Gated:** suite + fixture per batch; registry/guards green; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Per-singleton census (5 targets); decisions table draft.
- [ ] **Task 2:** Migrate the migrate-set in batches per lane (root accessors added per target).
- [ ] **Task 3:** Record opt-outs (doc §4 census); guard whitelist updated if needed (an opted-out static must NOT trip the guard for consumers that keep using it — keep it OUT of ForbiddenLocators, document).
- [ ] **Task 4:** Gates; sprint-status; commits per target.

## Dev Notes

- The danger in batch stories is rote application where judgment is needed: each of the 5 has different consumer shapes (StatesCanvas = UI host; MessageManager = send flows with GetSafeRpcTarget territory; LobbyPlayerInfoHolder = player-info reads from everywhere). Census FIRST, per target; one commit per target, not one mega-commit.
- FMOD rule if GameAudioManager IS migrated instead: every consumer keeps going through it (never AudioSource) — injection changes resolution, not the audio architecture.
- Staleness: census-driven; consumer sets reshaped by Epics 7-9.

### Project Structure Notes

- Modified: per-target manager files, `CompositionRoot.cs`, consumers, architecture doc §4, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- FMOD via GameAudioManager only (whether migrated or opted out). GetSafeRpcTarget verbatim in MessageManager flows. Persistent EventInstance release rules untouched.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §4 census, §7, §8 Epic 10] / [epics.md#Story 10.4]
- [Source: Assets/Scripts/Characters/Powers/PTruthChains.cs:43-46] — the ChainingManager/Chat/LobbyPlayerInfoHolder consumer cluster from 6.3's scope fence.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
