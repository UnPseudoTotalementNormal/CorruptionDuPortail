# Story 12.1: UI inventory + triage (verify-don't-force, recorded)

Status: review

## Story

As a developer,
I want the 48 UI files triaged — reroute vs recorded façade opt-out,
so that Epic 12 spends churn only where it buys maintainability.

**No code change in this story** — the deliverable is the map for 12.2.

## Acceptance Criteria

1. **Triage table** (file → remaining static/manager dependencies → decision → reason) covering every `UI/` file (48 at recon) PLUS any `Smartphone/`/system-UI file still on a façade after Epics 7-11, recorded in the architecture doc (new §: UI triage).
2. **Explicit criteria applied:** REROUTE when a narrow injected dependency simplifies testing or removes a remaining hub/static; OPT OUT when the file is a pure local-player leaf with no decision logic (verify-don't-force, D-NFR6). Borderline cases get one sentence of justification each.
3. **Effort estimate per reroute** (trivial lane A / needs push / needs interface) so 12.2 can batch by shape.
4. **Census cross-check:** the triage reconciles with the §4 static census (every static a UI file still touches is either dying in 12.2/12.3 or a recorded survivor).

## Tasks / Subtasks

- [x] **Task 1:** Grep inventory of UI-layer residual statics (UI/, ChatSystem/, NoteSystem/, TooltipSystem/, Smartphone/, Board/UI/) post-Epic-11; separated already-migrated (`CompositionRoot.For`) + third-party/opt-out globals from the actionable `// dies 12.3` façade reads.
- [x] **Task 2:** Classified per the criteria (REROUTE scene-lane-A / prefab-push vs OPT-OUT prefab-only / singleton-view / POCO / global-service); borderlines (AnonymousRevealedMessagesComponent, NoteManager) justified.
- [x] **Task 3:** Triage table + 12.2 batch plan + census reconciliation written to architecture doc **§4g**; sprint-status; commit (docs only).

## Dev Notes

- UI is LAST by design (lowest architectural payoff, highest churn). The triage is the brake on perfectionism: an opt-out with a recorded reason is a legitimate END STATE, not a failure.
- Smartphone/ files: content-vs-host pattern means many are views over already-decoupled systems — likely heavy opt-out territory.
- Staleness: "48 files" is 2026-06-11; Epics 7-11 already rerouted UI hub-hops (7.4) and presentation subscribers (8.2) — the triage covers the REMAINDER.

### Project Structure Notes

- Modified: architecture doc only. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Smartphone/ client-only; UI placement three-question test (if the triage finds misplaced logic, RECORD it for Poyo — do not move it; behaviour-preserving).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §8 Epic 12, §6 verify-don't-force] / [epics.md#Story 12.1]

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- No code changed (docs-only story); no test gate. Inventory by grep over the UI layer.

### Completion Notes List

**Triage written to architecture doc §4g** (file → residual façade(s) → decision → effort → reason), covering every UI-layer file still touching a `// dies 12.3` façade post-Epic-11.

**REROUTE (12.2)** — 6 scene **lane-A** consumers clear the last hub reads + Board/Focus survivors: `AnonymeMessageButton`, `InfoTableSystem`, `PowersBar`, `CardPickerManager`, `ShutOffGameButton`, `TooltipLinkParser`; plus prefab **push** reroutes where a base already carries the slice (`MeIconCard`, `AwakeningRecapCorruption`/`VoteStateUI`/`AwakeningRecapMessages` via StateUI/state bases, `SelectPanelPlayer`).

**OPT-OUT / record (verify-don't-force, permanent remainder)** — singleton-views (`ChatPanel`/`ChatWindow`/`ChatNotificationComponent`, `HoverTooltipComponent`/`TooltipWindow`, `PlayerButtonObject`/`ConnectedPlayerPanel`), prefab-only leaves (`TakeDownThePortalTextTitle`, `CharacterAwakenTimer`, `RoleAttributionSettingTab`/`Object` — lane A physically impossible), the `SelectionFlowService` POCO (deps become params when POCO-ised), and the global-service/third-party façades (`MainMenu`/`LobbyListUI`/`LobbySelectionPanel`/`LoginMenu`/`SmartphoneController` → LobbyManager / InputManager / Unity Services).

**Already-migrated (not in scope):** `RobotBoardInfo`, `CharactersBarObject` (resolve via `CompositionRoot.For(nm)`).

**Census reconciliation (AC4):** every façade a UI file touches is either rerouted in 12.2 (so 12.3 deletes the field) or a recorded opt-out — reconciles with §4f's "24 → 1 + recorded exceptions". **Borderlines flagged:** `AnonymousRevealedMessagesComponent` (push vs record — a pure local view of a singleton's list); `NoteManager` (a §4f recorded survivor — kept; only its consumers' CharacterManager reads reroute). No misplaced gameplay logic found in the UI files (project-context three-question test) → nothing to record for Poyo there.

### File List

**Docs only:**
- `_bmad-output/refactor-architecture-despaghetti.md` — new **§4g** UI-layer triage table + 12.2 batch plan + census reconciliation.
- this story; `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 12.1 (Epic 12 / D6, doc-only). Triaged the UI layer post-Epic-11 into the architecture doc §4g: REROUTE (6 scene lane-A consumers clearing the last §4a/§4d/§4f hub+survivor reads, + prefab push reroutes) vs OPT-OUT (singleton-views, prefab-only leaves, the SelectionFlowService POCO, global-service/third-party façades). Reconciled with the §4f static census (every UI-touched façade either dies in 12.2/12.3 or is a recorded opt-out). No code. |
