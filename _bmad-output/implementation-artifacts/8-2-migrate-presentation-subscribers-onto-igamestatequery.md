# Story 8.2: Migrate the presentation subscribers onto IGameStateQuery

Status: review

## Story

As a developer,
I want the ~10 presentation components subscribing to `currentGameStateIndex.OnValueChanged` (RoomFog, BoardCameraManager, AwakeningLight, CharacterAwakenTimer, recap UIs…) to depend on an injected `IGameStateQuery`,
so that presentation sees only the read slice — the `LightManager` template applied to its whole family.

## Acceptance Criteria

1. **The family** (recon candidates: `RoomFog` 4 hits, `BoardCameraManager` 3, `AwakeningLight` 2, `CharacterAwakenTimer` 3, `AwakeningRecapMessages` 1, `AwakeningRecapCorruption` 1, `AnonymeMessageButton` 5, `MeIconCard` 4, `SkipButton` 1, `CorruptedCardText` 3, others surfaced by grep at dev time) each: lane A `[SerializeField]` concrete field, internally narrowed (`private IGameStateQuery Query => gameManager;`), subscription code UNCHANGED — only the source resolution changes (locator → field). Subscription stays in its current lifecycle hook per file (`Start`/`OnEnable` as today — do not normalize).
2. **`LightManager` updated to the slice** (it holds a concrete `GameManager` from 6.1): add the internal narrowing property; calls go through it. No scene rewiring (field unchanged).
3. **2.11a sequence golden passes unchanged** — subscription timing must not shift (the load-bearing `OnEnd → NV write → OnStart` order is pinned).
4. **Every instance MCP-wired + read-back verified** (D-NFR3); types appended to the shared registry; both guards green.
5. **Batched, gated:** suite unchanged per batch; boot smoke (light/fog/camera react to a state change) green.

## Tasks / Subtasks

- [x] **Task 1:** Re-grepped the IGameStateQuery-surface consumers; froze the batch list (below). Scene-vs-prefab determined via `find_gameobjects by_component` (the 7.4 lane-A-impossible check).
- [x] **Task 2:** Migrated the in-scope files (6.1 template: append `[SerializeField] GameManager` field, `private IGameStateQuery Query => gameManager` narrowing, reroute reads, wire, verify). Subscriptions left in their current hook (BoardCameraManager `Start`). No pre-existing unsubscribe leak fixed (Epic 11.4 owns that). Mixed + prefab consumers deferred per Poyo's decisions.
- [x] **Task 3:** Registry (`All += BoardCameraManager, CharactersBar`); both guards green; 2.11a golden + full suite bit-identical per the gate.
- [x] **Task 4:** Boot smoke green (0 errors, no assert); sprint-status updated; commit pending.

## Dev Notes

- This is 6.1 × 10 — pure template application. The only judgment call per file: does it ALSO use command surface (`NextGameState` etc.)? Then it waits for 8.3 or gets both deps (record).
- The internal-narrowing property (`=> gameManager`) keeps Unity-serializable concrete fields while making the CODE depend on the slice — D-NFR6's compromise, spec §3 lane A.
- Staleness: candidate list from 2026-06-11 grep; re-derive (7.x reroutes may have touched these files).

### Project Structure Notes

- Modified: ~10 presentation files, GameScene wiring, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Serialized-field safety; split canvases / no per-frame work introduced; `OnNetworkDespawn` unsubscribe rule NOT applied here (behaviour-preserving — Epic 11.4).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.5 (subscriptions stay), §3 lane A] / [epics.md#Story 8.2]
- [Source: Assets/Scripts/Board/LightManager.cs] — the template + first slice-narrowing subject.
- [Source: Assets/Scripts/Tests/PlayMode (2.11a sequence golden)] — the timing net.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Final compile: 0 errors / 0 warnings. Gate: EditMode **162/162**, PlayMode **146/146** (baseline, 2.11a sequence golden unchanged). Boot smoke (`manage_editor` play on GameScene): **0 errors, no assert** (BoardCameraManager/LightManager gameManager wired).
- MCP scene wiring: GameManager component instanceID = **72492** (GO 72488). Wired `gameManager` on BoardCameraManager (GO 72570 / comp 72574) and CharactersBar (GO 72330 / comp 72334) → 72492; read-back verified both (D-NFR3).

### Completion Notes List

**Task 1 — frozen batch (re-grep of IGameStateQuery-surface consumers via the locator):**

| File | Surface | Scene? | Verdict |
|---|---|---|---|
| `LightManager` | query (already on field, 6.1) | scene | **Migrated** — AC2 narrowing property added |
| `BoardCameraManager` | query only (OnValueChanged + GetGameState) | scene (72570) | **Migrated** — lane A |
| `CharactersBar` | query only (GetGameStates) | scene (72330) | **Migrated** — lane A |
| `RoomFog` | query + `onGameStarted` | scene | **Deferred → 8.3** (mixed) |
| `AnonymeMessageButton` | query + `onGameStarted` | scene | **Deferred → 8.3** (mixed) |
| `RobotBoardInfo` | query + `onGameStarted` | scene | **Deferred → 8.3** (mixed) |
| `CorruptionBoardInfo` | query + `onGameStarted` | scene | **Deferred → 8.3** (mixed) |
| `CharacterAwakenTimer` | query (GetGameStates) | **prefab-only** (find=0) | **Deferred** — lane A impossible |
| `RoleAttributionSettingTab` | query (GetGameStates) | **prefab-only** | **Deferred** — lane A impossible |
| `RoleAttributionSettingObject` | query (GetGameStates) | **prefab-only** | **Deferred** — lane A impossible |
| `RoleTargetSystem` | query (GetGameStates via For) | system, not presentation | **Deferred** — Epic 10 injects it |
| `PowerManager` | query + command | system, mixed | **Deferred** — mixed, Epic 8/later |

- **Decisions (Poyo, at dev time):** MIXED files → defer to 8.3 (don't half-migrate / double-touch scene wiring); PREFAB-only files → defer to a later UI/prefab pass (lane A physically impossible — prefab can't reference a scene object). Both recorded in deferred-work.md.
- **AC1/AC2:** the three migrated files depend on the narrow read slice via `private IGameStateQuery Query => gameManager;` over a concrete lane-A `[SerializeField] GameManager` (D-NFR6 — Unity can't serialize an interface). Subscription code unchanged (BoardCameraManager stays in `Start`); CharactersBar kept its null-tolerant `gameManager != null` guard (was `GameManager.instance != null`).
- **AC3:** 2.11a sequence golden (GameLoopTransitionOrderingTests) passes — subscription timing unchanged (same hook, same order; the source merely resolves the SAME scene GameManager via the field instead of the static).
- **AC4:** registry `All += BoardCameraManager, CharactersBar`; both guards green (SceneWiringGuard verifies the wiring, DiSeamNoLocatorGuard verifies locator-free). `GameManager` already in `InjectedManagerTypes` (6.3), so the new fields are wiring-checked automatically.
- **AC5:** suite bit-identical; boot smoke green.

### File List

**Modified (production):**
- `Assets/Scripts/Board/LightManager.cs` — added `IGameStateQuery Query => gameManager` narrowing; reads route through `Query` (AC2).
- `Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs` — lane-A `[SerializeField] GameManager` + `Query` narrowing + Awake assert; rerouted 3 `GameManager.instance` query hops.
- `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs` — lane-A `[SerializeField] GameManager` + `Query` narrowing (null-tolerant); rerouted the `GameManager.instance.GetGameStates` hop.
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` — `All += BoardCameraManager, CharactersBar` (+ `using Board.BoardCameraSystem`).

**Scene:**
- `Assets/Scenes/GameScene.unity` — wired `gameManager` on BoardCameraManager + CharactersBar to the scene GameManager.

**Docs:**
- `_bmad-output/implementation-artifacts/8-2-*.md` (this story); `sprint-status.yaml` (`8-2 → review`); `deferred-work.md` (mixed/prefab/system consumers).

### Change Log

- 2026-06-12 — Story 8.2 implemented: migrated the in-scope presentation query consumers (LightManager AC2 narrowing + BoardCameraManager + CharactersBar lane-A) onto `IGameStateQuery` via the 6.1 template (concrete `[SerializeField] GameManager` + `Query => gameManager` narrowing). Mixed (query+command) consumers deferred to 8.3, prefab-only consumers deferred to a later UI pass (Poyo's decisions, recorded). Registry + both guards green, 2.11a golden unchanged, boot smoke green. Status → review.
