# Refactor Architecture — Despaghettification (decoupling pass)

Branch: `refactor-despaghetti` (from `dev-refactor`, after the Epic 5 squash-merge `28639d6`). Never `Dev` directly; merge to `Dev` once, at the very end. Companion of `refactor-architecture-poco.md` and `refactor-architecture-desingleton.md` — same NFRs, same safety net.

Driver: Poyo's goal restated (2026-06-11) — *de-spaghettify the whole game so it is clean, simple to modify, and maintainable with real unit tests*, across the most important systems, ideally everything. This is the "THE BIG ONE" item Poyo flagged in `deferred-work.md`: the POCO + de-singleton work was **enabling scaffolding**, not the decoupling itself.

> **Status:** DRAFT for Poyo's validation. No code yet. Execution starts only after sign-off, story by story.

---

## 1. What "spaghetti" means here (measured, not guessed)

Reconnaissance on `Assets/Scripts` (2026-06-11):

- **24 static singletons** (`static instance`/`Instance`), **29 `NetworkBehaviour`s**.
- **Service Locator everywhere**: a consumer grabs `GameManager.instance` / `CharacterManager.instance` (now `For(nm)`) to reach what it needs. The dependency is **implicit** — nothing in a class's signature says what it actually needs.
- **Two God Objects**, by caller fan-in (number of non-test files referencing the type):

| Type | Fan-in | LOC | Role today |
|---|---|---|---|
| **GameManager** | 72 | 555 | game loop + state dict + RPC dispatch + **pass-through to characterManager/gameInfoRevealer/chainingManager/bars** + day count |
| **CharacterManager** | 37 | 509 | character registry/query + spawn + `GetSafeRpcTarget`/bot flow + reveal helpers |
| ChatManager | 17 | 216 | NetworkBehaviour singleton |
| SelectionFlowService | 16 | 253 | UI selection |
| RoleTargetSystem | 16 | — | targeting |
| BoardManager | 13 | 257 | board/despawn |

- Largest systems by file count: `Characters/` (52), `UI/` (48), `Board/` (33), `GameLogic/` (22), `Domain/` (15, the clean POCO core already built).

**The single biggest finding** — what callers actually use on `GameManager`:

| Member used via `GameManager.instance`/`For()` | Hits |
|---|---|
| `.characterManager` | **78** |
| `.gameInfoRevealer` | **31** |
| `.onGameStarted` | 10 |
| `.GetGameStates` / `.GetGameState` / `.GetGameStateIndex` | ~13 |
| `.currentGameStateIndex` | 7 |
| `.charactersBar` / `.powersBar` | 4 |
| `.currentDay` / `.hasGameStarted` | 3 |

→ **GameManager is mostly a locator hub.** ~109 of its uses are just "give me the *other* manager." Inject those directly and the God Object's fan-in collapses to its real surface: the game-loop/state machine.

## 2. Target architecture (the clean shape)

The end state — each system **declares** its dependencies and depends on **narrow interfaces**, not on whole managers or on a global lookup.

1. **Dependency injection over Service Locator.** A consumer receives what it needs through `[SerializeField]` (scene/prefab-wired) or an `Initialize(...)` call (runtime-spawned objects, e.g. powers). The model already exists here — `GameState` receives an injected `gameManager` in `GameManager.SetupGameStates`. Generalise it; delete the `instance`/`For(nm)` lookups from consumers.
2. **Narrow role interfaces, not God Objects.** Split each God Object's surface into intent-named interfaces a caller can depend on à la carte:
   - `IGameLoop` — `NextGameState`/`PreviousGameState`/`SetGameState`/`currentDay`/`hasGameStarted`/`onGameStarted`/`onNewDayPassed`.
   - `IGameStateQuery` — `GetGameState`/`GetGameStates`/`GetGameStateIndex`/`GetClosestPreviousState`/`currentGameStateIndex` (read).
   - `ICharacterQuery` — read-side character lookups.
   - `ICharacterCommand` — spawn/mutate (server-authority).
   - `IRevealService` — the `GameInfoRevealer` surface.
   - (more emerge per system: `IChatService`, `IBoardService`, …)
   A class implementing several is fine; the point is **callers see only the slice they use**.
3. **Thin adapters, fat POCOs.** `MonoBehaviour`/`NetworkBehaviour` shrink to adapters: lifecycle, RPC plumbing (`GetSafeRpcTarget`, `NetworkVariable`), Unity glue. Decision logic moves into POCOs/`Domain` with real EditMode unit tests — continuing the Wave 1–4 pattern.
4. **One composition root.** A single, explicit place wires the graph (scene-placed managers via inspector references + an init pass for spawned objects), instead of every object self-resolving from a static. No new DI framework — Unity's `[SerializeField]` + a small `Initialize` convention is the container.

NFR5 is sacred throughout: `GetSafeRpcTarget` / `IsLocalOrSimulated` / the `clientId >= 100` gateway are never edited, only relocated/wrapped. Server authority strict. Async = UniTask. Audio = FMOD.

## 3. Principles / invariants (non-negotiable)

- **Behaviour-preserving, every commit.** The golden/differential masters (Waves 1–4), the wire-format guard (5.1), the leaf-POCO guard (5.2), and the new `MultiClientGameFixture` (5.0) are the net. A golden that *moves* means hidden behaviour was disturbed — stop and investigate, never "re-bless".
- **Shippable at every commit (NFR7).** No big-bang. Strangler pattern: introduce the interface + new wiring alongside the old static, migrate call sites in batches the compiler enumerates (delete the symbol → CS0117/CS0103 → reroute), then remove the old path. Never leave both a clean core and the God Object as permanent debt.
- **Real unit tests are a deliverable, not a side effect.** Each extracted POCO ships EditMode tests. Each de-coupled `NetworkBehaviour` becomes reachable by the multi-client fixture without booting the whole scene.
- **Verify, don't force.** A reference with no injection context (static utility, serializable value object, pure UI leaf bound to the local player) may legitimately stay on a façade — record why, don't contort the design (the rule that governed 5.0c/5.0d).
- **Network-critical work gated by the fixture.** Anything that changes replication/ownership rides `MultiClientGameFixture` + a `# REVIEW-REQUIRED` gds-code-review.

## 4. Strategy — Strangler + interface extraction + injection

Per God Object / system, the repeatable recipe:

1. **Characterize** the public surface actually used (the histogram above is step 0 for GameManager). Group members into intent interfaces.
2. **Introduce the interface(s)**; have the existing manager implement them (zero behaviour change).
3. **Wire injection** at the composition root: scene managers expose themselves via `[SerializeField]` to consumers; spawned objects get an `Initialize(deps)` call where they are already created (e.g. `CharacterManager` spawns a `Character` → injects there).
4. **Migrate call sites in batches** by destructive deletion of the static accessor for that slice, letting the compiler list every site; reroute to the injected interface. Gate each batch at the full suite baseline.
5. **Remove** the dead static/locator path; add a static-absence guard test (like the leaf-POCO guard) so it cannot creep back.

## 5. Epic breakdown (prioritised — highest value first)

> Order maximises value-per-risk: kill the locator hub first (biggest, unblocks everything), then the second God Object, then the remaining replicated singletons, then per-system logic extraction, UI last (lowest architectural value, highest churn).

- **Epic D0 — Composition-root + injection seam convention.** Establish *the* pattern: how a scene manager is injected (`[SerializeField]`), how a spawned object is initialised (`Initialize`), where the graph is wired, and the static-absence guard test scaffold. Pick one *small* real consumer (e.g. a single power or a board component) as the worked example. Deliverable: a documented, tested seam other epics copy. Low risk, unblocks all.
- **Epic D1 — Dismantle GameManager-as-locator.** Inject `CharacterManager` (78), `GameInfoRevealer` (31), `ChainingManager`, `CharactersBar`/`PowersBar` **directly** into their consumers; stop routing through `GameManager.instance`. This alone removes the large majority of GameManager's fan-in. Pure reference-resolution change, low network risk, golden-gated. **Biggest single maintainability win.**
- **Epic D2 — Narrow the GameManager game-loop surface.** Extract `IGameLoop` + `IGameStateQuery`; consumers depend on the interface, injected. GameManager keeps owning the `NetworkVariable` + RPC dispatch (network adapter) but is no longer a grab-bag. (The parked 5.3/5.4 index-ownership work folds in here *if* it still earns its risk once the surface is narrow — decided then, not now.)
- **Epic D3 — CharacterManager split.** `ICharacterQuery` (reads) vs `ICharacterCommand` (server mutations); inject; keep `GetSafeRpcTarget`/bot flow verbatim in the adapter (NFR5). Gated by the multi-client fixture.
- **Epic D4 — Remaining replicated singletons → injection.** ChatManager, BoardManager, RoleTargetSystem, StatesCanvas, MessageManager, GameAudioManager, ChainingManager, LobbyPlayerInfoHolder — the 8 NetworkBehaviour singletons the de-singleton pass deferred. Same recipe, on demand / by fan-in.
- **Epic D5 — Per-system logic → POCO + unit tests.** Powers, Board, Chat, Focus, Tooltip: push remaining decision logic out of MonoBehaviours into `Domain`/POCOs with EditMode tests; thin the adapters.
- **Epic D6 — UI layer (48 files).** Last. Inherently bound to the local player; lowest architectural payoff, highest churn. Reroute off managers to injected view-models where it helps; otherwise leave on façades by the verify-don't-force rule.

Each epic = a sprint of small, golden-gated, individually-shippable stories, authored via `gds-create-story` and executed via `gds-dev-story`, exactly like Waves 1–5.

## 6. Risks & mitigations

| Risk | Mitigation |
|---|---|
| A reroute silently changes which instance a call hits (multi-NM) | Production has one NetworkManager → injection of the scene instance is identical; the multi-client fixture proves it for the replicated case |
| Init/lifecycle order: an injected ref read before it is wired | Composition root wires before first use; boot smoke-test (start→finish a game, no exception) per epic; `Initialize` is called at spawn, before the object acts |
| Golden moves during a "behaviour-preserving" reroute | That is the net working — stop, the reroute disturbed hidden behaviour |
| Scope creep into redesigning mechanics | Poyo owns design; this refactor preserves behaviour only ([[user-role-dev-not-gd]]) |
| UI churn for little gain | UI is Epic D6, last, and partially opt-out by the verify-don't-force rule |
| Half-done God Object = worse than before (both core + locator) | Strangler discipline: each epic ends with the old path *removed* + a static-absence guard, never parked half-migrated |

## 7. Definition of done (whole effort)

No God Object remains a grab-bag; consumers depend on narrow injected interfaces, not `instance`/`For(nm)` locators; decision logic lives in tested POCOs; the static-absence guards forbid regression; full EditMode + PlayMode suite green; boot smoke-test green; merged to `Dev` once, at the end.

## 8. First epic detail — D0 seam (to flesh out on sign-off)

On Poyo's go, story 1 of D0 will: (a) pick the worked-example consumer, (b) define the `Initialize`/`[SerializeField]` convention + where the composition root lives (likely the GameScene boot path + `CharacterManager`'s spawn path for runtime objects), (c) add the static-absence guard test scaffold, (d) prove it on the example with the suite green. Then D1 scales it to the 78 `characterManager` sites.

---

**Source of truth for execution:** this doc + `sprint-status.yaml`. Relates to [[project-poco-loop-progress]], [[project-desingleton-plan]], [[project-refactor-branch]], [[feedback-refactor-workflow]].
