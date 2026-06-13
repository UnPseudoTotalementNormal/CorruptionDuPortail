# Story 1.5: Characterization golden masters for `VoteTally` and `ChainingResolver`

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want `VoteTally` and `ChainingResolver` current behavior pinned as golden masters before Epic 2 extracts them,
so that those Wave 1 extractions are behavior-preserving by proof, not by hope — especially `ChainingResolver`, whose output several winning conditions depend on.

## Acceptance Criteria

**Given** the second party-mode review flagged that Epic 2 would otherwise extract `VoteTally` (FR5) and `ChainingResolver` (FR6) with **no characterization tests** — the Test Architect rated `ChainingResolver` the single highest-risk extraction of the whole refactor (zero existing coverage, ordering-sensitive, upstream of multiple conditions)
**When** this story lands (alongside the WinningCondition goldens of Story 1.2, on the faithful harness of Story 1.0)
**Then** `VoteTally` current vote-count → outcome verdicts are pinned as golden masters over a boundary corpus (tie, unanimous, abstention, single voter, zero voters), tagging vacuous cases `[Category("VacuousTruth")]`
**And** `ChainingResolver` is golden-mastered over a corpus that **varies the input order**, not just the input sets — ordering bugs are the dominant failure mode and single-shot goldens miss them
**And** a property-style note records which input permutations *must* be indifferent and which *must* matter, so Epic 2's extraction has an explicit ordering contract to preserve
**And** these goldens become the oracle that lets Epic 2 keep the `ChainingResolver`-vs-live differential running until conditions 2.3–2.6 are all green

### Delivery plan (decided 2026-06-10 — split into two PRs)

This story is delivered in **two separate branches/PRs to `dev-refactor`**, both required before the story is `done`:
- **PR A — `ChainingResolver` goldens** (tractable: a clean host harness already exists in `ChainingManagerTests`).
- **PR B — `VoteTally` goldens** (heavier: the tally is tangled inside `VoteState.OnEndStateServer` with a coroutine + RPCs; needs the `GameManager`/`CharacterManager` host harness from `VictoryConditionTests`).

Mark `1-5` → `review`/`done` only after **both** PRs merge. Keep one story file; record each PR in the Change Log.

### Acceptance reading notes (binding)

1. **Pure characterization on CURRENT code. Test-only. Zero production change.** Same discipline as Story 1.2: drive the existing logic on the live host, pin its current verdict as an `Assert`, do NOT "fix" surprising behavior. Do not touch `VoteState.cs`, `VoteRecapState.cs`, `ChainingManager.cs`, or any production file. Do not create the Epic 2 POCOs (`VoteTally`/`ChainingResolver` extraction is Epic 2 §2.9/§2.10).

2. **What "VoteTally" actually IS today** — the vote-count → outcome block in `VoteState.OnEndStateServer` (`VoteState.cs:186–198`):
   ```
   var ordered = votesForPlayer.OrderByDescending(v => v.Value.Count).ToList();
   int numTopTied = ordered.Count(v => v.Value.Count == ordered.First().Value.Count);
   if (numTopTied == 1 && ordered.First().Key != SKIP_VOTE_ID) {
       mostVotedPlayer = winner; ChainingManager.instance.AddCharacterToChainingList(winner);
   } else {
       mostVotedPlayer = SKIP_VOTE_ID;   // tie OR top is SKIP
   }
   ```
   The observable outputs to pin: (a) `VoteState.mostVotedPlayer` (a `static ulong`), and (b) whether `ChainingManager.instance.chainingPlayers` gained the winner. Boundary corpus → current verdict:
   - **unanimous / clear single winner** (one player strictly most-voted) → `mostVotedPlayer == winnerId`, winner added to chaining list.
   - **tie for the top** (≥2 players share the max count) → `mostVotedPlayer == SKIP_VOTE_ID`, nobody added.
   - **abstention wins** (`SKIP_VOTE_ID` bucket has the strict max) → `mostVotedPlayer == SKIP_VOTE_ID`, nobody added (the `ordered.First().Key != SKIP_VOTE_ID` guard).
   - **single voter** (exactly one vote cast for one player) → that player wins (count 1 > all others' 0), added.
   - **zero voters** (all buckets count 0) → every bucket ties at 0 → `numTopTied > 1` → `SKIP_VOTE_ID`, nobody added. Tag `[Category("VacuousTruth")]`.
   - **A note on the ordering trap:** `OrderByDescending` + `.First()` makes the *identity* of `ordered.First()` enumeration-order-dependent **only in the tie case** — but the tie case always routes to `SKIP_VOTE_ID` regardless of which tied entry is first, so the **outcome** is order-independent. Pin this as a property note (it is the reassuring half of the ordering contract). The single-winner case has a unique max, so `.First()` is unambiguous.

3. **What "ChainingResolver" actually IS today** — `ChainingManager.chainingPlayers` membership management, primarily `AddCharacterToChainingList` (`ChainingManager.cs:38–50`): server-only, **dedup by `Contains`**. This is the resolution several winning conditions depend on (the chained set ultimately drives `Character.isChained`, read by `WMarginal`/`WChosen`/`WOmniscience`). `ChainCharacterRpc` (the `isChained` write + portal-power side effect) is **adapter/side-effect territory** — Epic 2 keeps it in `Game`; it is **out of scope** for this characterization, which pins the *membership resolution*, not the NGO write. Existing coverage: `ChainingManagerTests` already pins single-add + dedup (untagged) — extend, do not duplicate.

4. **The ordering corpus + property note (the heart of the ChainingResolver AC):**
   - Vary input order: add `[A,B,C]` vs `[C,B,A]` vs `[B,A,C]` → assert the resulting `chainingPlayers` **membership set** is identical across permutations (order-INDIFFERENT for membership — this MUST hold).
   - Dedup under interleaving: `[A,B,A,C,B]` → membership `{A,B,C}`, count 3.
   - Pin the *sequence*: `chainingPlayers` preserves **insertion order** of first occurrence (`[A,B,C]` stays `A,B,C`). Record in the property note that **insertion-order of the list is currently observable but NO winning condition reads list position** — they read `Character.isChained` per character — so Epic 2 may treat membership as a set, but if it changes list ordering it must prove no consumer depends on it. This is the "which permutations must be indifferent vs must matter" deliverable (AC line 184).
   - Empty resolution (no adds) → empty `chainingPlayers`, tag `[Category("VacuousTruth")]`.

5. **Harnesses to reuse — do NOT invent new ones:**
   - **ChainingResolver (PR A):** reuse the `ChainingManagerTests` `SetUp`/`TearDown` verbatim (minimal `NetworkManager` + `UnityTransport` + `StartHost()`, `ChainingManager` with `NetworkObject` + `Spawn()`, `NetworkTestHelper.WaitUntilOrTimeout`). It already proves this path drives production membership logic.
   - **VoteTally (PR B):** reuse the Story 1.0/1.2 `VictoryConditionTests` host harness (NetworkManager + `GameManager` with `NetworkObject`+`Spawn`, `ignoreGameLoop=true` + `DummyGameState`, `CharacterManager` with reflection-injected `_charactersParent`/`_characterPrefab`, `WaitUntilAllSpawnedOrTimeout`, static `instance` reset in `TearDown`). The tally needs real `Character`s (so `GetCharacters()` and `CanVote` resolve) and `ChainingManager.instance` spawned (so the winner-add path runs).

6. **VoteTally isolation hazard (PR B) — read before implementing:** `OnEndStateServer` calls `StopCoroutine(updateVoteTimerCoroutine)` (`VoteState.cs:184`); if you call `OnEndStateServer` without a prior `OnStartStateServer`, `updateVoteTimerCoroutine` is `null`. It also fires `DoStateMethodRpc`/`AskForUpdateAllCharactersRpc`. **Recommended approach:** drive the *real* vote path on the host — populate `votesForPlayer` via the production entry points (`OnStartStateServer` to seed buckets, then `OnVoteSkipButtonPressed`/the voted RPC to cast votes), then invoke `OnEndStateServer` and assert `mostVotedPlayer` + chaining membership. If a clean call to `OnEndStateServer` proves intractable after reasonable attempts, **HALT and report** with the exact failure — do not characterize a reimplementation of the tally (that would void the golden). The tally must be the *real* code path.

7. **New categories are additive and independently filterable.** Tag every golden `[Category("GoldenMaster")]`; vacuous cases additionally `[Category("VacuousTruth")]` (NUnit allows multiple). Optionally add a sub-tag (`[Category("VoteTally")]` / `[Category("ChainingResolver")]`) so each half is independently runnable. These join `DomainPurity`/`DomainSnapshot`/`HarnessFidelity`/`Determinism`.

## Tasks / Subtasks

### PR A — ChainingResolver goldens (branch `story/1-5-chaining`)

- [ ] **A0 — Baseline green** (AC: "Given"): `run_tests category: DomainPurity,DomainSnapshot` (EditMode) + `HarnessFidelity,GoldenMaster,Determinism` (PlayMode) → green
- [ ] **A1 — Author `ChainingResolverGoldenMasterTests`** reusing the `ChainingManagerTests` harness, `[Category("GoldenMaster")]` + `[Category("ChainingResolver")]`:
  - [ ] Order-indifference: `[A,B,C]` vs `[C,B,A]` vs `[B,A,C]` → identical membership set
  - [ ] Dedup under interleaving: `[A,B,A,C,B]` → `{A,B,C}`, count 3
  - [ ] Insertion-order sequence pinned: `[A,B,C]` → list reads `A,B,C`
  - [ ] Empty resolution → empty list, `[Category("VacuousTruth")]`
  - [ ] Single add → membership 1 (may reference existing `ChainingManagerTests` rather than duplicate)
- [ ] **A2 — Property note** (AC line 184): a header comment block in the test file recording: membership is order-INDIFFERENT (must hold); list insertion-order is currently observable but read by NO winning condition (Epic 2 may set-ify membership but must prove no consumer reads list position).
- [ ] **A3 — Run + prove**: `run_tests category: ChainingResolver` (PlayMode) green; full regression (`HarnessFidelity,GoldenMaster,Determinism`) still green; `read_console` clean
- [ ] **A4 — Commit, PR → dev-refactor, squash-merge**

### PR B — VoteTally goldens (branch `story/1-5-vote`, after PR A merges)

- [ ] **B0 — Baseline green** (incl. the just-merged ChainingResolver goldens)
- [ ] **B1 — Author `VoteTallyGoldenMasterTests`** reusing the `VictoryConditionTests` host harness, `[Category("GoldenMaster")]` + `[Category("VoteTally")]`, driving the REAL vote path (note 6):
  - [ ] unanimous / clear single winner → `mostVotedPlayer == winnerId`, winner in chaining list
  - [ ] tie for top → `SKIP_VOTE_ID`, nobody added
  - [ ] abstention wins → `SKIP_VOTE_ID`, nobody added
  - [ ] single voter → that player wins, added
  - [ ] zero voters → `SKIP_VOTE_ID`, nobody added, `[Category("VacuousTruth")]`
- [ ] **B2 — Ordering property note**: record that the tie case routes to `SKIP_VOTE_ID` regardless of `OrderByDescending().First()` identity → outcome is order-independent despite the order-dependent intermediate.
- [ ] **B3 — Run + prove**: `run_tests category: VoteTally` (PlayMode) green; full regression green; console clean
- [ ] **B4 — Commit, PR → dev-refactor, squash-merge**; then mark story `done`

## Dev Notes

### What this story touches (and what it must NOT)

**Created:**
- `Assets/Scripts/Tests/PlayMode/ChainingResolverGoldenMasterTests.cs` (PR A)
- `Assets/Scripts/Tests/PlayMode/VoteTallyGoldenMasterTests.cs` (PR B)

**Must NOT change:** `VoteState.cs`, `VoteRecapState.cs`, `ChainingManager.cs`, any production file, any asmdef. Pure test addition.

### Current logic provenance

| Unit | Live source | Observable to pin |
|---|---|---|
| VoteTally | `VoteState.OnEndStateServer` (`VoteState.cs:186–198`) | `VoteState.mostVotedPlayer` (static), `ChainingManager.chainingPlayers` membership |
| ChainingResolver | `ChainingManager.AddCharacterToChainingList` (`ChainingManager.cs:38–50`) | `chainingPlayers` membership + insertion-order sequence; dedup |

`SKIP_VOTE_ID == GameValues.FAKE_CLIENT_ID` (`VoteState.cs:34`).

### Testing standards summary

- Both halves are **PlayMode** (need the NGO host). Reuse the two existing harnesses verbatim (note 5) — a new harness would void the Story 1.0 fidelity proof.
- Goldens encode the CURRENT verdict as an `Assert`; capture surprising behavior as-is.
- Categories additive/independently runnable: `GoldenMaster` (+ `VoteTally`/`ChainingResolver` sub-tags, + `VacuousTruth` on vacuous cases).
- Poll `read_console` after each test addition; run the relevant category + full regression before marking a PR done.

### Project Context Rules

- `GetSafeRpcTarget` / `IsLocalOrSimulated` are not exercised (no new RPCs; tests drive server-side logic on the host). [project-context.md critical patterns]
- Tests live in `Tests.PlayMode` (already references `Game` + now `CorruptionDuPortail.Domain`). No asmdef edits needed.
- Server-authority: the tally + chaining membership are server-only (`IsServer` guards); the host harness runs as server. Correct.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.5] (lines 172–185) — ACs verbatim
- [Source: Assets/Scripts/GameLogic/GameStates/VoteState.cs:34,186–198] — tally logic + `SKIP_VOTE_ID`
- [Source: Assets/Scripts/GameLogic/ChainingManager.cs:38–50] — `AddCharacterToChainingList` dedup
- [Source: Assets/Scripts/Tests/PlayMode/ChainingManagerTests.cs] — reusable ChainingManager host harness (PR A) + existing single-add/dedup coverage
- [Source: Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs] — reusable GameManager/CharacterManager host harness (PR B), per Story 1.2 reading-note 5

### Previous story intelligence

- Story 1.2 established the `GoldenMaster`/`VacuousTruth` category discipline and the "capture current verdict as-is, do not fix" rule — apply identically here.
- Story 1.4 added `CorruptionDuPortail.Domain` to `Tests.PlayMode.asmdef`; these PlayMode tests compile against it already. No asmdef work.
- `manage_asset move` MCP may report an error while actually succeeding (Story 1.3) — irrelevant here (no file moves), but `git mv`/`Write` are the reliable paths.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- VoteTally drive: benign `No script asset for DummyGameState` warnings (nested test SO, identical to `VictoryConditionTests`). The real path (OnStartStateServer → reflection `OnPlayerVotedRpc` → OnEndStateServer) ran clean — RPCs resolved once `VoteState` was registered in `gameManager.gameStates`.
- `NetworkList<ulong>` has no LINQ `ToList` (CS1061) — iterate with `foreach` to read membership.

### Completion Notes List

- **PR A (#25):** `ChainingResolverGoldenMasterTests` — 6 goldens pinning membership order-indifference, dedup, insertion-order sequence, empty (VacuousTruth). Reused `ChainingManagerTests` harness.
- **PR B (#26):** `VoteTallyGoldenMasterTests` — 5 goldens driving the real vote path, pinning `mostVotedPlayer` + chaining membership across unanimous/tie/abstention/single/zero. Reused `VictoryConditionTests` harness + spawned `ChainingManager` + registered `VoteState`.
- Both halves test-only, zero production change. Categories `ChainingResolver` / `VoteTally` (+ `GoldenMaster`, `VacuousTruth`) independently runnable.
- Full regression after each: 37/37 (PR A), 42/42 (PR B). Completes Epic 1.

### File List

- **Added:** `Assets/Scripts/Tests/PlayMode/ChainingResolverGoldenMasterTests.cs` (PR A)
- **Added:** `Assets/Scripts/Tests/PlayMode/VoteTallyGoldenMasterTests.cs` (PR B)

### Change Log

- 2026-06-10 — PR A (#25): ChainingResolver membership goldens. PR B (#26): VoteTally outcome goldens (real vote path). Story 1.5 → done; Epic 1 (Foundations) → done.
