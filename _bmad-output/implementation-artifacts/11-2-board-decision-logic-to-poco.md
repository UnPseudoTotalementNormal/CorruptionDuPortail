# Story 11.2: Board — decision logic to POCO

Status: review

## Story

As a developer,
I want board/despawn decision logic extracted into tested POCOs,
so that board rules are EditMode-testable.

## Acceptance Criteria

1. **Inventory first:** `BoardManager` (257 LOC) + board components pass: decision (card placement/ordering rules, despawn eligibility, board-state arithmetic) vs glue (NGO spawn/despawn calls, DOTween, scene refs). Inventory pasted into Dev Agent Record.
2. **Characterize-then-extract** per candidate: golden on current behaviour (EditMode where possible, PlayMode if NGO-coupled), then POCO into `Domain` (or Game-POCO, recorded), adapter applies decisions (NFR4).
3. **Despawn authority untouched:** POCO decides WHAT despawns; the adapter's server-side `Despawn(destroy:true)` call is the only executor (NFR5-adjacent — no authority move).
4. **EditMode tests shipped per core**; suite + fixture unchanged; sprint-status.

## Tasks / Subtasks

- [x] **Task 1:** Inventory of `BoardManager` (257 LOC) + board components — candidate list in Dev Agent Record. One clean pure-decision candidate found; rest = NGO/DOTween/UI glue.
- [x] **Task 2:** Extracted `BoardManager.GetCardPlacedPosition` (card grid-wrap layout arithmetic) → `CardLayout` Domain POCO; adapter wraps the plain-float result into a Vector3.
- [x] **Task 3:** 7 EditMode tests for `CardLayout` (origin / advance / wrap-at-`>=`-maxX boundary / multi-line / non-positive index / y-invariant); Domain purity green; gates (compile 0, EM 186/186 = 179+7, PM 148/148); sprint-status.

## Dev Notes

- Board is tween-heavy: remember "gameplay state is never the result of a tween" (project-context) — if the inventory finds state derived from tween completion, that is a FINDING to surface (pre-existing fragility), not something to silently fix (behaviour-preserving) — record for Poyo.
- `CancellableTaskHandler` patterns in `Board/CardComponents/` are async glue, not decisions — stay.
- Staleness: inventory-driven; BoardManager reshaped by 10.3.

### Project Structure Notes

- New: Domain POCOs + tests. Modified: board files (thinning). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- UniTask only; DOTween SetLink/Kill hygiene untouched; pooling rules; EditMode-first.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.4, §8 Epic 11] / [epics.md#Story 11.2]
- [Source: Assets/Scripts/Board/BoardManager.cs + Board/CardComponents/].

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0. EM 186/186 (179 + 7 new `CardLayout` tests, created via MCP `create_script`). PM 148/148 (board behaviour preserved — `GetCardPlacedPosition` feeds `PlaceAllCardsToPosition`; placement unchanged). Domain purity guard green.

### Implementation Plan / Inventory (Task 1)

**Decision-vs-glue pass over `BoardManager` (257 LOC) + `Board/` components:**

| Site | Classification | Action |
|---|---|---|
| `BoardManager.GetCardPlacedPosition` (grid layout: advance x by `CARD_SPACING`, wrap to a new line at/over `maxCardPosition.x`, decrement z by `CARD_LINE_SPACING`) | **DECISION — pure layout arithmetic over plain floats** | **EXTRACTED → `CardLayout` Domain POCO** (+ `CardPlacement` value struct); 7 EditMode tests. |
| `hasAllCardsShown` (`visibleCards.Count == GetCharacters().Count(!isFake)`) | Thin engine predicate over live collections | STAYS — one-liner, no logic worth a POCO. |
| `ShowAllPlayerCards` `!isFake` spawn filter + owned-card-to-front reorder (`ChangeIndex(IndexOf(ownedCard), 0)`) | Trivial decisions entangled with engine collections + the card spawn flow | STAYS (glue). |
| `PlaceAllCardsToPosition` / `ShowAllPlayerCards` / `HideAllCards` (DOTween `DOLocalMove*`, `UniTask.Delay`, `CancellationTokenSource`) | Animation / async glue (Dev Notes: `CancellableTaskHandler` patterns stay) | STAYS. |
| `AddNewCard` / `DestroyCard` (`Instantiate`/`Destroy`, event wiring, `Card.Initialize` lane-B push) | NGO/lifecycle glue. **AC3:** cards are LOCAL `Instantiate`/`Destroy` (not `NetworkObject.Despawn`, per the 10.3 census) — no despawn-authority logic to touch | STAYS. |
| `UpdateCards` (match card↔character by ownerClientId, `SetInfo`) | Glue (iterate + mutate engine objects) | STAYS. |
| All `Board/CardComponents/*`, `CardEffects/*`, `UI/*` | Visual / tween / effect-dispatch / UI glue | STAYS (out of this story's decision scope). |

**Dev-Notes tween-fragility check (explicit):** scanned for gameplay state derived from tween completion. **None found** — `BoardManager`'s tweens are pure visual movement/rotation; the only board state (`visibleCards`) is mutated imperatively (in `AddNewCard`/`DestroyCard`), never read off a tween's completion. No FINDING to surface to Poyo.

### Completion Notes List

**Extraction (Task 2) — `GetCardPlacedPosition` → `CardLayout` (Domain).** The grid-wrap layout is now a pure POCO over plain floats (origin x/y/z, spacing, line-spacing, maxX → `CardPlacement`), so the wrapping rule (including the exact `>=`-maxX boundary and the y-invariant) is EditMode-testable without a scene. `Vector3` is a UnityEngine type, so it cannot enter the purity-guarded `Domain`; the adapter reads `spawnCardPosition.localPosition` / `maxCardPosition.localPosition` and the `CARD_SPACING`/`CARD_LINE_SPACING` consts, calls the POCO, and wraps the result back into a `Vector3` — behaviour-identical (same loop, same order: advance-then-wrap).

**Tests (Task 3) — 7 EditMode cases.** Origin (index 0); within-line advance; overflow wrap to next line; the exact `>=`-maxX boundary (index lands x == maxX ⇒ wraps, proving `>=` not `>`); multi-line wrapping; non-positive index → origin; non-zero origin honoured with y held constant across a wrap.

**NFR compliance.** NFR4: POCO returns coordinates, adapter moves the card. AC3 despawn authority: untouched (cards are local objects; no server `Despawn`). Domain purity green.

### File List

**New (production):**
- `Assets/Scripts/Domain/CardLayout.cs` — `CardLayout` POCO + `CardPlacement` value struct (zero engine types).

**New (tests):**
- `Assets/Scripts/Tests/Editor/CardLayoutTests.cs` — 7 EditMode tests (`[Category("CardLayout")]`).

**Modified (production):**
- `Assets/Scripts/Board/BoardManager.cs` — `_cardLayout` field; `GetCardPlacedPosition` rebuilt to delegate to the POCO and wrap the result in a `Vector3`.

**Docs:** this story; `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 11.2 (Epic 11 / D5). Extracted `BoardManager.GetCardPlacedPosition` (card grid-wrap layout arithmetic) into a pure `CardLayout` Domain POCO (+ `CardPlacement`); the adapter reads scene Transforms/consts and wraps the plain-float result into a `Vector3`. 7 EditMode tests (incl. the `>=`-maxX boundary + y-invariant). Inventory recorded the rest as NGO/DOTween/UI glue; explicit tween-derived-gameplay-state check came back clean (no FINDING). NFR4 + AC3 (no despawn-authority move; cards are local) honoured. Compile 0; EM 186/186 (179 + 7); PM 148/148; Domain purity guard green. |
