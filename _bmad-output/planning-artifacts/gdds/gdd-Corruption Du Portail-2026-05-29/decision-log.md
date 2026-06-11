# Decision Log — Corruption Du Portail GDD

## 2026-05-29 — Session start

- **Intent:** Create (no prior GDD; brownfield, BMAD set up mid-project).
- **Working mode:** Facilitative + code exploration (user choice). User explains design intent; agent reads code in parallel to ground and challenge.
- **Genre handling:** No "social deduction" fragment in BMAD game-types library. Decision: treat as "Social Deduction (custom — Werewolf/Mafia family)". Document genre conventions explicitly (hidden info, roles/factions, phase structure, voting, per-faction win conditions) rather than forcing the closest fragment (party-game).
- **Grounding source:** `_bmad-output/project-context.md` (270 rules) loaded as foundational context. Code scan: Characters/, Powers/, GameStates/, WinningConditions/, ScriptableObjects/.

### Code-extracted facts (to validate with designer)
- Factions: anomaly, chosen, marginal, unknown.
- Winning teams: chosen, anomaly, marginal, alone.
- Archetypes (CharacterType): masked, detective, support, tracker, innocent.
- 18 roles (RoleID enum): Abyss, DrGloubi, Dryade, Gardien, Geolier, Incomplet, MageOcculte, Moork, Omniscient, Oracle, Orpheline, Repenti, Robot, Technomancien, ChasseuseDePrime, Uges, Messager, Croupiere.
- ~25 powers (P-prefix) with combinable components: PCChainer, PCConcentrated, PCPowerUnlockWhenChain, PCReparentOnChain.
- Phase flow: Lobby -> Introduction -> RoleAttribution -> Awakening -> AwakeningRecap -> Chaining -> Vote -> VoteRecap -> VictoryCheck -> TakeDownThePortal -> GameEnding.
- Coded win conditions: WAnomalyCorruption, WChosenChainedAllAnomaly, WMarginalIsChainedWin, WOmniscienceHackedCharacter.

## 2026-05-29 — Vision & core mechanics confirmed by designer

- **Genre framing confirmed:** Social Deduction (custom), "upgraded Werewolf".
- **3 camps:** Élus (chosen / village), Anomalies (anomaly / werewolves), Marginaux (marginal / solo-neutral).
- **Core fantasy:** be a real investigator — deduce each player's role, cross-verify info.
- **Central tension:** information is a double-edged currency. Reveal enough to help your camp win, but not so much that your role is identified. Role-ID leakage is the key risk (e.g. Dr Gloubi healing reveals he is Dr Gloubi).
- **Corruption:** the anomalies' goal is to corrupt everyone. Modeled on a modified Pied Piper. Corruption-by-power has **no concrete mechanical effect** on the corrupted player — it increments the **corruption counter**. The counter is primarily an **information signal / pressure clock** (e.g. "2 corruptions today but only Mage Occulte is anomaly → he guessed ≥1 role → act fast"). (Verify exact effects in code/SO before finalizing.)
- **Enchaînement (chaining) = vote elimination:** VoteState casts votes -> most-voted -> ChainingManager -> ChainingState. Being chained = **role revealed + becomes corrupted + can no longer use powers, but can still talk/participate** (like a vanilla LG villager). Design intent: a "dead" player stays useful (info/discussion), unlike classic Werewolf where eliminated = useless dead weight. This anti-dead-weight design is a deliberate pillar candidate.
- **PCChainer "En chaîne":** separate mechanic — a power, on success, can be reused up to maxChain (2) times. Engine behind Mage Occulte's Étreinte des Ombres multi-corruption.
- **Phase loop:** day/night cycle. Awakening = night (players use powers). Loop: Awakening -> AwakeningRecap -> ... -> Vote -> Chaining -> VictoryCheck -> repeat.
- **Mage Occulte endgame:** if voted/chained, triggers TakeDownThePortal; if he then guesses all players' roles, he wins solo.

### Designer constraint (workflow)
- DO NOT fully infer rules. Much of the truth lives in the **GameScene** config and **ScriptableObjects** (RoleDataObject / PowerDataObject .asset files). Read those before finalizing roles, powers, numbers.

## 2026-05-29 — POSTURE CHANGE (load-bearing)

- Poyo is the **developer**, paid by a friend who **owns the game design**. Poyo does not make radical GD decisions solo.
- **GDD = descriptive capture of the existing design**, NOT a redesign workshop. Document what exists; Poyo confirms accuracy. No mechanic changes unless Poyo (or Poyo + friend) decide.
- Source of truth for rules/numbers = **code + ScriptableObjects + GameScene config**. Read, do not invent.
- Genuine design gaps / open questions Poyo cannot answer -> tag `[NOTE FOR DESIGNER]` in gdd.md for the friend, do not resolve unilaterally.
- Pillars below are reframed as **observed pillars** (derived from existing design), presented for accuracy confirmation rather than authored.

## 2026-05-29 — Sections drafted & confirmed

- **Pillars:** confirmed accurate by designer. Written to gdd.md.
- **Executive Summary** (core concept, audience [ASSUMPTION], USPs): written.
- **Core Gameplay Loop:** authoritative order obtained from GameScene GameManager inspector (screenshot). 13 states:
  - Setup (isInGameLoop=false): LobbyState, RoleAttributionState, GameIntroductionState.
  - Loop (isInGameLoop=true): AwakeningState, AwakeningRecapState, ChainingState, VictoryConditionCheckState, VoteState, VoteRecapState, ChainingState2, TakeDownThePortalState, VictoryConditionCheckState(2nd).
  - End (isInGameLoop=false): GameEndingState.
  - ChainingState (1st) applies night-power chaining; ChainingState2 applies vote elimination. Duplicate state = tech debt (dict needs unique keys). Written to gdd.md.
- **Win/Loss Conditions:** 4 coded WinningConditions decoded & tabled. WinningTeam.alone + Mage Occulte endgame flagged [NOTE FOR DESIGNER]. Written to gdd.md.

### MCP status
- Unity MCP verified WORKING. Instance `CorruptionDuPortail@f525904260c848c4`, Unity 6000.2.6f2, GameScene open, idle, ready_for_tools=true. Can read SOs live for the roles/powers section.

## 2026-05-29 — Social Deduction Specific Design (roles/powers) WRITTEN

### Architecture correction (designer)
- **Character SOs are NOT deprecated** (earlier assumption wrong). Roles = `RoleDataObject` SO (`Assets/ScriptableObjects/Characters/*.asset`), still distributed by `RoleAttributionState`.
- **Powers migrated to prefabs**: `Power` is now a `NetworkBehaviour` (`Assets/Prefabs/Powers/*.prefab`); `Character.cs` collects them via `GetComponentsInChildren<Power>()`. The old `Powers/*.asset` SOs are the deprecated power system.
- `RoleDataObject.powers` (List<Power>) references the power prefabs by GUID.

### Extraction method
- Unity MCP `execute_code` UNUSABLE here: CodeDom mono cmdline too long (large project = too many assembly refs); Roslyn (Microsoft.CodeAnalysis) not installed. Fell back to a Python regex parser of the raw `.asset`/`.prefab` YAML: `_bmad-output/planning-artifacts/gdds/_extract_roles.py` (run with `py`). Decodes FixedString byte arrays, maps script/asset GUIDs via `.cs.meta`/`.meta`. Reusable to refresh.
- Enums decoded: FactionType (anomaly0/chosen1/marginal2/unknown3), CharacterType (masked1/detective2/support3/tracker4/innocent5), TargetIncludeFlags bitmask.

### Captured & written to gdd.md
- **Roles and Factions** table: 18 roles, faction/archetype/diff/powers/win, pool = 3 anomaly / 14 chosen / 1 marginal. Per-game counts come from RoleAttributionState dict (not yet read → flagged).
- **Awakening order**: 12 layers from AwakeningState.asset.
- **Powers structural overview**: 25 prefabs, passive vs active, maxWaitTime 60s, EmbraceOfShadows = only PCChainer(maxChain=2).
- **Phase Structure** cross-ref to Core Gameplay Loop; **Win Conditions per Faction** filled from coded conditions.

### Designer confirmations
- Moork, Uges, Gardien (no powers) + Moork absent from awakening = **WIP, game unfinished**. Tagged `[WIP]`.
- `Omniscient` role = **deprecated**, superseded by `Incomplet`. (Power `Omniscience` on Robot is unrelated.)
- `Traqueuse` = RoleID `ChasseuseDePrime`, redesigned. Confirmed.

### Open / flagged
- `[NOTE FOR DESIGNER]` per-game role counts + canBeFake from GameScene RoleAttributionState.
- `[NOTE FOR DESIGNER]` Mage Occulte solo win: coded WinningCondition vs handled in TakeDownThePortalState.
- Per-power **effects** not yet written (only structure). Requires reading 25 `P*.cs`.

## 2026-05-29 — Power glossary WRITTEN (effects + impl status)

- Read all 24 `P*.cs` power scripts. Wrote a **Power glossary** table to gdd.md: effect (from code, not inferred) + dev Status column.
- Reframe from Poyo: **GDD must accelerate development** → glossary doubles as a dev checklist (Status = OK / TODO / WIP), captures in-code TODOs.
- **In-code TODOs captured** (dev-actionable):
  - `PEmbraceOfShadows.OnCorruptionSuccessful()` empty `//TODO: THIS`.
  - `PHighPriorityBounty` `//TODO: do actual elimination logic & visual`.
  - `POmniscience` `//TODO: rework win condition to use power instead of WinningCondition`.
- **Reveal model**: powers grant personal info via `GameInfoRevealer.SetRevealLevel/SendRevealLevelRpc` (`isRoleRevealed`/`isCorruptRevealed`, RevealLevel.Personal/Public, optional falsified bool). Targeting via `TargetIncludeFlags` + `targetValidator`. Pick flows via `SelectionFlowService`.
- **Systems referenced by powers** (architecture-level): RoleTargetSystem, GameInfoRevealer, ChainingManager, CardEffectManager, SelectionFlowService.
- New flags: `Savoir de la corruption` exact reveal semantics; `Paranoïa de la corruption` falsified-flag intent; `Messages infini` reparent-only trigger — all `[NOTE FOR DESIGNER]`.

## 2026-05-29 — Primary Mechanics WRITTEN (+ Controls) + flags resolved

- Read `VoteState`, `ChainingManager`, `ChainingState`, `TakeDownThePortalState`. Wrote Game Mechanics → Primary Mechanics: Corruption, Chaining, Voting, Take Down The Portal, Information/reveal economy. Wrote Controls and Input (partial; bindings TBD).
- **Flags RESOLVED from code:**
  - Take Down The Portal trigger = a chained character holding the *Abattre le portail* power (Mage Occulte chained). `shouldActivate` `//TODO: Temporary`.
  - Mage endgame = NOT a coded WinningCondition; handled in `TakeDownThePortalState`; full success → **anomaly faction** wins.
  - `WinningTeam.alone` = **unused** by current win logic.
  - Updated the 3 prior `[NOTE FOR DESIGNER]` spots accordingly (Core Loop step 8, Win/Loss table note, Win Conditions per Faction).
- **Key mechanics facts:** chaining = one `ChainingState` SO used twice (pass1 night-power queue, pass2 vote elim); queue = `ChainingManager.chainingPlayers`. `isChained` ≠ `isEliminated` (chained still votes/talks; only `isEliminated` blocks voting). Healing one-shot per player. Vote: 1 vote/player, no change, Skip option, tie/Skip → no elim, timer clamps to ≤5s once all voted.

## 2026-05-29 — GameScene explored (MCP) + Progression/Balance WRITTEN

### Scene context (Unity MCP, GameScene)
- 7 roots: `---System---` (4 ch), `---GameLogic---` (18 ch, NetworkObject), `---GameVisuals---` (9 ch), Music, RoomFog, Cube, TestNetworkAction.
- `GameManager` (under GameLogic) confirms LIVE the 13-state machine with `isInGameLoop`: Lobby(F), RoleAttribution(F), GameIntroduction(F), Awakening(T), AwakeningRecap(T), ChainingState(T), **VictoryConditionCheckStatePreVote**(T), Vote(T), VoteRecap(T), **ChainingState2**(T, =ChainingState SO reused), TakeDownThePortal(T), VictoryConditionCheck(T), GameEnding(F). Both check-states are the same `VictoryConditionCheckState` class (PreVote + post-portal).
- Managers wired on GameManager: gameInfoRevealer, charactersBar, powersBar, chainingManager, characterManager. `DevIdentityController` MonoBehaviour on GameManager GO = the simulated/bot identity debug system (ties to GetSafeRpcTarget / IsLocalOrSimulated). `currentDay` starts 1.

### Role attribution (RoleAttributionState.asset)
- All `roleToAttribute` = 0 → counts set **per match in the lobby** (RoleAttributionSettingTab), not hard-coded.
- Attribution dict = **13 roles**: MageOcculte (canBeFake=0, always real), Abyss, Robot, Technomancien, Croupière, Orpheline, DrGloubi, Dryade, Messager, Repenti, Oracle, Traqueuse, Incomplet.
- Excluded from dict (can't be dealt): Uges/Gardien/Moork [WIP], Omniscient [deprecated], **Geôlier** (functional but excluded — flagged [NOTE FOR DESIGNER]).
- GUID→role mapping cross-referenced against AwakeningState layers.

### Written to gdd.md
- Roles & Factions → new **Attribution pool (per-match)** subsection.
- **Progression and Balance** section: session-based, no meta-progression/XP/economy; roleDifficulty 1–3 spread (UX hint); resources = power uses + messages; balance lever = lobby role mix + fake cards (canBeFake), Mage always real.
- New flag: standard role-count presets per player-count (not in code).

### RESUME POINT — remaining sections
Done so far: Executive Summary, Pillars, Core Gameplay Loop, Win/Loss, Roles & Factions (+ Attribution pool), Awakening, Power glossary, Primary Mechanics, Controls (partial), Progression & Balance.
Remaining (lighter / partly N/A): Level Design (likely N/A — single board/room), Art & Audio (FMOD GameAudioManager; mystical Portal theme; RoomFog), Technical Specifications (NGO 2.6 server-auth, Facepunch/Steam transport + DevIdentityController simulated gateway, 60 FPS targets), Development Epics, Success Metrics, Out of Scope, Assumptions & Dependencies, Goals/Context (still _TBD_).
Open `[NOTE FOR DESIGNER]` still live: Geôlier excluded from attribution pool (intentional?); recommended role-count presets per player-count; Savoir de la corruption / Paranoïa de la corruption reveal semantics; Messages infini reparent-only trigger.

## 2026-05-29 — Remaining sections WRITTEN (draft complete)

All `_TBD_` sections filled, grounded in project-context.md + architecture.md (no new code reads needed):
- **Technical Specifications:** perf targets (60 FPS desktop 16.6ms / mobile 60 w/ 30 floor 33ms, ~30Hz tick), platform details (Unity 6000.2.6f2, NGO 2.6 server-auth + Gateway RPC, Facepunch/Steam + sim gateway, Unity Services lobby, Win primary / Android+iOS secondary, Input System multi-map), asset reqs (Addressables, FMOD banks).
- **Level Design Framework:** single shared 3D board, no progression (flagged "does not apply").
- **Art & Audio:** Portal/Corruption reskin theme, 3D card board + 2D smartphone OS UI, RoomFog, card-flip reveal; FMOD-only audio, faction sounds. Visual + music direction → `[NOTE FOR DESIGNER]`.
- **Goals and Context:** upgraded-Werewolf goals (descriptive); brownfield rationale ([ASSUMPTION] Werewolf pain points).
- **Success Metrics:** technical targets concrete; gameplay KPIs → `[NOTE FOR DESIGNER]` (candidate list: completion rate, per-faction win balance, match length, dead-time reduction).
- **Out of Scope:** no meta-progression/maps; WIP roles + TODO powers (dev items, not cut); `WinningTeam.alone` unused.
- **Assumptions and Dependencies:** hard deps list; assumptions index (2); consolidated open `[NOTE FOR DESIGNER]` index (9 items).
- **Development Epics:** summary table E1–E5 in gdd.md + detailed `epics.md`. Epics derived from `[WIP]`/`TODO` checklist (completion roadmap, not greenfield). Sequence E1‖E2 → E3 → E4 → E5; E3/E4 network-critical (test-gated). Priority + design calls flagged for GD owner.

**Status:** gdd.md draft complete (all template sections populated). NOT yet through Finalize (decision-log audit / input reconciliation / validator discipline pass / open-items triage / polish) — deferred per user quota. Goals/Background remain partly design-owned (flagged), not invented.

## 2026-05-29 — Epics REMOVED (designer request)

- Poyo rejected the E1–E5 epics: they were **inferred** from the WIP/TODO checklist, and epic priority/scope/sequencing is a **design-ownership** call he does not make (he is the paid dev, not the GD owner).
- **Deleted `epics.md`.** Replaced gdd.md → Development Epics → Epic Structure with a "not defined / design-owned" placeholder that points back to the Status columns (Roles & Factions, Power glossary) as the factual basis for whatever epics the GD owner defines later.
- **Posture reaffirmed:** Poyo executes, does not design. GDD = descriptive capture only. Remaining design-owned sections stay flagged (`[NOTE FOR DESIGNER]`), never authored on his behalf. GDD may stay intentionally incomplete on design-owned sections.
