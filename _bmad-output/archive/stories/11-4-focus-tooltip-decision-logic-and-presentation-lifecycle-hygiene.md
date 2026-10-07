# Story 11.4: Focus + Tooltip + presentation lifecycle hygiene

Status: review

## Story

As a developer,
I want Focus/Tooltip decision logic extracted and the recorded presentation lifecycle leaks fixed deliberately,
so that the presentation layer is clean and the known debt is closed, not forgotten.

## Acceptance Criteria

1. **Focus/Tooltip inventory + extraction:** `FocusManager` / `TooltipSystem` (+`TooltipLinkParser` — already partially pure?) decision logic (focus priority/stack rules, tooltip link resolution) → POCOs + EditMode tests, same regime as 11.1-11.3.
2. **Lifecycle hygiene pass (the deliberate behaviour-adjacent change of the track):** every presentation component recorded as leaking (`OnValueChanged` subscribed, never unsubscribed — `LightManager` noted in 6.1, others collected by 8.2's Task 2 notes) gets its `OnDestroy`/`OnDisable` unsubscribe — as a SEPARATE commit, explicitly labelled lifecycle hygiene, with the suite green before AND after (the unsubscribes must not change any in-game behaviour — they only fix teardown).
3. **Subscription symmetry rule recorded** in the architecture doc (subscribe in X ⇒ unsubscribe in its teardown mirror) — the convention for all future presentation code.
4. **Gated:** suite + fixture unchanged; sprint-status (`epic-11 → done`).

## Tasks / Subtasks

- [x] **Task 1:** Focus/Tooltip inventory — Focus + TooltipManager geometry + `TooltipLinkParser` are engine-coupled (no pure decision); the one extractable rule = the tooltip link-colour wrap → `TooltipLinkFormatter` POCO + 5 EditMode tests. (Commit A.)
- [x] **Task 2:** Leak census — grep `OnValueChanged +=` vs `-=`; presentation leaks (recorded scope): LightManager (6.1), RoomFog, AwakeningLight, AnonymeMessageButton, AwakeningStateUI, BoardCameraManager. Fix table in Dev Agent Record.
- [x] **Task 3:** Hygiene commit (Commit B) — cached-target `OnDestroy`/override unsubscribes only, no other change; suite green before AND after (EM 201/PM 148 unchanged by the unsubscribes — they fire only at teardown).
- [x] **Task 4:** Subscription-symmetry rule recorded in architecture doc §5b; gates; sprint-status (`epic-11 → done`).

## Dev Notes

- WHY the leaks waited until now: fixing them earlier would have mixed a lifetime change into behaviour-preserving reroutes (golden noise risk). Isolated here, labelled, gated = auditable.
- The leaks are mostly benign in-scene (objects live as long as the scene) but bite on scene reload / play-restart with domain reload disabled — that is the test to add if cheap (subscribe-count probe across a simulated re-init).
- `TooltipLinkParser` (2 instance-hits) may already be near-pure — check before extracting (don't reinvent).
- Staleness: leak census depends on 8.2's notes; inventory-driven.

### Project Structure Notes

- New: Focus/Tooltip POCOs + tests. Modified: presentation components (unsubscribes), architecture doc. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- `OnNetworkDespawn` for NetworkVariable unsubscribes (NetworkBehaviours); `OnDestroy` for plain Monos; static-event zombie rule (domain reload disabled).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §8 Epic 11] / [epics.md#Story 11.4]
- [Source: _bmad-output/implementation-artifacts/6-1-*.md Task 1 note] — the recorded LightManager leak (the origin of this story's hygiene scope).
- [Source: Assets/Scripts/FocusSystem/FocusManager.cs + Assets/Scripts/TooltipSystem/].

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0. EM 201/201 (196 + 5 new `TooltipLinkFormatter` tests). PM 148/148 — unchanged by the lifecycle-hygiene unsubscribes (they fire only at `OnDestroy`), satisfying AC2's "suite green before AND after". Domain purity guard green.

### Implementation Plan / Inventory

**Task 1 — Focus/Tooltip decision inventory.** `FocusManager` applies a caller-supplied predicate over engine collections (charactersBar / boardManager.visibleCards) — no pure decision to lift. `TooltipManager` is screen-space geometry (RectTransform / Camera / Canvas / DOTween). `TooltipLinkParser` is an already-cohesive but engine-coupled reflection parser (`CharacterManager.instance` façade, NetworkVariable reflection) — left as-is (Dev Notes: "don't reinvent"). **The one pure decision** = the tooltip link-colour wrap (`description.Replace("<link=", "<color=#…><link=").Replace("</link>", "</link></color>")`) → extracted to `TooltipLinkFormatter.WrapLinksWithColor`.

**Task 2 — leak census** (`OnValueChanged +=` without a matching `-=`, presentation scope):

| Component | Subscriptions (no prior unsubscribe) | Teardown added |
|---|---|---|
| `LightManager` (the 6.1 origin) | `gameManager.currentGameStateIndex` (Start) | new `OnDestroy` |
| `BoardCameraManager` | `gameManager.currentGameStateIndex` (Start) | appended to existing `OnDestroy` |
| `AwakeningLight` | `Loop.onGameStarted` (Start) + local char `isAwakened` (OnGameStarted) | new `OnDestroy` + cached char |
| `RoomFog` | `GameManager.instance.onGameStarted` + local char `isAwakened` + `currentGameStateIndex` | new `OnDestroy` + cached GM & char |
| `AnonymeMessageButton` | `onGameStarted` + `currentGameStateIndex` + local char `hasSentMessageThisTurn` + `messageLeft` | new `OnDestroy` (CustomButton has none) + cached GM & char |
| `AwakeningStateUI` | `Loop.onGameStarted` (SetupStateUI) + local char `isAwakened` (OnGameStarted) | **override** `OnDestroy` (calls `base`) + cached char |

Symmetric/safe (NOT touched): `CharactersBarObject`, `CorruptedCardText`, `MessageLeftText`, `Card`, `AwakeningState`, `Character` (own NV). **Out of scope** (gameplay, not presentation — recorded for a later sweep): `PLegacy`, `PCPowerUnlockWhenChain`, `PersonalBeaconObject` (`isChained`).

### Completion Notes List

**Commit A — Focus/Tooltip extraction.** `TooltipLinkFormatter` POCO (zero engine types) + 5 EditMode tests (single link, multiple links, no-link passthrough, empty, hex honoured). `TooltipManager` delegates the link-colour wrap; the hardcoded `#6fb5d1` becomes a named adapter const. Behaviour-identical (same `Replace` chain, including the original NRE-on-null contract).

**Commit B — presentation lifecycle hygiene (the deliberate behaviour-adjacent change, isolated + labelled).** Six recorded presentation leaks closed: each subscribe now has a teardown-mirror unsubscribe. **Cached-target rule applied** — where the subscription targets another object resolved at runtime (`GetLocalCharacter(false).isAwakened`, `GameManager.instance.currentGameStateIndex`), the resolved object is stored in a field so the unsubscribe hits the same instance; every deref is null-guarded (`if (cachedTarget != null)`). `AwakeningStateUI` overrides the `StateUI.OnDestroy` (calls `base` first) since these UIs are locally `Instantiate`d, not network-spawned, so `OnNetworkDespawn` is unreliable — consistent with the base's own teardown choice. **The unsubscribes change no in-game behaviour** (they only run at `OnDestroy`), so the full suite is green both before and after — AC2 satisfied. The optional subscribe-count re-init probe (Dev Notes "if cheap") was NOT added — a faithful scene-reload/domain-reload-disabled re-init harness is non-trivial; the fix is obviously-symmetric and the suite-green-before/after gate covers regression. Recorded for a future cheap probe if desired.

**AC3 — convention recorded** in `refactor-architecture-despaghetti.md §5b` (subscription symmetry: subscribe ⇒ unsubscribe in the teardown mirror; cache the exact target; guard the deref; why it waited until 11.4).

### File List

**New (production):**
- `Assets/Scripts/Domain/TooltipLinkFormatter.cs` — link-colour-wrap POCO (zero engine types).

**New (tests):**
- `Assets/Scripts/Tests/Editor/TooltipLinkFormatterTests.cs` — 5 EditMode tests.

**Modified (production) — Commit A:**
- `Assets/Scripts/TooltipSystem/TooltipManager.cs` — `_linkFormatter` + `LINK_COLOR_HEX`; description link-wrap delegates to the POCO.

**Modified (production) — Commit B (lifecycle hygiene):**
- `Assets/Scripts/Board/LightManager.cs`, `Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs`, `Assets/Scripts/FX/AwakeningLight.cs`, `Assets/Scripts/FX/RoomFog.cs`, `Assets/Scripts/UI/BoardUI/AnonymeMessageButton.cs`, `Assets/Scripts/UI/StateUI/AwakeningStateUI.cs` — cached-target `OnValueChanged`/event unsubscribes in `OnDestroy`.

**Docs:** `_bmad-output/refactor-architecture-despaghetti.md` (§5b subscription-symmetry rule); this story; `sprint-status.yaml` (11-4 → review, epic-11 → done).

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 11.4 (Epic 11 / D5, last story). **Commit A:** extracted the tooltip link-colour wrap → pure `TooltipLinkFormatter` POCO + 5 EditMode tests (the only pure Focus/Tooltip decision; the rest is engine-coupled presentation, recorded). **Commit B (lifecycle hygiene, isolated + labelled):** closed 6 recorded presentation `OnValueChanged` leaks (LightManager [6.1 origin], RoomFog, AwakeningLight, AnonymeMessageButton, AwakeningStateUI, BoardCameraManager) with cached-target, null-guarded teardown-mirror unsubscribes — no in-game behaviour change (fire only at OnDestroy), suite green before+after. Recorded the subscription-symmetry convention in architecture doc §5b. Compile 0; EM 201/201 (196 + 5); PM 148/148; Domain purity green. **Epic 11 (D5) complete → epic-11 done.** |
