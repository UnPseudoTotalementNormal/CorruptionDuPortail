# Story 2.9: Extract `VoteTally` + migrate its tests to EditMode

Status: done

## Story

As a developer (Poyo),
I want vote counting extracted as a POCO `VoteTally`,
so that vote-outcome logic is fast EditMode-testable.

## Acceptance Criteria

**Given** the `VoteTally` goldens of Story 1.5 and the pinned vote-insertion order (Story 1.1, §3b C)
**When** `VoteTally` is extracted into `Domain`
**Then** it reproduces the golden vote-count → outcome verdicts bit-for-bit, respecting the pinned insertion order as its contract
**And** it returns a decision only (NFR4); no transition, no RPC
**And** its tests run as EditMode in this same commit

### Acceptance reading notes (binding)

1. **The tally-as-it-exists-today** is `VoteState.OnEndStateServer:186-198`:
   ```
   ordered = votesForPlayer.OrderByDescending(v => v.Value.Count).ToList();   // STABLE sort
   numTopTied = ordered.Count(v => v.Value.Count == ordered.First().Value.Count);
   if (numTopTied == 1 && ordered.First().Key != SKIP_VOTE_ID) winner = ordered.First().Key;
   else winner = SKIP_VOTE_ID;                                                 // tie OR top is SKIP
   ```
2. **POCO signature** (pure Domain): `ulong Resolve(IReadOnlyList<VoteCount> votesInInsertionOrder, ulong skipVoteId)` where `VoteCount` is a Domain struct `{ ulong Candidate; int Count; }`. The adapter passes the candidates **in the dictionary's enumeration (insertion) order** — `OrderByDescending` is a STABLE sort, so insertion order is the tie-break contract (Story 1.1 §3b C). Mirror the live algorithm exactly: stable `OrderByDescending(Count)`, top-count tie test, `numTopTied == 1 && top.Candidate != skip ? top.Candidate : skip`.
3. **Bit-for-bit with the goldens.** The Story 1.5 corpus must hold: unanimous/clear single winner → winner; tie for top → skip; abstention wins (SKIP strict max) → skip (the `!= skip` guard); single voter → that player; zero voters (all buckets 0 → all tied) → skip.
4. **Decision only (NFR4).** `VoteTally.Resolve` returns the winning client-id (or `skipVoteId`); it does NOT set `mostVotedPlayer`, add to the chaining list, or fire any RPC. The adapter (`VoteState`) applies the decision: `mostVotedPlayer = winner; if (winner != SKIP) ChainingManager.AddCharacterToChainingList(winner)` + the existing RPCs.
5. **Empty guard.** Live never passes an empty dict (`OnStartStateServer` seeds a bucket per non-fake char + SKIP), but guard `Count == 0 → skipVoteId` defensively (matches the "everything ties at nothing → skip" intent and avoids a `First()` throw).
6. **EditMode tests** `VoteTallyTests` `[Category("VoteTally")]` reproduce the golden corpus directly over `VoteCount` lists — fast, no NGO. The Story 1.5 PlayMode `VoteTallyGoldenMasterTests` stay green (the live path now delegates to the POCO).

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — `VoteTally` + `VoteCount` in Domain** (notes 1–2, 5); `DomainPurity` green
- [ ] **T2 — Re-point `VoteState.OnEndStateServer`** to build the ordered `VoteCount` list and apply `Resolve` (note 4); `read_console` clean
- [ ] **T3 — EditMode `VoteTallyTests`** reproducing the golden corpus (note 3)
- [ ] **T4 — Prove**: `run_tests category: VoteTally` (EditMode + the PlayMode goldens) + full regression green; console clean

## Dev Notes

**Created:** `Assets/Scripts/Domain/VoteTally.cs` (+ `VoteCount`); `Assets/Scripts/Tests/Editor/VoteTallyTests.cs`.
**Modified:** `Assets/Scripts/GameLogic/GameStates/VoteState.cs` (delegate the tally to `VoteTally`).
**Must NOT change:** the Story 1.5 `VoteTallyGoldenMasterTests`, `ChainingManager`.

### Adapter shape (post-extraction, in `VoteState.OnEndStateServer`)

```
var _votes = new List<VoteCount>();
foreach (var _kvp in votesForPlayer) _votes.Add(new VoteCount(_kvp.Key, _kvp.Value.Count)); // insertion order
ulong _winner = new VoteTally().Resolve(_votes, SKIP_VOTE_ID);
mostVotedPlayer = _winner;
if (_winner != SKIP_VOTE_ID)
{
    Character _votedCharacter = gameManager.characterManager.GetCharacters().Find(c => c.ownerClientId.Value == _winner);
    ChainingManager.instance.AddCharacterToChainingList(_votedCharacter.ownerClientId.Value);
}
// (UpdateMostVotedPlayer RPC + AskForUpdateAllCharactersRpc unchanged)
```

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.9] (lines 324–336)
- [Source: Assets/Scripts/GameLogic/GameStates/VoteState.cs:34,186-198] — the tally + `SKIP_VOTE_ID`
- [Source: Assets/Scripts/Tests/PlayMode/VoteTallyGoldenMasterTests.cs] — the frozen corpus to reproduce
- [Source: Story 1.1 §3b C] — pinned vote insertion order (the tie-break contract)

### Previous story intelligence

- 2.8 established the extract-to-Domain-POCO + decision-only (NFR4) pattern; 2.9 applies it to the tally. `OrderByDescending` stability is the load-bearing detail — insertion order is the contract.
- The live tally's tie path routes to SKIP regardless of `First()` identity (Story 1.5 ordering note), so outcome is order-independent in the tie case despite the order-dependent intermediate.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Extracted `VoteTally.Resolve(IReadOnlyList<VoteCount>, skipVoteId)` into Domain — mirrors the live algorithm exactly (stable `OrderByDescending`, top-count tie test, `numTopTied == 1 && top != skip ? top : skip`), decision-only (NFR4). `VoteState.OnEndStateServer` now builds the ordered `VoteCount` list (insertion order = tie-break contract) and applies the result (`mostVotedPlayer` + chaining add). Added `using CorruptionDuPortail.Domain;`.
- 8 EditMode `VoteTallyTests` reproduce the Story 1.5 corpus (clear winner / tie / abstention-via-guard / single voter / zero voters / empty / stable-sort tie / unique-top) over `VoteCount` lists.
- Behavior-preserving: the Story 1.5 PlayMode vote goldens (now delegating to the POCO) green; `DomainPurity` green; regression 42/42 goldens + 10/10 EditMode.

### File List

- **Added:** `Assets/Scripts/Domain/VoteTally.cs` (`VoteTally` + `VoteCount`), `Assets/Scripts/Tests/Editor/VoteTallyTests.cs`
- **Modified:** `Assets/Scripts/GameLogic/GameStates/VoteState.cs` (delegate the tally to `VoteTally`)

### Change Log

- 2026-06-10 — Extracted `VoteTally` (vote-count → outcome) into Domain; adapter applies the decision; tally now EditMode-tested. Story 2.9 → review.
