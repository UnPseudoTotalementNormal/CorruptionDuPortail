# Story 3.1: Add the `IRandomProvider` port with Unity-backed and seeded implementations

Status: review

## Story

As a developer (Poyo),
I want a seedable `IRandomProvider` port the core depends on instead of `UnityEngine.Random`,
so that role distribution becomes reproducible and the core stays engine-free.

## Acceptance Criteria

**Given** the role-assignment selection currently calls `UnityEngine.Random` directly (`RoleAttributionState.cs:102`)
**When** the port is added
**Then** `IRandomProvider` (`Next(int max)`, seedable) lives in `Domain`
**And** a Unity-backed implementation lives in the adapter (ready to be injected in prod — the prod wiring lands with the consumer in 3.3) and a deterministic seeded implementation is available to tests
**And** no `Domain` type references `UnityEngine.Random` (NFR2)

## Acceptance reading notes (binding)

1. **Semantics pinned by the call site.** `RoleAttributionState.GiveRandomRole` uses `Random.Range(0, _availableRoles.Count)` — Unity's `int` overload is **`[0, max)` (max exclusive)**. So `IRandomProvider.Next(int maxExclusive)` returns a value in `[0, maxExclusive)`. `UnityEngine.Random.Range(0, maxExclusive)` and `System.Random.Next(maxExclusive)` both honour this — the Unity-backed and seeded impls are drop-in equivalent on the contract (NOT on the sequence: same seed yields different sequences across the two PRNGs; that is fine — prod uses the Unity impl, goldens use the seeded impl, see 3.2/3.3).
2. **Port** `IRandomProvider` in `Domain`: `int Next(int maxExclusive);`. Pure contract, no engine type.
3. **Seeded impl** `SeededRandomProvider` in `Domain` (engine-free — wraps `System.Random`, BCL only): constructed with an `int` seed; `Next(maxExclusive)` → `System.Random.Next(maxExclusive)`. Deterministic + reproducible across runs/machines for a given seed. This is the "available to tests" impl the 3.2 goldens + 3.3 `RoleDistributor` tests consume.
4. **Unity-backed impl** `UnityRandomProvider` in the **Game** adapter: `Next(maxExclusive)` → `UnityEngine.Random.Range(0, maxExclusive)`. This is the production source; it is created here but **not yet wired into the prod role-assignment path** — that injection lands in 3.3 alongside the `RoleDistributor` extraction (keeps this story a pure additive seam, zero behavior change, NFR7 strangler).
5. **Scope:** additive only. `RoleAttributionState` is **not** re-pointed in this story (no behavior change). The consumer extraction + prod injection is 3.3.
6. **`Next(0)` edge:** `System.Random.Next(0)` returns `0`; `UnityEngine.Random.Range(0, 0)` returns `0`. Both agree — document, do not special-case (mirrors the current code, which never calls with an empty pool in practice but would index `[0]` and throw downstream — unchanged).

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (full EditMode; console clean)
- [ ] **T1 — `IRandomProvider` + `SeededRandomProvider` in Domain** (notes 2–3); `DomainPurity` green
- [ ] **T2 — `UnityRandomProvider` in the Game adapter** (note 4)
- [ ] **T3 — EditMode `RandomProviderTests`** `[Category("RandomProvider")]`: seeded determinism (same seed → identical sequence), seed independence (different seed → not identical over a sample), range bounds `[0, max)` for both impls, `Next(1)` always 0, `Next(0)` → 0
- [ ] **T4 — Prove**: `run_tests category: RandomProvider` (EditMode) + full regression green; `read_console` clean

## Dev Notes

**Created:** `Assets/Scripts/Domain/IRandomProvider.cs`, `Assets/Scripts/Domain/SeededRandomProvider.cs`, `Assets/Scripts/Characters/UnityRandomProvider.cs` (or a suitable Game-assembly location), `Assets/Scripts/Tests/Editor/RandomProviderTests.cs`.
**Modified:** none (additive seam — NFR1).
**Must NOT change:** `RoleAttributionState` (re-pointed in 3.3), the frozen role-pool order (Story 1.1).

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.1] (lines 403–415)
- [Source: Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:90-104] — the frozen pool + `Random.Range` selection (3.3's extraction target)
- [Source: Assets/Scripts/Domain/ChainingResolver.cs] — the Domain POCO style

### Previous story intelligence

- Epic 2 complete (NFR6 gate green: 117 EditMode + 129 PlayMode). The role-pool iteration order was already frozen in Story 1.1 (`GetFrozenRolePoolOrder`), so the only remaining non-determinism in role assignment is the RNG seam this port introduces.
- Tests.Editor already references `CorruptionDuPortail.Domain` + `Game` — no asmdef edit needed.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Added `IRandomProvider` (`int Next(int maxExclusive)` → `[0, maxExclusive)`) in Domain; `SeededRandomProvider` (Domain, `System.Random`-backed, reproducible) + `UnityRandomProvider` (Game adapter, `UnityEngine.Random.Range(0, max)` — the exact live call).
- Additive seam only — `RoleAttributionState` NOT re-pointed (consumer wiring + prod injection deferred to 3.3). Zero behavior change.
- 6 EditMode `RandomProviderTests` `[Category("RandomProvider")]`: seeded determinism (same seed → identical 1000-draw sequence), seed divergence, `[0,max)` bounds for both impls, `Next(1)→0`, `Next(0)→0`.
- Domain stays pure (DomainPurity green). 123/123 EditMode (117 + 6 new). PlayMode untouched (no prod-path change).

### File List

- **Added:** `Assets/Scripts/Domain/IRandomProvider.cs`, `Assets/Scripts/Domain/SeededRandomProvider.cs`, `Assets/Scripts/GameLogic/UnityRandomProvider.cs`, `Assets/Scripts/Tests/Editor/RandomProviderTests.cs`

### Change Log

- 2026-06-11 — Story 3.1 drafted; add `IRandomProvider` port + `SeededRandomProvider` (Domain) + `UnityRandomProvider` (adapter), additive seam, consumer wiring deferred to 3.3.
