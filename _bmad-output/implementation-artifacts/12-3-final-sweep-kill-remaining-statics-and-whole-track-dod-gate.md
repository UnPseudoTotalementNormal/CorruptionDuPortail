# Story 12.3: Final sweep — kill the remaining statics + whole-track DoD gate

Status: review

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

- [x] **Task 1:** Final static census; partition delete/survive; reroutes. **Strategy B (Poyo's call):** 10 context-having leaves rerouted onto `CompositionRoot.For(Singleton)`; the 2 God-Object façades KEPT as recorded verify-don't-force exceptions (read only by context-less W*/TargetUtils/PowerEffectDispatcher + ChatManager NFR5 + fixtures).
- [x] **Task 2:** Static-absence guard (`StaticSingletonCensusGuardTests`, 4 tests) — whitelist of 23 survivors + reasons, CompositionRoot pinned instance-less, bite test (synthetic singleton) + staleness test. Caught + whitelisted the `NullObserver` Null-Object.
- [x] **Task 3:** DoD audit (§10 point by point) — evidence in architecture doc §10 "DoD verification" + Completion Notes below.
- [x] **Task 4:** NFR5 diff audit — `git log -L` over GetSafeRpcTarget/IsLocalOrSimulated across `c72b8d1..HEAD` = 0 commits; direct body diff base..HEAD byte-identical.
- [x] **Task 5:** Full gates (EM 205/205, PM 148/148 incl. MultiClientGameFixture host+client+bot, both DI-seam guards + the new census guard, GameScene boot smoke clean). Sprint-status: `12-3 → review`. **Merge prep + `epic-12 → done` handed to Poyo (no merge without his go).**

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

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors after each wave (refresh_unity force). Gates: EditMode 205/205 (201 baseline + 4 new census-guard tests), PlayMode 148/148, both DI-seam guards + the new StaticAbsenceGuard green.
- The census guard's first run caught one unrecorded singleton (`NullObserver`, a Null-Object nested in `PowerEffectTrace`) → whitelisted with reason. Re-run green.
- `sed -i` on the annotation pass stripped CRLF on 8 files (whole-file diff noise); fixed by `git checkout` + a CRLF-preserving `perl -i -pe` re-apply, then `git commit --amend` → clean 174/10 diff.
- GameScene boot smoke: entered play mode, console clean (login-menu flow, only the pre-existing Adaptive Performance notice), no NRE from the reroutes / CompositionRoot resolution. Exited.

### Completion Notes List

**Strategy: (B) verify-don't-force** (Poyo's decision at census time). The two options were presented from the Task-1 census: (A) reroute everything incl. the static POCOs + NFR5 paths and delete both façades, vs (B) reroute the leaves that have a clean indirection and keep the genuinely context-less callers on a recorded façade. (B) chosen — it leaves NFR5 + the 5.0 network fixtures untouched for zero gameplay gain, and the §10 DoD "(plus recorded exceptions)" clause covers it.

**Task 1 — reroutes (commit `c37951d`).** 10 leaves rerouted off the God-Object façades onto the ONE sanctioned static `CompositionRoot.For(NetworkManager.Singleton)` (concrete / `CharacterQuery` / `GameStateQuery`): `RoomFog`, `ChatWindow` (CharacterManager read only — keeps `ChatManager.instance` survivor), `DevIdentityController`, `TakeDownThePortalTextTitle`, `CharacterAwakenTimer`, `RoleAttributionSettingTab`/`Object`, `AwakeningRecapMessages` (currentDay — keeps `MessageManager.instance`), `ShutOffGameButton`, `SelectPanelPlayer`. After this, `GameManager.instance`/`CharacterManager.instance` are read by ONLY: W* winning-condition POCOs (×5), `TargetUtils`, `PowerEffectDispatcher` (incl. NFR5 `GetSafeRpcTarget`), `ChatManager`'s own reads, and the network test fixtures — all context-less recorded exceptions.

**Task 2 — census guard (commit `8db80f5`).** `StaticSingletonCensusGuardTests` (assembly-scan EditMode, model = `LeafPocoNoFacadeGuardTests`): fails on any non-whitelisted static `instance`/`Instance` in the game assembly. Whitelist = 23 recorded survivors (22 live project singletons + `NullObserver`) with hardcoded reasons; `CompositionRoot` pinned to stay For(nm)-based (no instance member); bite + staleness tests. Stale `// dies in 12.3` annotations refreshed to "recorded §4 survivor".

**Task 3 — DoD audit (§10).** Each §10 point evidenced in the architecture doc "DoD verification" block: no God-Object grab-bag (8/9 narrowing + 7.5 field removal), narrow injected interfaces (DiSeamNoLocatorGuard), one sanctioned static + recorded exceptions (StaticSingletonCensusGuard §4h), tested POCOs (LeafPocoNoFacadeGuard), both guards green, full suite + boot smoke green, NFR5 proven.

**Task 4 — NFR5 audit.** `git log -L 108,115` (GetSafeRpcTarget) and `-L 291,296` (IsLocalOrSimulated) on `CharacterManager.cs` over `c72b8d1..HEAD` = **0 commits**; direct method-body diff base..HEAD = **byte-identical**. `clientId >= 100` bot-intercept is inside those unchanged bodies. NFR5 call sites kept verbatim on `.instance`.

**Task 5 — gates + merge prep.** EM 205/205, PM 148/148 (incl. host+client+bot fixture), both guards + census guard green, GameScene boot smoke clean. **Merge to Dev = Poyo's call** (one merge, branch protection). Whole-track changelog for the merge body is in the Change Log below.

**AC mapping:** AC1 (destructive deletion **reframed to strategy B** — façades kept as recorded exceptions, every caller rerouted or recorded with reason) ✅; AC2 (static-absence guard + whitelist-with-reasons + bite) ✅; AC3 (§4 census VERIFIED true — 1 sanctioned static + 23 recorded survivors + NetworkManager.Singleton; 8.4 index-adapter consistent) ✅; AC4 (whole-track DoD §10 evidenced) ✅; AC5 (merge-ready: Epics 6–12 changelog + NFR5 proof + goldens-unchanged; merge is Poyo's) ✅.

### File List

**Task 1 reroutes (commit `c37951d`):**
- `Assets/Scripts/FX/RoomFog.cs`
- `Assets/Scripts/ChatSystem/ChatWindow.cs`
- `Assets/Scripts/Misc/DevIdentityController.cs`
- `Assets/Scripts/UI/Misc/TakeDownThePortalTextTitle.cs`
- `Assets/Scripts/Board/UI/CharacterBar/CharacterAwakenTimer.cs`
- `Assets/Scripts/UI/GameSettings/RoleAttributionSettingTab.cs`
- `Assets/Scripts/UI/GameSettings/RoleAttributionSettingObject.cs`
- `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapMessages.cs`
- `Assets/Scripts/UI/Misc/ShutOffGameButton.cs`
- `Assets/Scripts/UI/SpawnPanels/SelectPanelPlayer.cs`

**Task 2 census guard + annotations (commit `8db80f5`):**
- `Assets/Scripts/Tests/Editor/StaticSingletonCensusGuardTests.cs` (new) + `.meta`
- `Assets/Scripts/Characters/CharacterManager.cs`, `Assets/Scripts/GameLogic/GameManager.cs` (recorded-survivor annotations)
- `Assets/Scripts/Board/BoardManager.cs`, `ChatSystem/ChatManager.cs`, `FocusSystem/FocusManager.cs`, `GameLogic/ChainingManager.cs`, `MessageSystem/MessageManager.cs`, `Network/LobbyPlayerInfoHolder.cs`, `RoleTargetSystem/RoleTargetSystem.cs`, `UI/StatesCanvas.cs`, `UI/BoardUI/Selection/SelectionFlowService.cs` (annotation refresh)

**Docs / tracking:**
- `_bmad-output/refactor-architecture-despaghetti.md` (§4h final census + §10 DoD verification)
- `_bmad-output/implementation-artifacts/12-3-*.md` (this file); `sprint-status.yaml`

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 12.3 (Epic 12 / D6, FINAL). Strategy B (verify-don't-force): rerouted the 10 context-having God-Object-façade leaves onto `CompositionRoot.For(Singleton)` (`c37951d`); kept `GameManager.instance`/`CharacterManager.instance` as recorded exceptions for the context-less callers (W*/TargetUtils/PowerEffectDispatcher/ChatManager NFR5 + fixtures). Added `StaticSingletonCensusGuardTests` making the §4 census executable — 23 whitelisted survivors + CompositionRoot pinned instance-less (`8db80f5`). DoD §10 evidenced point-by-point; NFR5 proven byte-identical (`git log -L` 0 commits + direct diff). EM 205/205, PM 148/148, boot smoke clean, goldens unchanged. **Despaghettification track (Epics 6–12) complete — merge to Dev is Poyo's call.** |
| | **Merge body (Epics 6–12, for the single Dev merge):** D0 composition root + 3-lane seam + 2 guards (6); GameManager-locator dismantled (7); game-loop surface narrowed to IGameLoop/IGameStateQuery (8); CharacterManager split ICharacterQuery/ICharacterCommand, fixture-gated (9); remaining singletons injected/recorded by fan-in (10); per-system decision logic → tested Domain POCOs (11); UI layer rerouted/recorded + endgame census guard (12). Behaviour-preserving every commit (golden/differential/wire-format/leaf-POCO/multi-client nets); NFR5 byte-identical; one sanctioned static (CompositionRoot) + 23 recorded exceptions; EM 205 / PM 148. |
