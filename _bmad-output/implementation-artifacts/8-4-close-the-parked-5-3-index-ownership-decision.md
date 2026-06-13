# Story 8.4: Close the parked 5.3/5.4 index-ownership decision

Status: review

## Story

As a developer,
I want the parked `GameLoopMachine` index-ownership move (stories 5.3/5.4) explicitly decided — fold in or close — now that the surface is narrow,
so that the highest-risk parked work stops being an open question.

**This is a decision story.** Primary deliverable = a recorded decision; code only if the decision is "execute".

## Acceptance Criteria

1. **Decision inputs assembled:** with Epics 6-8.3 landed — (a) how many consumers still touch `currentGameStateIndex` directly vs through `IGameStateQuery`; (b) what 5.3 would still buy (POCO owns index; adapter mirrors) now that consumers see only the slice; (c) the unchanged risk profile (silent multiplayer desync — host advances, client frozen, no exception; rated highest-risk of the whole refactor).
2. **Decision recorded** in `refactor-architecture-despaghetti.md` (new short section) AND `sprint-status.yaml` comments: either **close as won't-do** (reason: e.g. "surface narrowed; ownership move buys purity only; risk unjustified — POYO's standing position") or **execute**.
3. **If execute:** the original 5.3 ACs apply IN FULL (synchronous mirror — no suspension point between machine-settle and NV write; 2.11a golden passes UNCHANGED not adapted; parameterized client-trace suite on `MultiClientGameFixture` covering normal/skip/late-join/terminal transitions; remote client sees exactly one `OnValueChanged` per transition as an ordered trace; atomic commit; NFR5 untouched) + 5.4 (façade removal) + `# REVIEW-REQUIRED` gds-code-review. Sprint-status: un-park 5-3/5-4 or mirror them as 8-4 sub-items — keep ONE source of truth, record which.
4. **If close:** sprint-status `5-3`/`5-4` → a terminal status with the won't-do note; no dangling "blocked".
5. Either way: epic-8 closes with the full suite green at baseline.

## Tasks / Subtasks

- [x] **Task 1:** Assembled decision inputs (grep of `currentGameStateIndex` consumers + the 8.3 census + the parked-5.3/5.4 framing). Findings in Completion Notes.
- [x] **Task 2:** Presented the trade-off to Poyo via AskUserQuestion (close vs execute, with the recommendation to close). Poyo: "Fais selon ton meilleur jugement" → delegated the call.
- [x] **Task 3:** Recorded per AC2/AC4 — decision = **CLOSE as won't-do**. Written to `refactor-architecture-despaghetti.md` §8a (+ §10 DoD permanent-exception note) and `sprint-status.yaml` (5-3/5-4 → `wont-do` with notes). No code (close path → AC3 execute branch not taken).
- [x] **Task 4:** Suite green at baseline (EditMode 162/162; no code touched → PlayMode unchanged at 146/146); sprint-status updated (8-4 → review; epic-8 ready to close).

## Dev Notes

- History: 5.3/5.4 PARKED by Poyo (2026-06-11) — "pure-archi, highest-risk silent-desync, no gameplay gain; folds into Epic 8 (D2) IF it still earns its risk". The default lean is CLOSE unless the D2 narrowing changed the calculus — but it is Poyo's call (Task 2 is mandatory).
- If closed: the index façade (the adapter owning the NV while `GameLoopMachine` computes) is then PERMANENT by design — update the leaf-guard / NFR7 wording accordingly so the DoD ("no strangler façade") doesn't contradict the recorded decision (12.3 checks this).

### Project Structure Notes

- Modified: architecture doc (decision section), sprint-status; code only if execute. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Ask the user when destructive/irreversible — the decision itself is the user-gate here.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.3 / 5.4 (original ACs) + #Story 8.4]
- [Source: _bmad-output/refactor-architecture-despaghetti.md §8 Epic 8] / sprint-status comments on 5-3/5-4.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Doc-only decision story (close path). No production code changed. Suite at baseline: EditMode 162/162; PlayMode unchanged at 146/146 (no `.cs` touched).

### Completion Notes List

**Decision: CLOSE 5.3 / 5.4 as won't-do.** Poyo delegated the call ("fais selon ton meilleur jugement"); the recommendation was CLOSE, and the inputs support it.

**Task 1 — decision inputs:**
- `currentGameStateIndex` (the `NetworkVariable<int>`) is **written only by GameManager** (server, `SwitchGameState` + index-0 reset). No external writer.
- External READ consumers after D2: **3 via the slice** `IGameStateQuery.currentGameStateIndex` (LightManager 8.2, BoardCameraManager 8.2, PowerManager 8.3); **2 via the locator** (RoomFog, AnonymeMessageButton → Epic 9); **2 via an injected concrete `gameManager`** (GameState.`IsStateActive` internal; GameSnapshotBuilder off a passed param — already no-action).
- The transition **arithmetic is already POCO** (`GameLoopMachine.Advance/Rewind`, story 2.11b, EditMode-tested). What's left in the adapter is the NV ownership + `OnValueChanged` plumbing + the `OnEnd → NV write → OnStart` ordering (the 2.11a sequence golden).
- **What 5.3 would buy now:** architectural purity only (POCO owns the canonical index, NV mirrors). Consumers already depend on the slice → indifferent to who owns the index. No consumer change, no new testability.
- **Risk (unchanged, #1 of the refactor):** a synchronous mirror with no suspension point between machine-settle and the NV write; a stray suspension → host advances, client frozen, no exception (silent multiplayer desync). Touches the load-bearing 2.11a ordering.
- **Calculus:** purity-only payoff vs re-opening the highest-risk surface + REVIEW-REQUIRED + a multi-client trace suite, for zero gameplay gain. The D2 narrowing LOWERED the payoff, so the parked "fold in IF it earns its risk" condition is not met → close.

**Task 3 — recorded (AC2/AC4):**
- `refactor-architecture-despaghetti.md` **§8a** (new): the decision + inputs + rationale, AND that the `GameManager` index-mirror (owns the `currentGameStateIndex` NV + the `OnEnd → NV write → OnStart` sequencing while `GameLoopMachine` computes the arithmetic) is a **PERMANENT-by-design network-adapter boundary**, NOT a strangler façade. §10 DoD updated with the same recorded exception so story 12.3's "no façade remains" check does not contradict the decision.
- `sprint-status.yaml`: `5-3` / `5-4` → **`wont-do`** (terminal, not `blocked`) with the rationale; `8-4` → `review`.
- The 5.2 `LeafPocoNoFacadeGuard` needs no change: it covers the Wave 1–3 leaf cores, not `GameLoopMachine` (which already satisfies its spirit — a plain POCO, no static accessor, no static mutable state; the NV lives in `GameManager`).

**Task 4 — gate:** EditMode 162/162 (both DI guards + LeafPocoGuard + goldens green); no code change → PlayMode unchanged at 146/146 baseline. Epic 8 ready to close (8-1/8-2/8-3 done, 8-4 review).

### File List

**Modified (docs only — no production code):**
- `_bmad-output/refactor-architecture-despaghetti.md` — new §8a decision section (close 5.3/5.4; permanent index-adapter exception) + §10 DoD exception note.
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — `5-3`/`5-4` → `wont-do` (terminal) with rationale; `8-4` → `review`.
- `_bmad-output/implementation-artifacts/8-4-*.md` (this story).

### Change Log

- 2026-06-12 — Story 8.4 (decision story) CLOSED the parked 5.3/5.4 `GameLoopMachine` index-ownership move as **won't-do**: the D2 narrowing reduced the payoff to architectural purity only (consumers already see `IGameStateQuery.currentGameStateIndex`; arithmetic already POCO in 2.11b) while the #1 silent-desync risk is unchanged and gameplay gain is zero. The `GameManager` index-mirror adapter is recorded as a permanent network-adapter boundary (§8a + §10 DoD exception), not a strangler façade. `5-3`/`5-4` → `wont-do`; no code changed; suite green at baseline. Status → review. Epic 8 ready to close.
