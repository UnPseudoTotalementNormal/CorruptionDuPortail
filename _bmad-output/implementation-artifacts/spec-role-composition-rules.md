---
title: 'Composition rule system — faction-minimum guarantee (≥1 anomaly, ≥1 chosen) at both the start gate and the distributor'
type: 'feature'
created: '2026-07-16'
status: 'done'
baseline_commit: '976483f6'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-role-attribution-uitk.md'
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The lobby start gate (`LobbyState.OnStartGameButtonPressed`) is a scalar check `Σforced ≤ players ≤ Σmax`. It cannot express — and `RoleDistributor` cannot honour — a **per-faction floor**. Two real bugs follow: (1) 5 players, anomaly `max5/forced5` + chosen `max5/forced0` → all 5 reals are forced anomaly → **0 real élu**, yet the gate passes; (2) anomaly `max5/forced0` + chosen `max5/forced0`, 5 players → the RNG draw can produce **0 anomaly or 0 chosen**. The game must always contain ≥1 real anomaly AND ≥1 real élu. Poyo: "c'est un peu des deux" — it is both an attribution bug and a validation gap.

**Approach:** Introduce a small, **declarative, extensible composition-rule set** consumed by BOTH sides so they never disagree. A pure `CompositionValidator` (Domain POCO) evaluates a snapshot (players + per-role `{faction, max, forced}`) against the rules and returns pass/fail + a readable reason. `RoleDistributor` gains a **faction-reservation pass** that *guarantees* the reals: it protects `min_f` copies of each required faction from the fake draw (generalising the existing forced mechanism — the real loop drains all un-faked copies, so a protected copy is always assigned real). Rules today: `Σmax ≥ players` (coverage), `≥1 anomaly`, `≥1 chosen`; adding/removing a faction floor = one data entry.

## Boundaries & Constraints

**Always:**
- **Single source of truth for the rules.** The faction minimums live in ONE authored place (a `CompositionRuleSet` SO, default `{anomaly:1, chosen:1}`) read by the server gate, the live distribution, and the client footer mirror. Never hardcode the pair in two places.
- **Gate ↔ distributor contract.** Every composition the gate ACCEPTS must yield a valid distribution with ≥1 real of each required faction and no exception. This is the highest-value test.
- **`RoleDistributor` stays pure (NFR4):** no NGO, no `RoleDataObject`, no `UnityEngine.Random`. Faction info enters as a parallel `IReadOnlyList<FactionType>` (frozen pool order) + `IReadOnlyList<FactionMinimum>`. `CompositionValidator` is equally pure.
- **Faction is NOT added to the replicated DTO.** `RoleSettingEntry` keeps `{roleId, max, forced}`. Role→faction is derived from `RoleDataObject.role.factionType` — server-side from the authored `roleAttributionDictionary`, client-side from the data source (which already groups by faction). No wire change.
- **Faction-reservation top-up is deterministic (frozen pool order), consumes NO RNG.** When `Σforced` over a faction already ≥ its min, that faction adds zero reservation. The real/fake RNG draw order is untouched → golden masters move ONLY where a faction rule actually binds.
- **Server authority preserved.** The gate runs server-side in `OnStartGameButtonPressed` (host-only callers). The client footer is a UX-only mirror; never trust it.
- **Readable failure (Samus rule).** A blocked start states the reason AND points to the fix: "6 rôles garantis pour 5 joueurs — baisse le forcé Anomalie ou ajoute un joueur", "Aucun rôle Élu dans la partie", "Pool trop petit".

**Ask First:**
- Promoting the rule set from a POCO default to a **designer-editable SO asset** vs keeping a static Domain default (spec assumes the SO — confirm the wiring cost is acceptable).
- Whether the deterministic top-up (which specific faction role is protected) should instead be **RNG-random** (spreads the guaranteed real across a faction's roles; costs a golden re-pin of the RNG stream).
- Any rule beyond the three listed (camp-ratio, marginal floor) — deferred by prior session ("on verra à la fin").

**Never:**
- Do not change the fake-then-real depleting-draw core of `RoleDistributor` beyond adding the reservation pass.
- Do not mutate a ScriptableObject at runtime; do not add faction to `RoleSettingEntry`; no owner-write NetworkVariables; `AudioSource`/`System.Threading.Tasks.Task`.
- Do not build the "Prêt" ready-to-start system here — it is deferred (deferred-work.md, `lobby-ready-system`). The "Démarrer" button stays as-is; only its gate LOGIC and reason text change.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Forced monopolises reals | 5 players; anomaly max5/forced5, chosen max5/forced0; min {anomaly1,chosen1} | Gate INVALID: guaranteed reals = 5 forced + 1 chosen top-up = 6 > 5 players → reason points to the forced anomaly | Start refused, logged |
| RNG could drop a faction | 5 players; anomaly max5/forced0, chosen max5/forced0; same mins | Gate VALID; distributor protects 1 anomaly + 1 chosen copy from the fake draw → ≥1 real of each guaranteed | N/A |
| Required faction absent | Pool has no `chosen` role (Σmax_chosen=0); min chosen1 | Gate INVALID: "Aucun rôle Élu dans la partie" | Start refused |
| Coverage shortfall | players > Σmax | Gate INVALID: "Pool trop petit" (existing rule, kept) | Start refused |
| Rule already met by forced | anomaly forced≥1, chosen forced≥1 | No faction top-up (deterministic, zero RNG); distribution byte-identical to a forced-only config | N/A |
| No faction rules passed | `factionMinimums` empty (headless golden harness) | `Distribute` reservation pass early-outs → byte-identical to the pre-change golden | N/A |
| Client footer mirror | Any lobby edit | Footer runs the SAME `CompositionValidator`; button enabled iff valid; reason + offending-faction highlight | Read-only mirror |
| Distributor infeasible call | Faction min unsatisfiable (should be gate-blocked) | Best-effort protect (never throws for faction infeasibility — the gate owns feasibility) + debug assert | Guarded |

</frozen-after-approval>

## Code Map

**Reuse / read:**
- `Assets/Scripts/Domain/RoleDistributor.cs` — pure two-loop draw; the reservation pass extends the `fakeable = max − forced` shielding to also honour faction minimums.
- `Assets/Scripts/Domain/FactionType.cs` — `anomaly=0, chosen=1, marginal=2, unknown=3` (chosen = "élu").
- `Assets/Scripts/Characters/{Role,RoleDataObject}.cs` — `role.factionType` is the role→faction source.
- `Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs` — `GetRoleCount`/`GetForced`/`GetTotalRolesToAttribute`/`GetTotalForced` (max/forced per RoleID).

**Mutate:**
- `Assets/Scripts/Domain/RoleDistributor.cs` — add `factions` + `factionMinimums` params; deterministic reservation top-up before the fake loop; empty-minimums early-out.
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` — `OnStartStateServer` builds a `_factions` frozen-order list and passes it + the rule set into `Distribute`; add `BuildCompositionSnapshot(gameSettingsManager, playerCount)` + `ValidateComposition(...)` helpers reused by the gate; serialized `CompositionRuleSet` ref (nullable → no faction rules for headless tests).
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — `OnStartGameButtonPressed` replaces the two scalar guards with `RoleAttributionState.ValidateComposition(...)`; keep the host-only server path.
- `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` — `BuildFooter` gate mirror uses `CompositionValidator` + the readable reason/offending-faction highlight (button stays "Démarrer la partie").
- `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs` — expose the faction minimums + per-role faction so the controller can run the validator client-side.

**New (Domain, pure):**
- `Assets/Scripts/Domain/CompositionValidator.cs` — `FactionMinimum` struct, `CompositionSnapshot`, `CompositionValidation {IsValid, Failures[]}`, `Validate(snapshot, minimums)`.
- `Assets/Scripts/GameLogic/GameSettings/CompositionRuleSet.cs` (SO) + authored `.asset` under `Assets/GameData/` (`{anomaly:1, chosen:1}`).

**Tests:**
- `Assets/Scripts/Tests/Editor/RoleDistributorTests.cs` — faction-reservation cases + empty-minimums equivalence.
- `Assets/Scripts/Tests/Editor/CompositionValidatorTests.cs` (new) — the gate feasibility + reasons + gate↔distributor contract.
- `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs` — existing goldens pass EMPTY minimums (byte-identical); ONE new mixed-faction golden proving ≥1 real per faction.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Domain/CompositionValidator.cs` -- create the pure validator (`FactionMinimum`, `CompositionSnapshot`, `Validate`) -- single evaluation surface for gate + mirror. Rules: coverage `Σmax≥players`; per-faction `Σmax_f≥min_f`; guaranteed-reals `Σforced + Σ_f max(0, min_f − Σforced_f) ≤ players`. Each failure carries a French reason + the offending faction/roles.
- [x] `Assets/Scripts/Domain/RoleDistributor.cs` -- add `factions`/`factionMinimums`; deterministic top-up of `protect[i]` (start `forced[i]`, raise to reach each `min_f` over its faction in frozen order); `fakeable[i] = max[i] − protect[i]`; empty-minimums early-out (no RNG shift) -- guarantees ≥`min_f` reals per faction.
- [x] `Assets/Scripts/GameLogic/GameSettings/CompositionRuleSet.cs` (+ `.asset`) -- SO holding `List<FactionMinimum>`; `ToMinimums()` → plain list -- single authored source.
- [x] `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` -- `_factions` list in `OnStartStateServer`, pass rule set into `Distribute`; `BuildCompositionSnapshot` + `ValidateComposition` helpers; serialized `CompositionRuleSet` ref -- wires faction into distribution + shared gate logic.
- [x] `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` -- swap the two scalar guards for `ValidateComposition` -- authoritative faction gate, server-side.
- [x] `Assets/Scripts/UI/LobbyRoles/{LobbyRolesUitkController,GameLobbyRolesDataSource}.cs` -- footer mirror runs `CompositionValidator`; readable reason + offending-faction highlight -- UX matches the server gate.
- [x] `Assets/Scripts/Tests/Editor/{RoleDistributorTests,CompositionValidatorTests}.cs` + `Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs` -- cover the I/O matrix incl. the gate↔distributor contract, empty-minimums equivalence, and a mixed-faction golden -- pin the redefinition.

**Acceptance Criteria:**
- Given 5 players with anomaly `max5/forced5` + chosen `max5/forced0` and mins `{anomaly1,chosen1}`, when the host presses Démarrer, then the gate refuses with a reason naming the over-forced anomaly and the game does not start.
- Given 5 players with anomaly `max5/forced0` + chosen `max5/forced0` and the same mins, when the game starts, then the distribution contains ≥1 real anomaly AND ≥1 real élu on every seed.
- Given `factionMinimums` empty, when `Distribute` runs, then its output is byte-identical to the pre-change golden under the same seed/counts.
- Given any composition the gate accepts, when distributed, then no exception is thrown and every required faction has ≥1 real (property/contract test).
- Given the client footer, when the composition is invalid, then Démarrer is disabled with the same reason the server would log.

## Design Notes

Why protecting-from-fake guarantees a real: the live path assigns `fakeCount = Σmax − players` fakes then `realCount = players` reals, and `fakeCount + realCount = Σmax` — so the real loop drains **every** un-faked copy. A copy never drawn as a fake is therefore always assigned to a real. `forced` already exploits this (`fakeable = max − forced`); the faction pass simply raises the protected count per faction to its floor. Deterministic frozen-order top-up keeps the RNG stream identical to an equivalent forced-only config, so goldens only move where a rule truly binds.

Extensibility: `Validate` runs a fixed handful of checks where the faction floors are DATA (`List<FactionMinimum>`). Add/remove a faction floor = edit the `.asset`. If rule *kinds* proliferate later, promote to an `ICompositionRule` list — not now (YAGNI).

**Implementation notes (done):** The existing golden masters stayed **byte-identical with no edits** — the headless harness leaves `_compositionRules` null ⇒ empty minimums ⇒ the distributor's reservation early-outs. The faction guarantee is proven three ways instead of by re-pinning goldens: (1) `RoleDistributorTests` mechanics + empty-minimums equivalence; (2) `CompositionValidatorTests` — the gate rules + the **gate↔distributor contract** across 5 configs × 5 seeds; (3) `RoleAttributionGateTests` (EditMode adapter, no NGO) proves faction flows through `BuildCompositionSnapshot`; (4) one new PlayMode golden runs the LIVE `OnStartStateServer` end-to-end. The contract holds precisely because `guaranteed-fit` (Σprotect ≤ players) forces the fake loop to consume exactly `fakeCount`, leaving the real loop to drain every protected copy. New seam files added beyond the Code Map: `ILobbyRolesDataSource.GetFactionMinimums` + `DemoLobbyRolesDataSource` impl (harness mirrors the production rule). Authored asset: `Assets/ScriptableObjects/GameSettings/CompositionRuleSet.asset` wired onto `RoleAttributionState.asset` (`_compositionRules`, verified). Verification: EditMode 437/437, PlayMode 13/13 (LobbyState guard messages updated to the new reasons).

## Verification

**Commands:**
- `mcp__UnityMCP__run_tests` (EditMode, filter `RoleDistributorTests`+`CompositionValidatorTests`) -- expected: green, incl. the contract + equivalence tests.
- `mcp__UnityMCP__run_tests` (PlayMode, filter `RoleAssignmentGoldenMasterTests`) -- expected: green with the re-pinned + new mixed-faction golden.
- `mcp__UnityMCP__read_console` after each script edit -- expected: no compile errors before running tests.

**Manual checks:**
- In the lobby (host), set the two Poyo bug configs on the tablet and confirm the footer reason + disabled Démarrer match the matrix.

## Suggested Review Order

**Composition rules (Domain core — the design intent)**

- Entry point: the single rule surface both the gate and the mirror call — coverage, guaranteed-fit (names the over-forced faction), per-faction capacity.
  [`CompositionValidator.cs:92`](../../Assets/Scripts/Domain/CompositionValidator.cs#L92)

**Distribution guarantee (why the gate can trust it)**

- The faction-reservation pass: protects `min` copies of each faction from the fake draw so the real loop places them; deterministic, no RNG, empty-list no-op.
  [`RoleDistributor.cs:163`](../../Assets/Scripts/Domain/RoleDistributor.cs#L163)
- The new 8-arg `Distribute` overload + protect/fakeable wiring (5-arg overload delegates ⇒ existing callers byte-identical).
  [`RoleDistributor.cs:63`](../../Assets/Scripts/Domain/RoleDistributor.cs#L63)

**Server gate + adapter (authoritative)**

- The gate now delegates to the shared validator (subsumes the old scalar Σforced/Σmax guards).
  [`LobbyState.cs:42`](../../Assets/Scripts/GameLogic/GameStates/LobbyState.cs#L42)
- Snapshot/validate seam: joins replicated max/forced with authored faction; feeds the same list into the live distribution.
  [`RoleAttributionState.cs:128`](../../Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs#L128)
- Live distribution passes the frozen faction list + rule set; total derived from Σ_initialCounts (consistent with the gate).
  [`RoleAttributionState.cs:59`](../../Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs#L59)

**Client mirror (UX-only)**

- Footer runs the SAME validator; disabled Démarrer + reason naming the offending faction.
  [`LobbyRolesUitkController.cs:521`](../../Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs#L521)
- The mirror sources its minimums from the same production ruleset (single source of truth).
  [`GameLobbyRolesDataSource.cs:109`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L109)

**Config / authoring**

- The authored rule set → pure minimums (coalesces duplicate faction entries so the two consumers can't disagree).
  [`CompositionRuleSet.cs:38`](../../Assets/Scripts/GameLogic/GameSettings/CompositionRuleSet.cs#L38)

**Tests (supporting)**

- Gate rules + the gate↔distributor contract (5 configs × 5 seeds).
  [`CompositionValidatorTests.cs:1`](../../Assets/Scripts/Tests/Editor/CompositionValidatorTests.cs#L1)
- Distributor faction mechanics + empty-minimums equivalence.
  [`RoleDistributorTests.cs:1`](../../Assets/Scripts/Tests/Editor/RoleDistributorTests.cs#L1)
- EditMode adapter (faction flows through ValidateComposition) + live-path PlayMode golden.
  [`RoleAttributionGateTests.cs:1`](../../Assets/Scripts/Tests/Editor/RoleAttributionGateTests.cs#L1)
