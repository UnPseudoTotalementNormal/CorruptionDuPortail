# Story 12.3: Final sweep — kill the remaining statics + whole-track DoD gate

Status: ready-for-dev

## Story

As a developer,
I want every remaining project static except `CompositionRoot` (and the recorded exceptions) removed, and the whole-track definition of done verified,
so that the despaghettification closes with the endgame census true, not aspirational.

## Acceptance Criteria

1. **Destructive deletion of the remaining statics:** `GameManager.instance`, `CharacterManager.instance` façades (their recorded callers were resolved by 12.2 or are recorded survivors), and any leftover the census shows — deleted; compiler enumerates stragglers; each rerouted or its survival re-recorded with reason.
2. **Static-absence proof:** an assembly-scan EditMode test (model: `LeafPocoNoFacadeGuardTests`) fails if any non-whitelisted project type exposes a static `instance`/`Instance` — the whitelist = `CompositionRoot` + the recorded §4 census survivors, hardcoded in the test WITH their recorded reasons as messages.
3. **§4 census verified TRUE:** 1 surviving project static (`CompositionRoot`) + engine-owned `NetworkManager.Singleton` + recorded exceptions only. The 8.4 decision's façade status (if "close" was chosen) is consistent with the DoD wording.
4. **Whole-track DoD holds (architecture doc §10):** no God Object grab-bag (GameManager = game-loop adapter; CharacterManager = character adapter); narrow injected interfaces; tested POCOs; both guards green over every migrated type; full EditMode + PlayMode suite green at baseline; boot smoke green (full game start→finish, bots included).
5. **Merge-ready:** one merge to `Dev` prepared — changelog of Epics 6-12, NFR5 untouched-proof (diff audit over `GetSafeRpcTarget`/`IsLocalOrSimulated`/`clientId >= 100` bodies across the whole track: byte-identical), goldens unchanged. The merge itself is Poyo's call (protect `Dev` — [[project-refactor-branch]]).

## Tasks / Subtasks

- [ ] **Task 1:** Final static census; partition delete/survive; destructive deletions; reroutes.
- [ ] **Task 2:** Static-absence guard test (whitelist + reasons); prove it bites (synthetic singleton).
- [ ] **Task 3:** DoD audit (§10 point by point, evidence pasted into Dev Agent Record).
- [ ] **Task 4:** NFR5 diff audit (git diff over the track range filtered to the three symbols' defining files — bodies byte-identical).
- [ ] **Task 5:** Full gates (suite, fixture, both guards, boot smoke start→finish). Sprint-status: `12-3` + `epic-12 → done`; track complete. Merge prep handed to Poyo (no merge without his go).

## Dev Notes

- The point of THIS story is that the endgame is CHECKED, not assumed: census true, guards bite, NFR5 provably untouched, DoD evidenced. Budget real time for the audits (Tasks 3-4) — they are the deliverable.
- 8.4-decision interaction: if index ownership was closed as won't-do, the adapter-owns-NV shape is a RECORDED design, and the DoD/no-façade wording was already amended (8.4 AC) — verify consistency, don't relitigate.
- Merge to Dev: ONE merge, at the end, Poyo triggers — never auto-merge ([[feedback-review-required-gate]] + branch protection memory).

### Project Structure Notes

- Modified: façade-bearing manager files (deletions), new absence-guard test, architecture doc (final census). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Branch/PR conventions (Dev = PR target, CI green); commit body explains the why (this one's body = the track summary, Discord-surfaced).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §4 census, §10 DoD] / [epics.md#Story 12.3]
- [Source: Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs] — the absence-guard model.
- [Source: _bmad-output/implementation-artifacts/12-2-*.md] — previous story.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
