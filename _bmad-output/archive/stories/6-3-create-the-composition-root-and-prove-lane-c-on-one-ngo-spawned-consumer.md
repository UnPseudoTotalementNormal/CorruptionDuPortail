# Story 6.3: Create the CompositionRoot + prove lane C on one NGO-spawned consumer

Status: review

## Story

As a developer,
I want the `CompositionRoot` (the one surviving project static) created and lane C proven on one real network-spawned consumer,
so that Epic 7 can reach the client-side replicas (`Character`, powers) that no project code instantiates — without improvising the pattern mid-batch.

This closes Epic 6 (D0) and is the **hard prerequisite of Epic 7**. Worked example chosen at authoring time: **`PTruthChains`** (see Dev Notes — exactly one hub-hop, NGO-spawned, server-side RPC body).

## Acceptance Criteria

1. **CompositionRoot exists, typed, scene-placed.** A `CompositionRoot` MonoBehaviour in GameScene exposes **typed accessors** to the scene managers (no `Dictionary<Type, object>` bag). Its own manager references are lane A `[SerializeField]` fields wired in GameScene and covered by the SceneWiringGuard (6.2 — add `CompositionRoot` to the shared registry / its field types to `InjectedManagerTypes`).
2. **Per-NM aware, Awake-registered.** It registers per-`NetworkManager` at `Awake` (never in `OnNetworkSpawn` — cross-object spawn order is not guaranteed), and `CompositionRoot.For(NetworkManager)` resolves the graph for that NM by **delegating to the existing 5.0c/5.0d per-NM registries** (`GameManager.For(nm)`, `CharacterManager.For(nm)`). The `MultiClientGameFixture` stays green with two in-process NMs.
3. **Lane C proven on `PTruthChains`.** Its single hub-hop (`GameManager.For(NetworkManager).characterManager` in `OnCardClickedRpc`) is replaced by a field resolved **once in `OnNetworkSpawn`** via `CompositionRoot.For(NetworkManager)`, with an `Assert.IsNotNull` and **no locator fallback**. No other static lookup is introduced or removed in this story (its `RoleTargetSystem.instance` / `ChainingManager.instance` / `ChatManager.instance` / `LobbyPlayerInfoHolder.instance` / `SelectionFlowService.instance` reads are Epic 10's scope — leave them).
4. **Guard extended: lane C whitelist + For-coverage.** `DiSeamNoLocatorGuardTests` is extended so that for migrated consumers: (a) `GameManager.For(` / `CharacterManager.For(` are **forbidden** alongside `.instance` (today the guard only catches `.instance` — a `For()` hub-hop would slip through); (b) `CompositionRoot` may appear **only inside `OnNetworkSpawn`**. Both new rules proven to bite on synthetic violations. `PTruthChains` is appended to the shared registry.
5. **Docs match what shipped.** Architecture doc §3 lane C + §4 root spec updated if implementation details diverged (e.g. accessor names).
6. **Green gate.** Full suite + `MultiClientGameFixture` pass unchanged at baseline; boot smoke-test green (start a game, use the truth-chains power path if practical, no exception). NFR5 untouched (`PTruthChains.OnCardClickedRpc` body semantics byte-identical — only the manager *access path* changes).

## Tasks / Subtasks

- [x] **Task 1: Implement `CompositionRoot`** (AC: 1, 2)
  - [x] New file `Assets/Scripts/GameLogic/CompositionRoot.cs` (GameLogic owns the graph; `Game` asmdef — single project assembly). MonoBehaviour, NOT a NetworkBehaviour.
  - [x] Typed accessors, first slice only: `public CharacterManager CharacterManager => ...` and `public GameManager GameManager => ...`.
  - [x] Per-NM registry `s_byNetworkManager` + Awake claim + value-scan `OnDestroy` unregister + `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset, mirroring `GameManager.For`.
  - [x] **Design choice recorded (see Completion Notes / doc §4):** `For(nm)` returns a lightweight `CompositionRoot.Services` value resolver delegating to `CharacterManager.For(nm)` / `GameManager.For(nm)` — a root *instance* is not required for resolution, so the fixture's second NM resolves with zero fixture changes. Root binds to `NetworkManager.Singleton` at Awake.
  - [x] Placed `CompositionRoot` GameObject in GameScene; wired `[SerializeField]` gameManager → GameManager (id 100110), characterManager → CharacterManager (id 100592) via MCP `manage_components`; verified by read-back; scene saved.
- [x] **Task 2: Reroute `PTruthChains` (lane C worked example)** (AC: 3)
  - [x] `OnNetworkSpawn` (after `base.OnNetworkSpawn()`): `_characterManager = CompositionRoot.For(NetworkManager).CharacterManager; Assert.IsNotNull(...)`. Field `private CharacterManager _characterManager;` (NOT serialized).
  - [x] `OnCardClickedRpc`: replaced the `GameManager.For(NetworkManager).characterManager.GetCharacter(...)` hub-hop with `_characterManager.GetCharacter(...)`. Nothing else changed — RoleTargetSystem/faction/chat lines byte-identical.
  - [x] Server-side RPC: `OnNetworkSpawn` ran there too; resolved instance identical to the old hub-hop — behaviour-preserving by construction.
- [x] **Task 3: Extend guard #1** (AC: 4)
  - [x] `ForbiddenLocators` += `"GameManager.For("`, `"CharacterManager.For("`.
  - [x] New rule + helper `CompositionRootUsedOutsideOnNetworkSpawn` (brace-match the `OnNetworkSpawn(` span; any `CompositionRoot` outside = fail). Mechanism recorded in code comment.
  - [x] Synthetic bite tests for both rules (`Guard_Bites_OnForHubHopAndCompositionRootOutsideOnNetworkSpawn`) + per-consumer test `MigratedConsumers_OnlyReferenceCompositionRootInsideOnNetworkSpawn`.
  - [x] `DiSeamMigratedConsumers`: appended `PTruthChains` to `All` (both guards) + `CharacterManager` to `InjectedManagerTypes`; added new `SceneWiredOnly = { CompositionRoot }` (guard #2 only — NOT the no-locator set, per the caution). SceneWiringGuard now iterates `All.Concat(SceneWiredOnly)`.
- [x] **Task 4: Fixture + suite gate** (AC: 2, 6)
  - [x] `read_console` after each change — clean.
  - [x] New `CompositionRootResolutionTests : MultiClientGameFixture` — two-NM resolution proven (host + client, per-NM isolation). PASS.
  - [x] Full EditMode **162/162** + PlayMode **146/146** green (baseline 160/145 + 2 EM guard tests + 1 PM fixture test). Boot smoke via `manage_editor` play — zero errors, no CompositionRoot assert.
  - [x] sprint-status `6-3 → review` (epic-6 stays in-progress — 6.2 still in review). Commit pending.
- [x] **Task 5: Sync the architecture doc** (AC: 5)
  - [x] §4 updated with the shipped shape (Services value resolver, concrete first-slice accessors, guard-coverage split).

## Dev Notes

### Why `PTruthChains` (candidate decision — AC of epics.md asked for it recorded here)

Grep recon (2026-06-11): powers reach managers via `GameManager.For(NetworkManager)` / `CharacterManager.For(...)` — 127 occurrences across 34 files, powers dominating. `PTruthChains` (`Assets/Scripts/Characters/Powers/PTruthChains.cs`, 86 lines) is the minimum-risk representative: **exactly one** hub-hop (`GameManager.For(NetworkManager).characterManager.GetCharacter(...)` at line 35, inside `[Rpc(SendTo.Server)] OnCardClickedRpc`), already overrides `OnNetworkSpawn` (line 14 — the lane C hook exists), and is a real NGO-spawned `Power` (spawned server-side at `CharacterManager.cs:444` `Instantiate(_power, null)` + NGO spawn; client replicas instantiated by NGO). Its other statics (RoleTargetSystem/ChainingManager/ChatManager/LobbyPlayerInfoHolder/SelectionFlowService) are out of scope here (Epic 10) — the guard's forbidden list does not include them yet, so appending PTruthChains to the registry is safe.

### The For() blind spot (why AC 4a exists)

`DiSeamNoLocatorGuardTests.ForbiddenLocators` today = `{ "GameManager.instance", "CharacterManager.instance" }` — written when 6.1's example used `.instance`. The powers population uses `For(` — without AC 4a, Epic 7 migrations could "pass" the guard while still hub-hopping. Closing this hole NOW is why 6.3 blocks Epic 7.

### Root resolution design (the one subtle decision)

Production: one NM, one scene root, `For(nm)` == the scene root, accessors delegate to `*Manager.For(nm)` — strictly identical resolution to today's locator (behaviour-preserving by construction). Fixture: the second NM has no scene-placed root; `For(nm)` must still answer. Recommended: make the static `For(nm)` build/cache a lightweight per-NM resolver delegating to `GameManager.For(nm)`/`CharacterManager.For(nm)` regardless of whether a scene root instance exists for that NM; the scene root instance is the production fast-path + the lane A wiring carrier. This keeps the fixture green with zero fixture changes. Whatever is chosen: record it, prove it with the fixture run, sync the doc (Task 5).

### What NOT to do

- Do NOT make `CompositionRoot` a NetworkBehaviour (spawn-order trap; the whole point is Awake-time availability).
- Do NOT pre-register every manager type — first slice only (GameManager, CharacterManager). The root grows with the track.
- Do NOT touch the other statics in `PTruthChains` (Epic 10) or "fix" anything else in the file.
- Do NOT add a locator fallback in the consumer (`?? GameManager.For(...)` defeats the migration).
- NFR5: `PTruthChains` has no `GetSafeRpcTarget`/`clientId >= 100` code of its own; nothing to relocate. The RPC attribute/semantics stay untouched.

### Staleness check (authored 2026-06-11)

Authored right after 6.1, before 6.2 lands. If 6.2 changed the shared-registry naming, adapt Task 3 to the actual symbol. Re-verify `PTruthChains.cs` line numbers at dev time (file may have moved if Epic 4/5 follow-ups touched powers).

### Project Structure Notes

- New: `Assets/Scripts/GameLogic/CompositionRoot.cs`; GameScene gains the root GameObject (+ wiring).
- Modified: `Assets/Scripts/Characters/Powers/PTruthChains.cs`, `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs`, shared registry file (6.2), `Assets/Scenes/GameScene.unity`, architecture doc (if diverged).
- Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Networked init in `OnNetworkSpawn`, never `Awake` — applies to the CONSUMER side (lane C resolves there); the ROOT is deliberately a plain MonoBehaviour registering at `Awake` (non-networked init — allowed by the rule's escape hatch).
- `NetworkBehaviour` exposes deps via `Initialize(...)`/`[SerializeField]` — never `FindObjectOfType`/singleton lookup in `OnNetworkSpawn` (project-context.md:157). Lane C is the codified, guard-fenced exception: ONE root, `OnNetworkSpawn`-only, whitelisted — not a return of ad-hoc lookups.
- Domain reload disabled → `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` static reset on the root's registry (model: `GameManager.cs:51-59`).
- `NetworkManager.Singleton == null` possible in `OnDestroy` during shutdown — unregister by value-scan (model: `GameManager.UnregisterFromRegistry`, GameManager.cs:183-187).
- Server authority / UniTask / FMOD rules untouched by this story.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3 lane C, §4 root, §5 guards] — the spec.
- [Source: _bmad-output/planning-artifacts/epics.md#Story 6.3].
- [Source: Assets/Scripts/Characters/Powers/PTruthChains.cs:14,35] — the worked example: `OnNetworkSpawn` hook + the single hub-hop.
- [Source: Assets/Scripts/GameLogic/GameManager.cs:29-59,134-187] — `instance` façade + per-NM registry + SubsystemRegistration reset + value-scan unregister (the pattern the root mirrors/delegates to).
- [Source: Assets/Scripts/Characters/CharacterManager.cs:444] — power spawn site (lane B counterpart, used by Epic 7.1).
- [Source: Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs] — guard to extend (ForbiddenLocators + OnNetworkSpawn whitelist).
- [Source: Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs] — the two-NM proof harness.
- [Source: _bmad-output/implementation-artifacts/6-1-*.md] — previous story: seam conventions, serialized-field safety, guard-bite precedent.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow).

### Debug Log References

- EditMode guard categories (DiSeamGuard + SceneWiringGuard): 7/7 PASS.
- PlayMode `CompositionRootResolutionTests.For_ResolvesPerNetworkManager_GraphForBothClients`: PASS.
- Full EditMode: 162/162 PASS. Full PlayMode: 146/146 PASS.
- Boot smoke (Play on GameScene): 0 errors, no CompositionRoot assert (wiring confirmed at Awake).

### Completion Notes List

- **Resolution design (the one subtle decision, recorded).** `CompositionRoot.For(NetworkManager)` returns a lightweight `CompositionRoot.Services` **`readonly struct`** value resolver — NOT the MonoBehaviour. The resolver holds only the NM and delegates each typed accessor to the per-NM registries (`CharacterManager.For(nm)` / `GameManager.For(nm)`). Consequence: a scene-placed root *instance* is **not required** for resolution, so the `MultiClientGameFixture`'s second in-process NM (no scene root) resolves its own graph with **zero fixture changes**. The scene `CompositionRoot` MonoBehaviour is the production lane-A wiring carrier (SceneWiringGuard target) + Awake-registered per NM. This honours Task 1's "the static `For` can answer for any NM the manager registries know" while side-stepping the MonoBehaviour-can't-be-`new`'d / Awake-assert trap a MonoBehaviour-returning `For` would hit for the fixture NMs.
- **First slice = concrete accessors** `Services.CharacterManager` / `Services.GameManager`. The §3 lane C sketch's interface names (`CharacterQuery`, `GameLoop`) arrive with Epics 8/9; accessors narrow then. No accessors pre-built for not-yet-needed managers.
- **Guard coverage split (AC4 caution honoured).** `CompositionRoot` legitimately calls `For(`, so it is in the NEW `DiSeamMigratedConsumers.SceneWiredOnly` (guard #2 wiring-check only), **never** in `All`. `PTruthChains` (a true migrated lane C consumer) is in `All` → both guards. Guard #1 gained `GameManager.For(` / `CharacterManager.For(` (the For() hub-hop blind spot, AC4a) and the "`CompositionRoot` only inside `OnNetworkSpawn`" rule (AC4b, brace-matched on the `OnNetworkSpawn(` signature token to avoid prose false-matches). Both new rules have synthetic bite tests.
- **NFR5 / behaviour-preserving.** `PTruthChains.OnCardClickedRpc` body semantics byte-identical — only the manager *access path* changed (`GameManager.For(NetworkManager).characterManager` → injected `_characterManager`, same instance for the server NM). No `GetSafeRpcTarget`/`clientId>=100` code in this file to relocate. Goldens unchanged (full suites green at baseline).
- **`Character`/`CharacterManager` reachable without `using Characters;`** in `PTruthChains` — its namespace `Characters.Powers` is nested under the enclosing `Characters` namespace.
- **Unblocks Epic 7** — the lane C pattern + the For() hub-hop guard hole are both closed.

### File List

- **Added:** `Assets/Scripts/GameLogic/CompositionRoot.cs`
- **Added:** `Assets/Scripts/Tests/PlayMode/Desingleton/CompositionRootResolutionTests.cs`
- **Modified:** `Assets/Scripts/Characters/Powers/PTruthChains.cs`
- **Modified:** `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs`
- **Modified:** `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs`
- **Modified:** `Assets/Scripts/Tests/Editor/SceneWiringGuardTests.cs`
- **Modified:** `Assets/Scenes/GameScene.unity` (CompositionRoot GameObject + lane A wiring)
- **Modified:** `_bmad-output/refactor-architecture-despaghetti.md` (§4 shipped shape)
- **Modified:** `_bmad-output/implementation-artifacts/sprint-status.yaml` (6-3 → review)

### Change Log

- 2026-06-12 — Story 6.3 implemented: `CompositionRoot` (the one surviving static) + lane C proven on `PTruthChains`; guard #1 extended (For() hub-hop + OnNetworkSpawn whitelist); `SceneWiredOnly` registry split. EM 162 / PM 146 green + boot smoke. Status → review.
