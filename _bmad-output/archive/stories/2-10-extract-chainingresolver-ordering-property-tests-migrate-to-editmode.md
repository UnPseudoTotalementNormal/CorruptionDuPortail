# Story 2.10: Extract `ChainingResolver` + ordering property tests + migrate to EditMode

Status: done

## Story

As a developer (Poyo),
I want chaining resolution extracted as a POCO `ChainingResolver` with its ordering contract enforced,
so that the highest-risk extraction is proven order-correct before downstream conditions rely on it.

## Acceptance Criteria

**Given** the order-varied `ChainingResolver` goldens and the permutation contract from Story 1.5
**When** `ChainingResolver` is extracted into `Domain`
**Then** it reproduces the goldens across the order-varied corpus
**And** property tests assert invariance for permutations that *must* be indifferent and sensitivity for permutations that *must* matter (per the 1.5 contract)
**And** the `ChainingResolver`-vs-live differential is kept running until conditions 2.3–2.6 are all green, then retired
**And** its tests run as EditMode in this same commit

### Acceptance reading notes (binding)

1. **The resolution-as-it-exists-today** is `ChainingManager.AddCharacterToChainingList:38-50`: server-only, **dedup by `Contains`**, append to a list that preserves first-occurrence insertion order. Extract this membership rule into a pure Domain POCO.
2. **POCO** `ChainingResolver` (pure Domain):
   - `IReadOnlyList<ulong> Resolve(IEnumerable<ulong> additionsInOrder)` — dedup, first-occurrence order (the batch form the property tests exercise; reproduces the Story 1.5 corpus).
   - `bool IsNewMember(IReadOnlyList<ulong> currentMembership, ulong candidate)` — the per-addition decision the live adapter applies to its `NetworkList`. (`Resolve` = `IsNewMember` applied iteratively.)
   Decision-only (NFR4): no NGO, no NetworkList, no RPC.
3. **Adapter wiring.** `ChainingManager.AddCharacterToChainingList` snapshots `chainingPlayers` into a `List<ulong>` and appends iff `IsNewMember(...)`. Behavior identical to the current `if (!chainingPlayers.Contains(id)) chainingPlayers.Add(id)` (Story 1.5 chaining goldens stay green).
4. **Ordering property contract (Story 1.5):**
   - **MUST be indifferent:** the membership SET is identical across input permutations — `Resolve([A,B,C])` and `Resolve([C,B,A])` are set-equal. (Order-indifference of membership — the chained set drives `Character.isChained`.)
   - **MUST matter:** the LIST sequence is first-occurrence insertion order — `Resolve([A,B,C])` == `[A,B,C]` while `Resolve([C,B,A])` == `[C,B,A]`. (Observable, though no winning condition reads list position — the 1.5 note.)
   - Dedup under interleaving: `Resolve([A,B,A,C,B])` == `[A,B,C]`.
5. **Differential retirement.** The AC's "keep the differential until 2.3–2.6 green, then retire" is already satisfied: 2.3–2.6 are all merged green, and there is no standing `ChainingResolver`-vs-live differential test to remove (the Story 1.5 `ChainingResolverGoldenMasterTests` characterize the live `ChainingManager` and stay as the live-side net). Note this in the story; no test deletion needed.
6. **EditMode tests** `ChainingResolverTests` `[Category("ChainingResolver")]` (note 4) — fast, no NGO. The Story 1.5 PlayMode `ChainingResolverGoldenMasterTests` stay green (the live path now delegates to the POCO's dedup rule).

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — `ChainingResolver` in Domain** (notes 1–2); `DomainPurity` green
- [ ] **T2 — Re-point `ChainingManager.AddCharacterToChainingList`** to delegate the dedup decision (note 3); `read_console` clean
- [ ] **T3 — EditMode `ChainingResolverTests`** — order-indifference (set) + must-matter (list) + dedup + empty + `IsNewMember` (note 4)
- [ ] **T4 — Prove**: `run_tests category: ChainingResolver` (EditMode + the PlayMode goldens) + full regression green; console clean

## Dev Notes

**Created:** `Assets/Scripts/Domain/ChainingResolver.cs`; `Assets/Scripts/Tests/Editor/ChainingResolverTests.cs`.
**Modified:** `Assets/Scripts/GameLogic/ChainingManager.cs` (delegate the dedup decision to `ChainingResolver`).
**Must NOT change:** the Story 1.5 `ChainingResolverGoldenMasterTests`, the `ChainCharacterRpc` side-effects.

### Adapter shape

```
public void AddCharacterToChainingList(ulong _characterId)
{
    if (!IsServer) { Debug.LogError(...); return; }
    var _current = new List<ulong>();
    foreach (var _id in chainingPlayers) _current.Add(_id);
    if (new ChainingResolver().IsNewMember(_current, _characterId))
    {
        chainingPlayers.Add(_characterId);
    }
}
```

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.10] (lines 338–351)
- [Source: Assets/Scripts/GameLogic/ChainingManager.cs:38-50] — the dedup rule being extracted
- [Source: Assets/Scripts/Tests/PlayMode/ChainingResolverGoldenMasterTests.cs] — the order-varied corpus + property note (Story 1.5 PR A)

### Previous story intelligence

- Story 1.5 PR A pinned membership order-indifference + dedup + insertion-order on the live `ChainingManager`; the POCO must reproduce them. `NetworkList<ulong>` supports `foreach` (no LINQ `ToList`).
- 2.8/2.9 established the extract-to-Domain + decision-only pattern. `ChainingResolver` is the Test-Architect's highest-risk extraction — the order properties are the load-bearing proof.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Extracted `ChainingResolver` into Domain: `Resolve(additions)` (dedup, first-occurrence order — batch form for property tests) + `IsNewMember(current, candidate)` (per-addition decision). Decision-only (NFR4). `ChainingManager.AddCharacterToChainingList` snapshots `chainingPlayers` and delegates the dedup decision — identical behavior to the old `Contains` guard.
- 8 EditMode `ChainingResolverTests`: membership order-indifference (set invariant across permutations — the MUST-hold property), list first-occurrence sequence (the MUST-matter property), dedup under interleaving, empty, `IsNewMember`.
- Differential retirement: 2.3–2.6 are green, and there was no standing ChainingResolver-vs-live differential to remove (the 1.5 `ChainingResolverGoldenMasterTests` remain as the live-side net). 
- Behavior-preserving: the 1.5 PlayMode chaining goldens (now delegating to the POCO) green; `DomainPurity` green; regression 42/42 + 10/10 EditMode.

### File List

- **Added:** `Assets/Scripts/Domain/ChainingResolver.cs`, `Assets/Scripts/Tests/Editor/ChainingResolverTests.cs`
- **Modified:** `Assets/Scripts/GameLogic/ChainingManager.cs` (delegate the dedup decision to `ChainingResolver`)

### Change Log

- 2026-06-10 — Extracted `ChainingResolver` (membership dedup + ordering) into Domain with EditMode property tests; adapter delegates the decision. Story 2.10 → review.
