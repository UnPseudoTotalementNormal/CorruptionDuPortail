# Spec — Powers → POCO v2 (clean rebuild)

Branch: `refactor/powers-poco-v2` (off `Dev` @ 9e77d9d). Supersedes the `refactor/powers-poco-phase-a` resolver approach (owner rejected the single-`PowerResolver` god-class). Goal (Poyo): **every power's logic is a clean POCO, unit-testable and modular, so debugging + adding powers is easy.** Cost irrelevant; correctness + cleanliness first.

Decided by architecture party (Cloud Dragonborn / Winston / Indie), ~95% converged. This spec is the plan of record.

## The one idea: humble object, per power

**The POCO decides. The `NetworkBehaviour` replicates (dumb). An executor applies effects.** No layer does two of the three. There is NO central `PowerResolver` and NO central power `switch` — each power owns its own decision.

```
decision (pure) : (PowerContext, state) -> (EffectDescriptor[], nextState)
replication      : NetworkBehaviour holds typed NVs, calls decision, writes nextState
execution        : per-effect executor mutates engine / RPCs
```

## Assemblies

- **`CorruptionDuPortail.Domain`** (`noEngineReferences: true`, EditMode-pure) — `IPowerDecision`, `PowerContext`, `PowerOutcome`, every per-power `XDecision` POCO, state port interfaces + state structs, the `EffectDescriptor` data vocabulary. The compiler forbids any Unity/NGO reference here — the purity wall.
- **`Game` (existing NGO asmdef)** — `PowerHolder` (generic), per-power thin `XNet : NetworkBehaviour` (only for stateful powers), `IEffectExecutor` + `EffectDispatcher` registry, `EffectRuntime`, `PowerCatalog`, chaining.
- **Tests EditMode** — one test file per power decision (pure, value-equality asserts), + an effect-registry completeness guard test.

## Contracts (Domain)

```csharp
public interface IPowerDecision {
    PowerId Id { get; }
    bool IsPassive { get; }
    PowerOutcome Decide(in PowerContext ctx);   // pure
}

public readonly struct PowerOutcome {           // value
    public bool Accepted { get; }
    public IReadOnlyList<EffectDescriptor> Effects { get; }
    public int UsesConsumed { get; }
    // reject reason (debug), factory helpers Accept/Reject
}

// PowerContext = read-only snapshot: caster slot, target(s), + the EXISTING query ports
// (IGameStateQuery / ICharacterQuery) already extracted in the Epic 1-12 refactor, + a
// ctx.State<TPort>() resolver for a power's own replicated-state port. NO NetworkManager,
// NO MonoBehaviour, NO singleton.
```

## Fork decisions (locked)

- **Fork 1 — heterogeneous replicated state → bounded hybrid.** Stateless powers = pure `IPowerDecision`, hosted on ONE shared generic `PowerHolder` via `[SerializeReference]`. The ~4 stateful units keep a THIN per-power `NetworkBehaviour` (`XNet`, ~30 lines) that owns its typed NVs and exposes a **narrow state port** interface to the POCO (`ICardsShufflingState`…). The POCO never sees a `NetworkVariable`; in test it gets a `Fake…State`.
  - **No `NetworkList`.** Replace with `NetworkVariable<FixedList128Bytes<ulong>>` (fixed `T`, value semantics, kills the known late-joiner dup bug). If a real `NetworkList` is unavoidable, reuse the `[CHARLIST]` ref-dedup guard.
- **Fork 2 — effects → data descriptors + typed executor registry.** `EffectDescriptor` stays pure data in Domain (value-equatable — largely reusable from v1). Each effect gets an `EffectExecutor<T>` in Game, registered in a dictionary; `EffectDispatcher` dispatches by concrete type. Adding an effect = 1 Domain data type + 1 executor, **zero shared file edited**. A reflection **completeness guard-test** (every `EffectDescriptor` subtype has an executor) replaces compile-exhaustiveness.
- **Chaining (was PCChainer component) → `ChainDecorator` POCO** wrapping any inner `IPowerDecision` + a thin `ChainStateCarrier` NB for `currentChain`. Composable on any power (`new ChainDecorator{ Inner = new EmbraceDecision(), MaxChain = 2 }`).
- **Catalog → attribute auto-registration** (`[PowerLogic(PowerId.X)]` scanned at boot). Adding a power touches **no shared file**.

## Per-power authoring rules (the payoff)

| Case | New files | Shared files edited |
|---|---|---|
| Power, no replicated state | `XDecision.cs` (Domain) + prefab `[SerializeReference]` | `PowerId` enum (1 append line) |
| Power, replicated state | `XDecision.cs` + `IXState.cs` + `XState`(struct) + `XNet.cs` (Game) + prefab | `PowerId` enum |
| New effect verb | `XEffect.cs` (Domain data) + `XExecutor.cs` (Game) | none (guard-test catches a miss) |

Test a power = build a `PowerContext` + `Fake` state by hand, call `Decide`, assert effect list by value. Zero NGO, ~0.1s.

## Migration phases

- **Phase 0 — core skeleton.** `IPowerDecision`/`PowerContext`/`PowerOutcome`, the `EffectDescriptor` data (port from v1), `EffectExecutor<T>` + `EffectDispatcher` + registry + guard-test, `PowerHolder` (generic), `PowerCatalog` (attribute scan). Prove with ONE stateless power + ONE stateful power end-to-end + EditMode tests. NO prefab mass-rewire yet — additive, coexists with the old `Power` path.
- **Phase 1 — migrate stateless powers** to decisions (bulk; each = 1 Domain file + test).
- **Phase 2 — migrate the 4 stateful powers** (BoundByInk, CardsShuffling, CorruptingMark) + the chaining decorator; carriers + FixedList swap.
- **Phase 3 — NGO wiring + prefabs.** Generic holder on prefabs, `[SerializeReference]` decision, per-power carriers, catalog. Rewire spawn/attribution. This is the network-critical, playtest-gated phase.
- **Phase 4 — delete the old path** (`Power` inline effects, `PowerResolver`, `PowerEffectDispatcher` switch, `PowerComponent`s). Static/grep absence proof. Full green gate + real 2-build playtest.

## Progress log (branch `refactor/powers-poco-v2`)

**Phase 0 (foundation) — DONE.** Decision core (`IPowerDecision`/`PowerContext`/`PowerOutcome`/`IRosterView`/`IPowerStateResolver`), runtime registry (`EffectDispatcher` by-type + `PowerHolder` humble), 15 executors, power-local state mechanism (`EffectRuntime.PowerState` + state ports). All three patterns proven: passive, active, power-local state.

**Phase 1-2 (decision migration) — ✅ DONE, 22/22, all EditMode-green (38 PowerDecision tests ~0.14s; full suite 362/362).** Every power's logic is now a pure POCO `IPowerDecision`, unit-tested with zero NGO. The per-power notes below are historical (they list the small infra each needed — all built: roster/query/state ports, event ports, client-context flag, give-power bricks). The state carriers + prefab/spawn wiring are Phase 3.

_(historical migration order, all now done):_
- Passive: CorruptionParanoia, CorruptionInsight, CorruptionKnowledge, EyeOfTheVoid, AutoCorruption, InfiniteMessage.
- Active: ChainedByShadows, TruthChains, Blessing, HighPriorityBounty, CursedVision, Omniscience.
- **Remaining ~10 powers** (each needs a small bespoke infra bit, then the pure decision + test):
  - BoundByInk / CorruptingMark / CardsShuffling — state carriers (mechanism ready; add each state port + carrier + `FixedList` swap for the lists).
  - EmbraceOfShadows — char+role branch + IFailablePower success/fail events (power-local event effects).
  - LackOfAffection — runs on the contacted target's client; needs a client-context flag (`isTrueLocalTarget`).
  - VisionOfImpossible — multi-guess (needs a target-LIST context + guess-match reduction).
  - ClandestineObservation — needs a targeting-count query port.
  - Legacy / Reincarnation — give-power (needs a `GrantPower` effect + a config-power port; engine Power refs).
  - PersonalBeacons — spawns beacon objects (power-local) + the robot forceCorrupt reveal (already a known bug-fix from v1).
  - Chaining — a `ChainDecorator` POCO wrapping any decision + a `ChainStateCarrier`; folds in the old PCChainer/PCConcentrated/PCReparent components.

**Phase 3 (NGO wiring) — IN PROGRESS via in-place delegation (safer than the prefab-holder rewire).** Rather than re-authoring 22 prefabs onto a generic `PowerHolder`, each existing `Power : NetworkBehaviour` keeps its identity/prefab/spawn and just **delegates its server-effect body** to its pure decision + the executor registry. No prefab/spawn/attribution churn, network-safe, verifiable by the existing PlayMode goldens.

Infra built + proven (all compile-clean, EditMode 362/362, PlayMode power fixtures 20/20):
- `PowerDispatcherHost` — boot registry, reflection-discovers every `IEffectExecutor` in the Game assembly.
- `Power.RunDecisionEffects(decision, ctx, state?)` — server-only helper: `Decide` → dispatch. Uses decrement stays in each power's own use flow.
- `Power.Roster` → `CharacterManagerRoster` (live `IRosterView` over CharacterManager; lossless int-slot round-trip incl. fake ids = ulong.MaxValue−n).
- `Power.SelfState` → `PowerStateAdapter` (live `IPowerStateResolver` over the power itself; a state-carrier power implements its narrow ports).
- Built the 5 previously-missing executors + ports: `GrantLegacyPower`/`ILegacyGrant`, `SetPassiveBroadcast`/`ISetPassiveState`, `GrantRolePowers`/`IGrantRolePowers`, `RegisterInkTarget`/`IInkTargetRegister`, `DiscoveredAdd`/`IDiscoveredAdd`. Added `ReflectionHelper.GetPrivateField` for goldens.
- **KEY FIX**: `RunDecisionEffects` originally passed the state resolver only to the `EffectRuntime` (state-WRITE effects). Decisions that READ `ctx.State<TPort>()` (Clandestine/BoundByInk/CardsShuffling) also need it in the `PowerContext` — the caller now threads `SelfState` into BOTH the context ctor (`state:`) and the runtime.

**17 / 22 powers wired + golden-verified** (10 new goldens this session), covering every integration shape:
- Passive, no roster/state: `PCorruptionParanoia`, `PAutoCorruption`.
- Passive + live roster: `PCorruptionInsight`, `PCorruptionKnowledge`, `PEyeOfTheVoid` (config-in-code chat id).
- Passive, reparent trigger: `PInfiniteMessage`.
- Active + roster (server RPC): `PTruthChains` (both branches), `PHighPriorityBounty` (robot branch; client-side NewTargeting stays put).
- Active char+role (server RPC): `PBlessing`, `PChainedByTheShadows`.
- Active + power-local WRITE state: `POmniscience` (IHackTargetState), `PCorruptingMark` (ILastCorrupted + ICorruptionEvents).
- Active + power-local READ+WRITE state: `PBoundByInk` (IInkChatState read + IInkTargetRegister write), `PCardsShuffling` (ICardsShufflingGuess read + IDiscoveredAdd write), `PClandestineObservation` (IClandestineReport read).
- Give-power: `PLegacy` (ILegacyGrant), `PReincarnation` (ISetPassiveState + IGrantRolePowers).

**Remaining 5 powers — HELD for Poyo + a real 2-build playtest.** These genuinely cannot be verified solo:
- `PCursedVision`, `PEmbraceOfShadows` — v1-pilot shape: their effect logic runs in a **client-side selection callback** (no ServerRpc; mixed ServerRpc + server-only calls). Any clean wiring (IsServer-guarded delegate, or adding a ServerRpc) shifts the server/client boundary — a behaviour change only a playtest can validate.
- `PLackOfAffection` — runs on the **contacted target's client** (`isTrueLocalTarget`); needs a second real client to exercise.
- `PVisionOfTheImpossible` — multi-guess over a **target LIST** (client-callback dispatch) + `IVisionGuesses`.
- `PPersonalBeacons` — **spawns beacon GameObjects** (engine instantiation) + the robot forceCorrupt reveal.

The in-place delegation pattern + all shared infra are proven; these 5 are owner-gated on a real 2-build playtest (Claude does not playtest — [[feedback_no_playtest_by_claude]]).

**Phase 4 — delete the old inline `Power`/`PowerResolver`/`PowerComponent` path + absence proof + full green gate.**

## Guardrails
- Keep every step compiling + tests green (poll `read_console` after each change; `run_tests` between phases).
- `noEngineReferences` on Domain is the purity enforcement — never weaken it.
- Server authority: only the server writes NVs / runs decisions that mutate; `Decide` is pure and side-effect-free (it returns intentions).
- Reuse the golden-trace harness concept for the executor/dispatch realization (Murat's gap: goldens prove intention, add per-effect realization tests).
