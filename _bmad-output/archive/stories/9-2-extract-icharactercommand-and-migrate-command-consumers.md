# Story 9.2: Extract ICharacterCommand + migrate the command-side consumers `# REVIEW-REQUIRED`

Status: done

## Story

As a developer,
I want spawn/mutation operations behind an injected `ICharacterCommand`, with the bot flow untouched,
so that server-authority writes are explicit and narrow without changing the network behaviour by one byte.

## Acceptance Criteria

1. **Command surface from the 9.1 census** (spawn, server mutations, reveal-adjacent commands) → `ICharacterCommand` in `Game`; `CharacterManager` implements; signatures verbatim; inert first commit.
2. **NFR5 byte-identical:** `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` bodies UNTOUCHED and NOT exposed on the interface (they stay adapter-internal; if a consumer calls them directly today, that call site is inventoried and the exposure decision recorded — default: keep concrete access for those few, verify-don't-force, revisit in 12.3).
3. **Fixture proof:** `MultiClientGameFixture` runs green; at least one case exercises a `clientId >= 100` target through a command-path consumer migrated in this story (interception asserted, not hoped).
4. **Server-authority preserved:** command members callable in the same contexts as today; no client-side write path opened.
5. **Gated + reviewed:** suite + goldens unchanged per batch; registry/guards green; **gds-code-review run before merge** (REVIEW-REQUIRED — network-sensitive, point-of-no-return for the split).

## Tasks / Subtasks

- [x] **Task 1:** Command-member list from 9.1's census + direct `GetSafeRpcTarget`/`IsLocalOrSimulated` call-site inventory frozen (Completion Notes).
- [x] **Task 2:** `ICharacterCommand` extracted (Game asmdef, via `create_script`), `CharacterManager : …, ICharacterCommand` (all 10 members implicit — inert), `CompositionRoot.CharacterCommand` accessor (instance + Services). Zero call-site change; EditMode **162/162** + PlayMode **146/146** byte-identical. **AC1 inert checkpoint.**
- [x] **Task 3:** 4 clean command consumers narrowed to `ICharacterCommand` (LobbyState / RoleAttributionState / VoteState / ChainingManager); powers + GameManager + Character + DevIdentityController kept concrete (AC2 verify-don't-force, recorded); bot-path fixture case added (`CharacterCommandBotFlowTests`). Detail in Completion Notes.
- [x] **Task 4:** Gates EditMode **162/162** + PlayMode **147/147** (incl. the RPC-through-interface PlayMode goldens + the new bot case). sprint-status → review. **gds-code-review DONE (3 adversarial layers, 2026-06-12): 0 decision / 1 patch / 1 defer / 3 dismissed; no Critical/High, no behaviour regression.**

### Review Findings (gds-code-review 2026-06-12)

- [x] [Review][Patch] PowerEffectDispatcher missing from the AC2 kept-concrete inventory [Assets/Scripts/Characters/Powers/PowerEffectDispatcher.cs:77] — RESOLVED: added to the kept-concrete inventory in Completion Notes (static-locator + NFR5 internal → Epic 10/12 static-leaf sweep). Doc-only, no code change.
- [x] [Review][Defer] `GetCharacters().Add()` routed through `ICharacterQuery` is a pre-existing no-op [Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:109] — deferred, pre-existing. `CharacterManager.GetCharacters()` returns `new List<Character>(_characters)` (defensive copy, CM:303), so the `.Add` mutates a throwaway and is silently discarded — identical to pre-9.2 `characterManager.GetCharacters().Add(...)`. Behaviour-preserving rename; the dead write is out of 9.2 scope.

**Dismissed (noise / verified):** (1) `GiveRoleToCharacterRpc`-through-interface "only transitively gated" — VERIFIED gated: `RoleAssignmentGoldenMasterTests:122/156` run the real `RoleAttributionState.OnStartStateServer` → `ApplyRole` → `Command.GiveRoleToCharacterRpc` (PM green). (2) Naming `Command` (bare) vs `CharacterQuery` — consistent with the existing bare `Loop` (IGameLoop) on the same base; `CharacterQuery` is the deliberate outlier (dodges the `Query` collision, 9.1). (3) Interface-satisfaction "unproven from diff" — all 10 members verified verbatim + public on CharacterManager (line-by-line) and EM/PM compile+pass.

## Dev Notes

- THE network-sensitive story of the track. The split itself is type-level (inert); the risk concentrates in any consumer whose command call sits inside RPC/bot-flow code — those reroutes must be access-path-only, diff-reviewed line by line.
- Precedent: 5.0c migrated ~78 CharacterManager call sites in gated batches with bot flow byte-identical — same discipline, now with the fixture (which 5.0c didn't have for its first batches).
- Review policy: `feedback_review_required_gate` — run /gds-code-review (cheap local effort) before merging.
- Staleness: census-driven; re-derive everything at dev time.

### Project Structure Notes

- New: `Assets/Scripts/Characters/ICharacterCommand.cs`. Modified: `CharacterManager.cs`, `CompositionRoot.cs`, command-consumer files. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Silent killers #1-#3 (GetSafeRpcTarget / IsLocalOrSimulated / server authority) — the whole story is fenced by them. Never widen `NetworkVariable` write perms. RPC order guarantees unchanged.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3(d) NFR5 relocation, §8 Epic 9] / [epics.md#Story 9.2]
- [Source: _bmad-output/implementation-artifacts/5-0c-charactermanager-per-networkmanager-registry.md] — the batch-migration precedent + bot-flow discipline.
- [Source: _bmad-output/implementation-artifacts/9-1-*.md] — previous story: the census this story consumes.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Inert compile: 0 errors (`CharacterManager : ICharacterQuery, ICharacterCommand` satisfied implicitly → no CS0535). `ICharacterCommand.cs` via `create_script` ([[unity-silent-compile-exclusion]]). Gate: EditMode **162/162** + PlayMode **146/146** (interface-only, zero call-site change → byte-identical, calque 9.1/8.1 inert).

### Completion Notes List

**Task 1 — frozen census + member partition.**

`ICharacterCommand` (the COMMAND slice, signatures lifted verbatim — 10 members):
- `Character AddNewCharacter(ulong)`
- `void RemoveCharacter(ulong)`
- `Character CreateNewFakeCharacter()`
- `void GivePowerToCharacter(ulong, Power)`
- `void RemovePowerFromCharacter(ulong, Power)`
- `void GiveRoleToCharacterRpc(ulong, Role)` — `[Rpc(SendTo.Everyone, RequireOwnership=true)]`
- `void AskForUpdateAllCharactersRpc()` — `[Rpc(SendTo.Server)]`
- `void SpawnSimulatedPlayer()`
- `void SetPossessedIdentity(ulong?)`
- `void RegisterSpawnedCharacter(Character)`

**OFF every interface (recorded):** `GetSafeRpcTarget`, `IsLocalOrSimulated` (NFR5 network-authority internals, §3(d) / AC2 — stay concrete, never edited); `GetCharacterAsync` (no external consumer — internal to `GiveRoleToCharacterAsync`); `instance`/`For` (static infra).

**Direct `GetSafeRpcTarget` / `IsLocalOrSimulated` consumer inventory (AC2 — these keep CONCRETE `CharacterManager` access, verify-don't-force, revisit 12.3):**
- `GetSafeRpcTarget`: `Power` (base) + `PCardsShuffling`, `PBoundByInk`, `PBlessing`, `PEyeOfTheVoid`, `PDroolyHealing`, `PClandestineObservation`, `PVisionOfTheImpossible`, `PLackOfAffection`, `PHighPriorityBounty`; `GameManager` (468).
- `IsLocalOrSimulated`: `PPersonalBeacons`, `PLackOfAffection`.
- These consumers mix query + command + adapter-internal on the SAME field → narrowing buys nothing while the concrete field must stay for GetSafeRpcTarget → keep concrete, recorded.

**Command-consumer partition (Task 3 input):**
- **CLEAN → narrow to `ICharacterCommand` (command + reads only, NO GetSafeRpcTarget/IsLocalOrSimulated):** `LobbyState` (Add/Remove + GetCharacters), `RoleAttributionState` (CreateNewFake/GivePower/GiveRoleRpc + GetCharacters), `VoteState` (AskForUpdateRpc + reads), `ChainingManager` (AskForUpdateRpc + GetCharacter, own `[SerializeField]` field). GameState-base trio routes commands via a new `protected ICharacterCommand Command => characterManager` + reads via the 9.1 `CharacterQuery`; ChainingManager gets its own Query+Command narrowing props.
- **KEEP CONCRETE → record (AC2 verify-don't-force):** all powers + `GameManager` (GetSafeRpcTarget/IsLocalOrSimulated); `Character` (lane C, spawn-critical OnNetworkSpawn, single `RegisterSpawnedCharacter` + `GetLocalCharacter` — low ISP value, high spawn-path risk → keep concrete, record); `DevIdentityController` (`CharacterManager.instance` debug-only F-key tool, NOT injected — Spawn/SetPossessed; lane decision deferred to Epic 10/12, record).

**OPEN network decision for Task 3 (REVIEW-REQUIRED):** routing the two `[Rpc]` commands (`AskForUpdateAllCharactersRpc`, `GiveRoleToCharacterRpc`) THROUGH the `ICharacterCommand` interface reference — NGO ILPP rewrites the method BODY (send/execute routing is reached via any dispatch incl. interface vtable), so it is expected byte-identical and is hard-gated by the RPC PlayMode goldens (`VoteTallyGoldenMasterTests` exercises VoteState.AskForUpdate; `RoleAssignmentGoldenMasterTests` exercises RoleAttribution.GiveRoleRpc). Empirical gate decides; fallback = keep those two call-sites concrete.

**Task 2 — inert extraction (done).** `ICharacterCommand` in `Assets/Scripts/Characters/`; `CharacterManager : NetworkBehaviour, ICharacterQuery, ICharacterCommand` (implicit); `CompositionRoot.CharacterCommand` accessor on instance + Services. Inert — no consumer rerouted yet. **AC1 inert checkpoint, EM 162 / PM 146 byte-identical.**

**Task 3 — command-consumer narrowing (done).** 4 CLEAN command consumers narrowed (access-path-only, the 9.1 read-narrowing discipline applied to the command slice — the property returns the same object, so dispatch is byte-identical):
- GameState base gained `protected ICharacterCommand Command => characterManager` (next to 9.1's `CharacterQuery`). Routed states: **LobbyState** (`Command.AddNewCharacter` / `Command.RemoveCharacter`; `CharacterQuery.GetCharacters`), **RoleAttributionState** (`Command.CreateNewFakeCharacter` / `Command.GivePowerToCharacter` / `Command.GiveRoleToCharacterRpc`; reads via `CharacterQuery`), **VoteState** (`Command.AskForUpdateAllCharactersRpc`; reads via `CharacterQuery`).
- **ChainingManager** (own `[SerializeField]` field): added its own `Query` + `Command` narrowing props; `Query.GetCharacter`, `Command.AskForUpdateAllCharactersRpc`.
- **`[Rpc]`-through-interface decision (Poyo: "fais selon ton meilleur jugement" → Option A, gate decides):** the two `[Rpc]` commands (`AskForUpdateAllCharactersRpc`, `GiveRoleToCharacterRpc`) ARE dispatched through the interface. Validated byte-identical by PlayMode — `VoteTallyGoldenMasterTests` (VoteState.AskForUpdate) + `RoleAssignmentGoldenMasterTests` (RoleAttribution.GiveRoleRpc) pass unchanged. NGO ILPP rewrites the method body, so vtable dispatch reaches the same send/execute routing. (Fallback "keep those 2 concrete" was prepared but not needed.)

**KEPT CONCRETE (AC2 verify-don't-force, recorded — narrowing buys nothing while the concrete field must stay for the NFR5 internals, or the consumer is not injected):** all powers + `GameManager` (call `GetSafeRpcTarget` / `IsLocalOrSimulated`); `Character` (lane C, spawn-critical `OnNetworkSpawn`, single `RegisterSpawnedCharacter` + `GetLocalCharacter` — low ISP value, high spawn-path risk); `DevIdentityController` (`CharacterManager.instance` debug-only F-key tool, not injected — lane decision → Epic 10/12); **`PowerEffectDispatcher`** (`CharacterManager.instance.AskForUpdateAllCharactersRpc()` + `.GetSafeRpcTarget()` + `.GetCharacter()` — static-locator + NFR5 internal, not Epic-7-injected → Epic 10/12 static-leaf sweep) [review-added]. `SpawnSimulatedPlayer` / `SetPossessedIdentity` / `RemovePowerFromCharacter` therefore have no migrated consumer this story (interface members exist; consumer reroute deferred). No registry/guard edits: the 4 consumers were already Epic-7-injected (lane B push / scene field), and narrowing introduces no `.instance`/`.For(`.

**AC3 — bot-path fixture proof (`CharacterCommandBotFlowTests : MultiClientGameFixture`, +1 → PM 147).** One `[UnityTest]` asserting: (1) `CompositionRoot.For(host/client).CharacterCommand` resolves per-NM to the right CharacterManager (never crossed); (2) `AskForUpdateAllCharactersRpc()` dispatched THROUGH the `ICharacterCommand` ref reaches the server RPC body (`Assert.DoesNotThrow`) — the novel-risk proof; (3) `AssertSimulatedBotIsIntercepted()` — the clientId >= 100 GetSafeRpcTarget redirect-to-host is byte-identical (NFR5, verbatim). Honest-scope note recorded in the test: the >= 100 redirect lives in concrete `GetSafeRpcTarget` (AC2-kept), so it is asserted on the concrete adapter alongside the interface-dispatched command surface — does not mutate the shared fixture.

**Gate (Task 4):** EditMode **162/162**, PlayMode **147/147** (146 baseline + the new bot case). AC4 server-authority preserved (same methods, same `Assert.IsServer` guards, no client write path opened). **AC5: gds-code-review still PENDING — REVIEW-REQUIRED, point-of-no-return for the split; run before merge.**

### File List

**Added (production):**
- `Assets/Scripts/Characters/ICharacterCommand.cs` — the command slice (10 members).

**Added (tests):**
- `Assets/Scripts/Tests/PlayMode/Desingleton/CharacterCommandBotFlowTests.cs` — AC3 bot-path fixture proof.

**Modified (production) — Task 2 (inert):**
- `Assets/Scripts/Characters/CharacterManager.cs` — `: …, ICharacterCommand` (implicit impl).
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `CharacterCommand` accessor (instance + `Services` struct).

**Modified (production) — Task 3 (command-consumer narrowing):**
- `Assets/Scripts/GameLogic/GameState.cs` — `protected ICharacterCommand Command`.
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs`, `.../RoleAttributionState.cs`, `.../VoteState.cs` — command calls via `Command`, reads via `CharacterQuery`.
- `Assets/Scripts/GameLogic/ChainingManager.cs` — own `Query` + `Command` narrowing props; routed.

**Docs:**
- `_bmad-output/implementation-artifacts/9-2-*.md` (this story); `sprint-status.yaml` (`9-2 → review`).

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Task 1 census + Task 2 inert `ICharacterCommand` extraction (CharacterManager implements; CompositionRoot.CharacterCommand). EM 162 / PM 146 byte-identical. |
| 2026-06-12 | Task 3 narrowed 4 clean command consumers (incl. `[Rpc]`-through-interface, Option A); kept powers/GameManager/Character/DevIdentityController concrete (AC2). Task 4 gate EM 162/162 + PM 147/147 (new `CharacterCommandBotFlowTests`). Status → review; gds-code-review pending (REVIEW-REQUIRED). |
