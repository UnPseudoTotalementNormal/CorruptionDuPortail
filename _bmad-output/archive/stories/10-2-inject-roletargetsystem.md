# Story 10.2: Inject RoleTargetSystem (fan-in 16)

Status: review

## Story

As a developer,
I want `RoleTargetSystem` consumers to receive it injected,
so that targeting resolution stops going through a static.

## Acceptance Criteria

1. **Recipe §7 applied** to `RoleTargetSystem` (fan-in 16, has `OnNetworkSpawn`): census → optional slice interface (likely `ITargetingService`-shaped if read/record splits cleanly — record the call) → root accessor → consumers per lane → static narrowed/annotated.
2. **Targeting behaviour unchanged:** `NewTargeting` flows (e.g. `PTruthChains.OnCardClickedRpc:34`), target validation paths, and any reveal interactions pass the fixture + goldens unchanged.
3. **Consumers** are mostly powers (the `Power` base-field extension pattern from 7.1/10.1 applies) + `TargetUtils` (static utility — verify-don't-force candidate: if it cannot take injection, route its resolution through a parameter from the calling power, or record).
4. **Gated:** suite + fixture + goldens per batch; registry/guards green; sprint-status.

## Tasks / Subtasks

- [x] **Task 1:** Census (powers + dispatcher + RobotBoardInfo; TargetUtils premise corrected); slice decision.
- [x] **Task 2:** Root accessor (concrete) + `Power.roleTargetSystem` base field (lane C).
- [x] **Task 3:** 13 targeting powers rerouted onto the base field; `RobotBoardInfo` lane C; `PowerEffectDispatcher` recorded (NOT TargetUtils).
- [x] **Task 4:** `instance` → recorded façade + guard #1 lock + §4c census; gates (EM 162 / PM 148); sprint-status.

## Dev Notes

- `TargetUtils` is the interesting case: a static utility consuming a singleton. Cleanest fix = pass the dependency as a parameter from the (already-injected) calling power — no root access inside statics (the root is for lane C glue only, guard-enforced). This becomes the precedent for static-util consumers track-wide.
- Staleness: census-driven.

### Project Structure Notes

- Modified: `RoleTargetSystem.cs`, `CompositionRoot.cs`, powers/UI consumers, `TargetUtils.cs`, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Pure decision logic in statics → candidates for Domain extraction later (Epic 11 notes any found). GetSafeRpcTarget verbatim.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7, §8 Epic 10] / [epics.md#Story 10.2]
- [Source: Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs] / [Assets/Scripts/Characters/Powers/Target/TargetUtils.cs].

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors (forced rebuild, 16 files). EditMode **162/162** + PlayMode **148/148** unchanged. Power goldens (PowerGoldenTraceTests / Entrapment / Corruption / Vision) exercise NewTargeting through the migrated powers and pass with zero harness changes — RoleTargetSystem injection resolves the same global the powers read before.

### Completion Notes List

**Task 1 — census + slice decision.**

`RoleTargetSystem` (namespace `RoleTarget`, fan-in 16) is a non-de-singletonised replicated singleton — same shape as ChatManager (10.1): `instance` claimed in `OnNetworkSpawn`, naive duplicate-guard (NOT 5.0c Design B per-NM), no `RoleTargetSystem.For(nm)`. So the **10.1 recipe applies verbatim**: root accessor forwards to the singleton, powers consume an injected `roleTargetSystem` base field resolved lane C, guard #1 locks the global.

Census of `RoleTargetSystem.instance`, partitioned:

- **Powers (lane C via `Power` base field) — MIGRATE NOW (14):** PVisionOfTheImpossible, PTruthChains, PReincarnation, PPersonalBeacons, POmniscience, PLackOfAffection, PHighPriorityBounty (×2), PEmbraceOfShadows, PDroolyHealing, PClandestineObservation, PChainedByTheShadows, PCardsShuffling (×2), PBlessing. All use `NewTargeting` (command/RPC) and/or `GetAllTargeting*`/`GetAllTargetersForTarget` (reads).
- **UI consumer `RobotBoardInfo` (lane C) — MIGRATE NOW:** it is a `NetworkBehaviour` with `OnNetworkSpawn`, **registered in `DiSeamMigratedConsumers.All` (8.3)**, and reads `GetAllTargetersForTarget` in a server RPC. It MUST migrate (else the guard-#1 lock below would flag it). Resolve `roleTargetSystem` lane C in its `OnNetworkSpawn` (it already wires CharacterManager/GameManager lane A; RoleTargetSystem is non-de-singletonised, so a lane-C root resolve is cleaner than a new scene field + `InjectedManagerTypes` entry).
- **Static POCO dispatcher — RECORD (verify-don't-force):** `PowerEffectDispatcher` (`NewTargeting` brick, line 38). `static class`, no injection context — same bucket as its already-recorded `CharacterManager.instance`/`ChatManager.instance`. Full POCO cleanup = Epic 11.1.

**TargetUtils premise corrected (AC3).** The story Dev Notes expected `TargetUtils` to be the static-util RoleTargetSystem consumer ("5 hits"). **It is NOT** — `TargetUtils` consumes `CharacterManager.instance` (3×) + `CompositionRoot.For(Singleton).GameInfoRevealer`, never `RoleTargetSystem`. Its `CharacterManager.instance` reads are already §4a-recorded (Epic 10.5). So the "static-util, prefer parameter-injection over root-access" precedent the story wanted applies instead to `PowerEffectDispatcher` (recorded for Epic 11.1's dispatcher→POCO pass, where `NewTargeting` would take the dependency as a parameter from the already-injected calling power, not a root-access-in-a-static). No TargetUtils edit in 10.2.

**Slice decision: inject CONCRETE `RoleTargetSystem`, NO interface (D-NFR6, recorded).** The surface splits read (`GetAllTargeting*`) vs record (`NewTargeting`), but the consumers are powers issuing fire-and-forget targeting RPCs / one UI read — no decision logic or unit test would benefit from a mock. NewTargeting is already the seam the Epic-4 goldens observe (via the `NewTargeting` EffectDescriptor brick). Mirrors 10.1: concrete base field, extract a slice later only if a targeting decision-logic test needs one.

**Task 2 — root accessor + base field (lane C).** `CompositionRoot` gains a `RoleTargetSystem` accessor on the instance + `Services` struct, forwarding to `RoleTarget.RoleTargetSystem.instance` (concrete, no scene-wiring/`InjectedManagerTypes` — not de-singletonised). `Power` gains `protected RoleTargetSystem roleTargetSystem;` resolved ONCE in `OnNetworkSpawn` via `CompositionRoot.For(NetworkManager).RoleTargetSystem`, right after the `chatManager` resolve. Null-tolerant (no `Assert`). No `PowerComponent` change (no component consumes RoleTargetSystem).

**Task 3 — batch migration (AC2, AC3).** 13 `Power` subclasses rerouted `RoleTargetSystem.instance.` → `roleTargetSystem.` (PVisionOfTheImpossible, PTruthChains, PReincarnation, PPersonalBeacons, POmniscience, PLackOfAffection, PHighPriorityBounty, PEmbraceOfShadows, PDroolyHealing, PClandestineObservation, PChainedByTheShadows, PCardsShuffling, PBlessing). `RobotBoardInfo` (registered `All` NetworkBehaviour) migrated lane C: new `private RoleTargetSystem roleTargetSystem;` resolved in its `OnNetworkSpawn` (before the `IsServer` guard), `AskForNewTextRpc` reads the field. `PowerEffectDispatcher` (static POCO, `NewTargeting` brick) recorded — NOT migrated (Epic 11.1). `TargetUtils` untouched (does not consume RoleTargetSystem — premise corrected).

**Task 4 — static narrowing + lock + gate (AC1, AC4).** `RoleTargetSystem.instance` annotated as the recorded-callers-only façade (`// recorded: dies in 12.3`). Guard #1 (`DiSeamNoLocatorGuardTests.ForbiddenLocators`) gained `"RoleTargetSystem.instance"`; all registered consumers (10 targeting powers + RobotBoardInfo) verified clean (EM 162 incl. the guard). §4c census table added to the architecture doc. No new test: AC4 mandates suite + fixture + goldens (not a new smoke); the power goldens (PowerGoldenTraceTests etc.) exercise NewTargeting through the migrated powers and pass unchanged — RoleTargetSystem has no `GetSafeRpcTarget` (it broadcasts `SendTo.Everyone`), so there is no bot-interception path needing a dedicated smoke. EM 162 / PM 148, guards green; RPC bodies + Awake/OnNetworkSpawn duplicate-guard untouched.

### File List

**Modified (production):**
- `Assets/Scripts/Characters/Powers/Power.cs` — `using RoleTarget;`, `protected RoleTargetSystem roleTargetSystem;` base field, resolved lane C in `OnNetworkSpawn`.
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `using RoleTarget;`, `RoleTargetSystem` accessor on the instance + `Services` struct (forward to the singleton).
- `Assets/Scripts/Characters/Powers/{PVisionOfTheImpossible,PTruthChains,PReincarnation,PPersonalBeacons,POmniscience,PLackOfAffection,PHighPriorityBounty,PEmbraceOfShadows,PDroolyHealing,PClandestineObservation,PChainedByTheShadows,PCardsShuffling,PBlessing}.cs` — `RoleTargetSystem.instance` → `roleTargetSystem`.
- `Assets/Scripts/Board/UI/RobotBoardInfo.cs` — lane-C `roleTargetSystem` field + resolve in `OnNetworkSpawn` + reroute.
- `Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs` — `instance` recorded-callers-façade annotation (`// recorded: dies in 12.3`). No code/visibility change.

**Modified (tests):**
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"RoleTargetSystem.instance"` added to `ForbiddenLocators`.

**Docs:**
- `_bmad-output/refactor-architecture-despaghetti.md` — §4c RoleTargetSystem static census + TargetUtils premise correction.
- `_bmad-output/implementation-artifacts/deferred-work.md` — story-10.2 deferrals.
- `_bmad-output/implementation-artifacts/10-2-inject-roletargetsystem.md` (this story); `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Injected RoleTargetSystem (fan-in 16) into 13 targeting powers (`Power.roleTargetSystem` base field, lane C) + RobotBoardInfo (lane C). Concrete (no interface — D-NFR6); stays a singleton (root = indirection point). `instance` → recorded façade; guard #1 locks `RoleTargetSystem.instance`; §4c census. PowerEffectDispatcher recorded (Epic 11.1); TargetUtils premise corrected (not a consumer). EM 162/162 + PM 148/148 (power goldens unchanged). Status → review. |
