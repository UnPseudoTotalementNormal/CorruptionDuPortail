---
title: Corruption Du Portail
game_type: Social Deduction (custom — Werewolf/Mafia family)
platforms: Windows/Steam (primary), Android/iOS (secondary)
created: 2026-05-29
updated: 2026-05-29
---

# Corruption Du Portail - Game Design Document

**Author:** Poyo
**Game Type:** Social Deduction (custom — Werewolf/Mafia family)
**Target Platform(s):** Windows/Steam (primary), Mobile Android/iOS (secondary)

---

## Executive Summary

### Core Concept

An "upgraded Werewolf" — an asymmetric multiplayer social-deduction game built around three camps: **Élus** (chosen / village), **Anomalies** (anomaly / werewolves), and **Marginaux** (marginal / solo-neutral). The Anomalies aim to **corrupt** everyone (a modified Pied-Piper spread); the Élus must stop them by **chaining** (vote-eliminating) the Anomalies. The heart of play is information management: deduce other players' roles while avoiding revealing your own, because a revealed role is ammunition for the enemy. Wrapped in a mystical "Portal/Corruption" theme to differentiate from classic wolves/villagers.

### Target Audience

Social-deduction players (Werewolf / Mafia / Among Us audience) who want deeper role/power interplay and less dead time. Online multiplayer on Steam (desktop) and mobile. [ASSUMPTION: audience profile inferred — confirm with designer.]

### Unique Selling Points (USPs)

- **No dead weight:** an eliminated (chained) player keeps contributing through discussion and information, instead of spectating until the end like in classic Werewolf.
- **Information as a double-edged currency:** every reveal that helps your camp also risks exposing your role to the enemy.
- **Deep role/power system:** 18 roles, ~25 powers built from combinable power-components, enabling chained combos (e.g. multi-corruption).
- **Corruption counter as a shared deduction signal:** the public corruption count is itself an information/pressure clock players reason about.

---

## Goals and Context

### Project Goals

Deliver an **"upgraded Werewolf"** online social-deduction game — Steam/desktop primary, mobile secondary — differentiated on three axes: **no dead weight** (chained players keep contributing), a **double-edged information economy**, and a **deep role/power system** (18 roles, ~25 component-built powers). [NOTE FOR DESIGNER]: formal project goals and commercial/success targets are owned by the GD.

### Background and Rationale

**Brownfield** — the game is in active development; the BMad/GDS docs were set up mid-project. This GDD is a **descriptive capture** of the existing design (source of truth = code, ScriptableObjects, GameScene config), not a redesign. [ASSUMPTION]: the design addresses classic-Werewolf pain points — spectator dead time after elimination, and shallow role interplay — confirm framing with the GD owner.

---

## Core Gameplay

### Game Pillars

1. **Information is a double-edged weapon.** The core skill is *dosing* what you reveal versus hide about your own role — reveal enough to help your camp win, not so much that you are identified. Governs what the player emits.
2. **Investigation and cross-deduction.** Reading what others emit: guessing roles, cross-referencing claims, catching inconsistencies. The detective fantasy. (Distinct from Pillar 1: emission vs reception.)
3. **Always useful, even when eliminated.** A chained player has their role revealed, becomes corrupted, and loses powers — but still talks and passes information. The deliberate anti-dead-time differentiator versus classic Werewolf.

> The **Corruption counter** is a mechanic serving Pillars 1 & 2 (shared information / readable pressure clock), not a separate pillar.

### Core Gameplay Loop

The match runs a state machine (server-authoritative). States are flagged `isInGameLoop`; the contiguous flagged block repeats once per "day" (`currentDay` increments via `onNewDayPassed`). Authoritative order from the GameScene GameManager config:

**Setup (once, not looped):**
1. **Lobby** — players join, preload assets.
2. **Role Attribution** — roles dealt.
3. **Game Introduction** — intro/onboarding before the first day.

**Daily loop (repeats until a victory condition exits):**
1. **Awakening (night)** — roles wake in configured layer order (`awakeningOrder`); each awakened player uses their power within a per-layer timer (= longest power `maxWaitTime` in the layer). Chained/eliminated players do not wake.
2. **Awakening Recap** — night summary.
3. **Chaining** — applies chaining caused by **night powers** (the 1–2 powers that chain).
4. **Victory Condition Check** — evaluate win conditions.
5. **Vote** — players vote to designate who to chain (`voteDuration` timer; skip option; ends early when all eligible have voted).
6. **Vote Recap** — vote result summary.
7. **Chaining 2** — applies the **vote elimination** (most-voted player is chained). [Duplicate state SO of Chaining — technical debt, because the state container is a dictionary and cannot hold the same key twice.]
8. **Take Down The Portal** — endgame state for the Mage Occulte path (in-loop; conditional). No-ops each day unless `shouldActivate` is set — which happens only when a chained character holds the *Abattre le portail* power (i.e. the Mage Occulte was just chained). See Game Mechanics → Take Down The Portal.
9. **Victory Condition Check** — second checkpoint.

**End (once):**
- **Game Ending** — resolves winners, ends the match.

> **Chaining** is overloaded: (a) vote elimination (Chaining 2) and (b) night-power chaining (Chaining). Distinct from the **`PCChainer` "En chaîne"** power-component (a power reusable up to `maxChain`=2 times on success).

### Win/Loss Conditions

Win conditions are objects (`WinningCondition`) checked during the Victory Condition Check states. Some are **global/faction** wins; some are **per-player** (carry an `ownerClientId`). Coded conditions:

| Condition | Winning team | Trigger (from code) |
|---|---|---|
| **WAnomalyCorruption** | anomaly | **Every** non-fake player `isCorrupted`. (Note: being chained also sets corrupted, so chaining feeds this track too.) |
| **WChosenChainedAllAnomaly** | chosen | **All** anomaly-faction players `isChained`. |
| **WMarginalIsChainedWin** | marginal (per-player) | The owning player gets `isChained` — a "Jester/Bouffon" who wins by getting voted out. |
| **WOmniscienceHackedCharacter** | marginal (per-player) | Owner has `POmniscience`, has designated a "hacked" target, and that target is `isChained` **and** of the `chosen` faction. |

> The Mage Occulte "Take Down The Portal / guess all roles" endgame is **not** a coded `WinningCondition` — it is resolved in `TakeDownThePortalState`, which on full success ends the game with the **anomaly faction** as winners (see Game Mechanics). The `WinningTeam.alone` enum value exists but is **not used** by current win logic.

---

## Game Mechanics

### Primary Mechanics

All game state is mutated server-authoritatively (`Character` NetworkVariables); clients propose via RPCs.

#### Corruption

- **State:** per-player `isCorrupted` (plus `isHealed`, `isBlessed`). 
- **Sources:** anomaly **auto-corruption** at game start (Auto Corruption); corruption powers (Marque corruptrice / Corruption ciblée, Étreintes des ombres on correct guess, Vision Maudite — which also corrupts its caster); **being chained also sets corrupted**.
- **Removal:** heal powers (Soin Baveux, Bénédiction) clear corruption and set `isHealed`. Healing is **one-shot per player** (`HealPlayerServerRpc` no-ops if already healed).
- **Effect:** per designer, corruption has **no direct mechanical effect** on the corrupted player. It is a shared **information signal / pressure clock** and feeds the anomaly win track (`WAnomalyCorruption` = every non-fake player corrupted).

#### Chaining

One mechanism, two triggers. Queue = `ChainingManager.chainingPlayers`; powers and the vote call `AddCharacterToChainingList`; a `ChainingState` drains the queue → `ChainCharacterRpc` → sets `isChained` + `isCorrupted` + **public role reveal**, with a card-flip animation per chained player.

- The **same `ChainingState`** appears twice in the loop. **Pass 1** (after Awakening) applies **night-power chaining** (queued by Chaînes de la vérité, Enchaîné par les Ombres, the Prime Prioritaire wrong-target penalty). **Pass 2** (`ChainingState2`, after the Vote) applies the **vote elimination**.
- A **chained** player: role revealed publicly, becomes corrupted, can no longer use powers (`Power.CanUse` blocks on `isChained`) — but still participates (talks; `CanVote` only blocks `isEliminated`, not `isChained`). This is the anti-dead-weight pillar.
- **`isChained` ≠ `isEliminated`.** Elimination is a separate, rarer state (e.g. Prime Prioritaire removing the Robot).
- If the chained player holds the **Abattre le portail** power (Mage Occulte), chaining triggers the **Take Down The Portal** endgame.

#### Voting

- During `VoteState`, each non-fake, non-eliminated player casts **one** vote (cannot change it) or **Skip**. `voteTimer` starts at `voteDuration`; once all eligible players have voted it is clamped to ≤5s; on expiry the state advances.
- **Resolution:** a **single** clear most-voted player (not Skip) is queued for chaining (applied in `ChainingState2`). A **tie**, or Skip winning, yields **no elimination** that day.

#### Take Down The Portal (Mage Occulte endgame)

- **Trigger:** the Mage Occulte (holder of Abattre le portail) gets chained → the portal state activates (`shouldActivate`, flagged `//TODO: Temporary`).
- **Flow:** the Mage repeatedly picks a still-hidden character, then guesses its role. **Correct** → that role is publicly revealed, continue to the next. **Wrong** → the attempt ends and the game proceeds.
- **Win:** if the Mage correctly identifies **every** remaining (non-fake, not-already-revealed) character, the game ends with the **anomaly faction** as winners. [Resolved] This endgame is handled directly in `TakeDownThePortalState`, **not** a coded `WinningCondition`, and it awards `WinningTeam.anomaly` — `WinningTeam.alone` is not used by current win logic.

#### Information / reveal economy

`GameInfoRevealer` tracks per-character reveal levels — `isRoleRevealed`, `isCorruptRevealed` at `RevealLevel` None / Personal / Public (a falsified flag can mark unreliable info). Powers grant **Personal** reveals to their owner; chaining and the portal endgame grant **Public** reveals. `RoleTargetSystem` records who-targeted-whom (queried by Observation Clandestine and Mélange des cartes). This reveal/targeting backbone is what the deduction pillars operate on.

### Controls and Input

In-game **smartphone-style 2D UI** overlaid on a **3D board** of player cards. Power use and most interactions route through `SelectionFlowService` — tap a player **card** (character selection), tap a **role** card (role selection), or a character-then-role sequence; voting uses a per-card vote canvas. _TBD: exact input bindings (touch / mouse / keyboard / gamepad) and platform input maps — confirm from the Input System asset._

---

## Social Deduction Specific Design (custom)

### Roles and Factions

Source of truth: `RoleDataObject` ScriptableObjects (`Assets/ScriptableObjects/Characters/*.asset`) + power prefabs (`Assets/Prefabs/Powers/*.prefab`). `FactionType` enum = anomaly / chosen / marginal / unknown. `CharacterType` (archetype) enum = masked / detective / support / tracker / innocent. Per-game role counts are NOT fixed by the pool — they come from the `RoleAttributionState` dictionary (`RoleAttributionSetting`: `roleToAttribute`, `canBeFake`) configured in the GameScene. [NOTE FOR DESIGNER: per-game role counts / fake settings not yet captured — read GameScene RoleAttributionState.]

> **WIP — the game is unfinished.** Several roles ship with no powers in their SO and/or are absent from the awakening order. These are flagged below as `[WIP]` and are not yet playable as designed. `Omniscient` is **deprecated** — its design role was superseded by `Incomplet`; it remains in the enum/pool but is not implemented.

The role pool holds **18 roles** across **3 active factions** (3 anomaly / 14 chosen / 1 marginal):

| Role | Faction | Archetype | Diff | Powers | Win condition | Status |
|---|---|---|---|---|---|---|
| **Mage Occulte** | anomaly | detective | 3 | Étreintes des ombres, Corruption ciblée, Auto Corruption, Corruption Insight, Abattre le portail | WAnomalyCorruption | OK |
| **Abyss** | anomaly | support | 2 | Enchaîné par les Ombres, Corruption Insight, Auto Corruption, Oeil du néant, Héritage | WAnomalyCorruption | OK |
| **Moork** | anomaly | detective | 2 | — | WAnomalyCorruption | `[WIP]` no powers, absent from awakening order |
| **Croupière** | chosen | detective | 3 | Mélange des cartes | WChosenChainedAllAnomaly | OK |
| **Dr Gloubi** | chosen | support | 2 | Soin Baveux, Savoir de la corruption | WChosenChainedAllAnomaly | OK |
| **Geôlier** | chosen | support | 2 | Chaînes de la vérité | WChosenChainedAllAnomaly | OK |
| **Dryade** | chosen | detective | 1 | Savoir de la corruption, Bénédiction | WChosenChainedAllAnomaly | OK |
| **Oracle** | chosen | detective | 1 | Vision de l'impossible | WChosenChainedAllAnomaly | OK |
| **Technomancien** | chosen | detective | 1 | Balises personnelles | WChosenChainedAllAnomaly | OK |
| **Repenti** | chosen | tracker | 1 | Vision Maudite | WChosenChainedAllAnomaly | OK |
| **Traqueuse** | chosen | tracker | 1 | Observation Clandestine, Prime Prioritaire | WChosenChainedAllAnomaly | OK (RoleID `ChasseuseDePrime`, redesigned) |
| **Incomplet** | chosen | masked | 1 | Réincarnation | WChosenChainedAllAnomaly | OK (replaces deprecated Omniscient) |
| **Messager** | chosen | innocent | 1 | Lié par l'encre | WChosenChainedAllAnomaly | OK |
| **Orpheline** | chosen | innocent | 1 | Manque d'affection, Paranoïa de la corruption | WChosenChainedAllAnomaly | OK |
| **Uges** | chosen | masked | 2 | — | WChosenChainedAllAnomaly | `[WIP]` no powers |
| **Gardien** | chosen | detective | 1 | — | WChosenChainedAllAnomaly | `[WIP]` no powers |
| **Omniscient** | chosen | tracker | 3 | — | WChosenChainedAllAnomaly | **Deprecated** — superseded by Incomplet |
| **Robot** | marginal | innocent | 3 | Omniscience, Messages infini | WOmniscienceHackedCharacter + WMarginalIsChainedWin | OK (solo: hacks a target's role, or wins if chained) |

#### Attribution pool (per-match)

Which roles can actually be dealt is driven by `RoleAttributionState.roleAttributionDictionary`. Each entry has `roleToAttribute` (count) and `canBeFake` (eligible as a fake/decoy card). In the shipped SO, **all `roleToAttribute` = 0** — counts are configured **per match in the lobby** (`RoleAttributionSettingTab`), not hard-coded. The dictionary currently holds **13 roles**:

> Mage Occulte (`canBeFake = 0` — always a real card), Abyss, Robot, Technomancien, Croupière, Orpheline, Dr Gloubi, Dryade, Messager, Repenti, Oracle, Traqueuse, Incomplet. *(all except Mage Occulte are fake-eligible.)*

The other 5 pool roles are **not** in the attribution dictionary, so they cannot currently be dealt: Uges / Gardien / Moork `[WIP]`, Omniscient *(deprecated)*, and **Geôlier** — which is otherwise functional (has Chaînes de la vérité, sits in the awakening order). [NOTE FOR DESIGNER: is Geôlier's exclusion from the attribution pool intentional, or an oversight?]

#### Awakening (night) order

Server-authoritative layer order from `AwakeningState.awakeningOrder` (12 layers). Roles in the same layer wake simultaneously; each layer's timer = the longest `maxWaitTime` among its powers (active powers = 60s).

1. Uges `[WIP]`
2. Incomplet · Messager
3. Technomancien
4. Repenti
5. Abyss
6. Mage Occulte
7. Geôlier
8. Oracle · Gardien `[WIP]` · Dryade · Dr Gloubi · Orpheline
9. Traqueuse
10. Robot
11. Omniscient *(deprecated)*
12. Croupière

> Moork (anomaly) is **not** in the awakening order — consistent with its `[WIP]` status.

#### Powers — structural overview

25 power prefabs. Each is a `Power`-derived `NetworkBehaviour`; targeting is gated by a `TargetIncludeFlags` bitmask (Self / Anomaly / Marginal / Chosen / Corrupted / Blessed / Chained / Healed / Fake). Powers are **passive** (always-on, no awakening) or **active** (used during the owner's awakening layer, default 1 use, `maxWaitTime` 60s).

- **Passive:** Auto Corruption, Corruption Insight, Savoir de la corruption, Paranoïa de la corruption, Observation Clandestine, Oeil du néant, Messages infini, Héritage, Abattre le portail (script `PNothing`).
- **Active:** Bénédiction, Lié par l'encre, Mélange des cartes, Enchaîné par les Ombres, Marque corruptrice, Vision Maudite, Soin Baveux, Prime Prioritaire, Manque d'affection, Omniscience, Balises personnelles, Réincarnation, Corruption ciblée, Vision de l'impossible. Chaînes de la vérité (`maxWaitTime` 0).
- **Étreintes des ombres** (Mage Occulte) is the only power carrying a power-component: **`PCChainer` (`maxChain` = 2)** — on success it can re-fire up to 2× (the multi-corruption combo). It has 2 uses.
- Corruption ciblée and Marque corruptrice share the script `PCorruptingMark`.

#### Power glossary (effects + implementation status)

Effects below are read directly from the `P*.cs` scripts (faithful, not inferred). Most active powers run a **selection flow** (pick character, pick role, or both via `SelectionFlowService`) and grant a **personal info reveal** to the owner only (`gameInfoRevealer` — `isRoleRevealed` / `isCorruptRevealed`, `RevealLevel.Personal`), reinforcing the investigation pillar. The **Status** column is a dev checklist: `OK` = implemented, `TODO` = explicit in-code TODO, `WIP` = unfinished.

| Power (FR) | Role(s) | Type | Effect (from code) | Status |
|---|---|---|---|---|
| **Auto Corruption** | Mage Occulte, Abyss | passive | At game start, corrupts the owner (anomalies begin `isCorrupted`). | OK |
| **Corruption Insight** | Mage Occulte, Abyss | passive | At start, reveals every player's corruption status to the owner. | OK |
| **Oeil du néant** | Abyss | passive | At start, opens the **Anomaly-only chat** to all anomaly players (shared team channel). | OK |
| **Héritage** | Abyss | passive | If the configured role (`roleForLegacy`) gets chained, owner inherits a configured power (`legacyPower`). | OK |
| **Savoir de la corruption** | Dr Gloubi, Dryade | passive | At start, flags all players so that revealing their role also surfaces corruption (`forceCorruptOnRoleRevealed`) to the owner. | OK — [NOTE FOR DESIGNER: confirm exact intended reveal semantics] |
| **Paranoïa de la corruption** | Orpheline | passive | At start, reveals the owner's own corruption status to themselves, flagged falsified (`true`) — deliberately unreliable self-info. | OK — [NOTE FOR DESIGNER: confirm the falsified-flag intent] |
| **Observation Clandestine** | Traqueuse | passive | Each awakening, auto-reports to the owner the **count of players who targeted** a configured role (`targetRoleID`). | OK |
| **Messages infini** | Robot | passive | On power reparent (when inherited), sets owner `messageLeft = int.MaxValue` (unlimited messages). | OK — trigger is reparent-only; confirm intended carrier |
| **Abattre le portail** | Mage Occulte | passive | `PNothing` — no own effect; the Mage Occulte endgame resolves in `TakeDownThePortalState`. | Placeholder by design |
| **Étreintes des ombres** | Mage Occulte | active | Pick character + guess their role. Correct → corrupt them + reveal their role/corruption to owner. Carries **PCChainer (maxChain 2)** → up to 2 corruptions/night on success. | TODO (`OnCorruptionSuccessful()` empty `//TODO: THIS`) |
| **Corruption ciblée** / **Marque corruptrice** | Mage Occulte / (shared `PCorruptingMark`) | active | Pick a character → corrupt them + reveal their corruption to owner. Concentrated effect (`PCConcentrated`) → also reveal last-corrupted's role. | OK |
| **Enchaîné par les Ombres** | Abyss | active | Pick character + guess role. Correct → reveal role to owner; if target is **chosen** → add to chaining list (gets chained). | OK |
| **Soin Baveux** | Dr Gloubi | active | Pick character + guess role. Correct guess → if target corrupted/healed, **heal** (clear corruption, set `isHealed`) + reveal role to owner. Public announce at night end. | OK |
| **Bénédiction** | Dryade | active | Pick character + guess role. Correct → heal + reveal role to owner + set `isBlessed` (anomaly/Dryade who target a blessed player get a Blessing card effect). | OK |
| **Vision Maudite** | Repenti | active | Pick character → corrupt them, tell owner whether target is an Élu (chosen) or not. **Cost: corrupts the owner too.** | OK |
| **Prime Prioritaire** | Traqueuse | active | Pick character. If **Robot** → eliminate it (`isEliminated`, public role reveal). If not → owner is chained at end of awakening (penalty). | TODO (`//TODO: do actual elimination logic & visual`) |
| **Manque d'affection** | Orpheline | active | Pick character → "contact" them; target sees "X came to see you" + faction sound. If target is chosen, reveals owner's role to the target. | OK |
| **Omniscience** | Robot | active | Pick character → "hack" them (store id), reveal their role to owner. Win via `WOmniscienceHackedCharacter` if the hacked target is later chained **and** chosen. | TODO (`//TODO: rework win condition to use power instead of WinningCondition`) |
| **Réincarnation** | Incomplet | active | Pick a **role** (not own) → inherit **all** that role's powers; power becomes passive after use. | OK |
| **Chaînes de la vérité** | Geôlier | active | Pick character → if target is **anomaly**, add to chaining list + public chat. (`maxWaitTime` 0.) | OK |
| **Vision de l'impossible** | Oracle | active | Pick **2 characters + 2 roles** → owner is told whether one of the chosen characters matches one of the chosen roles. | OK |
| **Mélange des cartes** | Croupière | active | Pick a role; fake card → told it's fake. Else guess which character holds it: correct → reveal role; wrong → reveal who that role targeted. | OK |

**Power components** (combinable, attached to a power prefab): `PCChainer` (re-fire up to `maxChain` on success — used by Étreintes des ombres), `PCConcentrated` (`IConcentratedPowerEffect` — extra reveal, used by Corruption ciblée/Marque corruptrice), `PCReparentOnChain` and `PCPowerUnlockWhenChain` (chaining-driven power transfer/unlock; not currently attached to any shipped power prefab).

**Cross-cutting systems referenced by powers** (for architecture, not GDD-level): `RoleTargetSystem` (records who targeted whom — feeds Observation Clandestine, Mélange des cartes), `GameInfoRevealer` (`RevealLevel` Personal/Public reveal of role/corruption), `ChainingManager.AddCharacterToChainingList`, `CardEffectManager`, `SelectionFlowService` (pick flows).

### Hidden Information Model

_TBD._

### Phase Structure (Day/Night equivalent)

See **Core Gameplay → Core Gameplay Loop** for the authoritative 13-state machine. In social-deduction terms: **Awakening = night** (roles wake in `AwakeningState.awakeningOrder` and use powers), the **Vote → Chaining 2** block = the **day** elimination, and the Recap states bridge them with public summaries.

### Voting / Accusation System

_TBD. Code: `VoteState` (`voteDuration` timer, skip option, ends early when all eligible voted) → most-voted player → `ChainingManager` → `ChainingState2` (vote elimination). Detailed rules — tie handling, abstentions, vote visibility — to be read from `VoteState`/`ChainingManager`._

### Win Conditions per Faction

Coded `WinningCondition` objects, evaluated in the `VictoryConditionCheckState` passes. Faction wins are global; marginal wins are per-player (`ownerClientId`).

- **Anomalies (`WAnomalyCorruption`):** every non-fake player `isCorrupted`. Chaining also sets corrupted, so eliminations feed this track.
- **Élus (`WChosenChainedAllAnomaly`):** all anomaly-faction players `isChained`.
- **Marginaux (per-player):**
  - `WMarginalIsChainedWin` — the owner wins by getting chained (Jester / Bouffon). Carried by **Robot**.
  - `WOmniscienceHackedCharacter` — owner has the `Omniscience` power, designated a "hacked" target, and that target is `isChained` **and** of the `chosen` faction. Carried by **Robot**.
- **Mage Occulte endgame:** not a coded `WinningCondition` — handled in `TakeDownThePortalState`; on full success the **anomaly faction** wins. `WinningTeam.alone` exists in the enum but is unused by current win logic.

---

## Progression and Balance

This is a **session-based** social-deduction game: a match starts, runs the day/night loop, and ends. There is **no meta-progression, XP, leveling, or persistent economy** in the current build.

### Player Progression

None within a match beyond the unfolding information state (what each player has learned / revealed) and role-state changes (corrupted, chained, eliminated). No score or unlock system observed.

### Difficulty Curve

Per-role intrinsic difficulty is authored on each role as `roleDifficulty` (1–3) — a UX/onboarding hint, not a mechanical modifier. Current spread: **diff 1** (easy): Dryade, Gardien, Oracle, Technomancien, Repenti, Traqueuse, Incomplet, Messager, Orpheline. **diff 2**: Abyss, Moork, Dr Gloubi, Geôlier, Uges. **diff 3** (hard): Mage Occulte, Croupière, Omniscient, Robot. Match difficulty/feel is otherwise an emergent function of the role mix.

### Economy and Resources

No currency. The closest "resource" mechanics are per-power **uses** (`maxPowerUse`, default 1, regenerated per awakening via `powerUseRegenPerAwakening`) and per-player **messages** (`messageLeft`, default 1 per turn; Robot's *Messages infini* can lift this to unlimited).

### Balance lever — role attribution (per match)

Balance is driven almost entirely by the **role mix per match**, configured in the lobby (`RoleAttributionSettingTab` → `RoleAttributionState.roleAttributionDictionary`), not hard-coded (all shipped counts = 0). Key rules from code:

- Host sets `roleToAttribute` per role; roles with count 0 are excluded from the deal.
- Slots beyond the player count are filled with **fake cards** drawn from roles flagged `canBeFake` — decoys on the board that are not held by any real player. (Mélange des cartes / Soin Baveux interactions account for fake cards.)
- **Mage Occulte is `canBeFake = 0`** — it is always a real, dealt role, never a decoy.
- Current attributable pool = **13 roles** (see Roles and Factions → Attribution pool). [NOTE FOR DESIGNER: recommended/standard role-count presets per player-count are not defined in code — capture intended presets here if they exist.]

---

## Level Design Framework

> Traditional level design does **not** apply — the game is single-arena. Captured here for completeness.

### Level Types

A **single shared 3D board** (the `GameScene`): a ring of player **cards** viewed through Cinemachine board cameras, with a 2D **smartphone-style OS** UI overlaid (chat, notes, info tables, role-target). `RoomFog` provides atmosphere. No multiple maps or arenas in the current build.

### Level Progression

None. Each match is a self-contained session on the same board; "progression" within a match is the unfolding information/role state (see Progression and Balance), not spatial.

---

## Art and Audio Direction

### Art Style

Mystical **Portal / Corruption** theme reskinning the Werewolf/Mafia archetypes (Élus / Anomalies / Marginaux) to differentiate from classic wolves-and-villagers. Presentation = a **3D board of character cards** (URP, Cinemachine cameras, `RoomFog` ambiance) under a **2D smartphone OS UI** (uGUI 2.0 + TextMeshPro + SoftMaskForUGUI). Chained players get a **card-flip reveal** animation; powers use DOTween-driven card effects.

[NOTE FOR DESIGNER]: detailed visual direction — palette, card art style, character/portal designs, mood references — is owned by the GD and not captured in code.

### Audio and Music

Audio is **FMOD only**, via `AudioSystem/GameAudioManager` (never `AudioSource` for gameplay sound). Banks live in `Assets/FMODBanks/`. Powers trigger event-based cues — e.g. *Manque d'affection* plays a **faction sound** to the contacted target.

[NOTE FOR DESIGNER]: music and ambience direction (themes per phase/faction, stingers, mix intent) not captured.

---

## Technical Specifications

### Performance Requirements

- **Desktop / Steam:** 60 FPS target → **16.6 ms/frame** budget.
- **Mobile (Android, Adaptive Performance):** 60 FPS target, **30 FPS floor** under thermal throttle → 33 ms/frame budget. Quality level changes at runtime via `AdaptivePerformance.WarningLevelEvent`.
- **Network tick rate:** ~**30 Hz** (`NetworkConfig.TickRate`). RPCs batched per tick — never fired per `Update()` frame; `NetworkVariable` mutated by event, never per frame.
- Heavy assets (cards, role art, FMOD banks, Addressables deps) **preloaded during the Lobby phase**, not mid-gameplay — `Resources.Load`/sync loads banned in the gameplay path.

### Platform-Specific Details

- **Engine:** Unity **6000.2.6f2** (exact), **URP**.
- **Networking:** NGO **2.6.0**, server-authoritative — Host owns all state mutation; clients propose via `ServerRpc`. Signature feature: **Gateway RPC** — Host locally simulates extra identities (`clientId >= 100`, `DevIdentityController`) for full-lobby solo debug (`GetSafeRpcTarget` / `IsLocalOrSimulated`).
- **Transport:** **Facepunch (Steam)** in shipped builds (vendored asmdef); **local simulated gateway** for solo debug. `NetworkTransportDetector` selects at runtime. Steam-specific code gated (`UNITY_STANDALONE_WIN`); never assume `SteamClient.IsValid`.
- **Lobby / auth:** Unity Services (Core, Multiplayer, Authentication) + Steam lobby, routed through `LobbyManager` / `LobbyCreationSettings` — Unity Services lobby and Steam lobby kept in sync.
- **Primary platform:** Windows / Steam. **Secondary:** Android (Addressables Android variant + `com.unity.feature.mobile`); iOS reachable via the same stack.
- **Input:** Unity Input System 1.14.2 — multi-platform action maps (touch / mouse / keyboard / gamepad) required for every action.

### Asset Requirements

- **Addressables** 2.7.2 (+ Android 1.0.6) for heavy prefabs (cards, role art); Addressables content build must precede mobile player build.
- **FMOD** banks in `Assets/FMODBanks/`, compiled from FMOD Studio — never regenerated at runtime; stale banks → `EVENT_NOTFOUND`.
- [NOTE FOR DESIGNER / dev]: per-asset budgets (texture sizes, bank sizes, build-size ceiling) not yet defined.

---

## Development Epics

### Epic Structure

_Not defined in this GDD._ Epic/milestone planning is a design-ownership decision (priority, scope, sequencing) that belongs to the GD owner, not inferred from code. The build's open implementation work (WIP roles, in-code `TODO` powers) is already captured descriptively in **Roles and Factions** and the **Power glossary** (Status column) — that checklist is the factual basis for whatever epics the GD owner chooses to define here later.

---

## Success Metrics

### Technical Metrics

Measurable engineering targets (from project-context):
- Sustained **60 FPS desktop** / 60-with-30-floor mobile across a full match loop.
- Network stability over a complete match at ~30 Hz tick — no RPC desync, no `NetworkVariable` saturation.
- Zero `EVENT_NOTFOUND` (FMOD) and zero unhandled RPC-before-spawn errors in a shipped build.

### Gameplay Metrics

[NOTE FOR DESIGNER]: gameplay KPIs are not defined in code and are owned by the GD. Candidate metrics to capture: match completion rate, **per-faction win balance** (Élus / Anomalies / Marginaux), average match length, and **dead-time reduction** (engagement of chained players vs. classic Werewolf spectators).

---

## Out of Scope

Deliberately absent from the current build (v1.0 scope):
- **No meta-progression** — no XP, leveling, persistent economy, unlocks, or cross-match scoring.
- **No level progression / multiple maps** — single shared board.
- **WIP roles not playable as designed:** Moork, Uges, Gardien (no powers in SO; Moork also absent from the awakening order). `Omniscient` is **deprecated** (superseded by Incomplet) and excluded from the attribution pool.
- **Incomplete powers (in-code TODO):** *Étreintes des ombres* corruption-success effect, *Prime Prioritaire* elimination logic/visual, *Omniscience* win-condition rework. (These are dev-completion items, tracked in Development Epics, not permanently cut.)
- `WinningTeam.alone` enum value — present but **unused** by current win logic.

[NOTE FOR DESIGNER]: explicit post-launch deferrals (features intentionally pushed beyond v1.0) are not captured — confirm.

---

## Assumptions and Dependencies

### Hard dependencies
- **Unity 6000.2.6f2** (exact, no upgrade without team coordination), **URP**.
- **NGO 2.6.0** ↔ Unity Multiplayer Tools (paired version bump).
- **Facepunch (Steam) transport** (vendored under `Assets/Scripts/Facepunch/`, not UPM) + **Steam platform** + **Unity Services** (Core, Multiplayer, Authentication) for lobby/auth.
- **FMOD Studio** (external bank authoring), **UniTask**, **DOTween**, **Addressables** 2.7.2 (+ Android 1.0.6), **Cinemachine** 3.1.5, **Input System** 1.14.2.

### Assumptions index
- [ASSUMPTION] Target audience profile inferred (Werewolf / Mafia / Among Us players seeking deeper roles + less dead time) — Executive Summary.
- [ASSUMPTION] Background rationale (built to fix classic-Werewolf dead time + shallow roles) — Goals and Context.

### Open `[NOTE FOR DESIGNER]` index (for the GD owner)
1. Geôlier excluded from the attribution pool — intentional or oversight?
2. Standard role-count presets per player-count — do they exist?
3. *Savoir de la corruption* / *Paranoïa de la corruption* — exact intended reveal semantics (incl. the falsified-flag intent).
4. *Messages infini* — reparent-only trigger intended? Confirm carrier.
5. Detailed art/visual direction (palette, card style, references).
6. Music / ambience direction.
7. Gameplay KPIs / success metrics.
8. Formal project goals + commercial targets.
9. Explicit post-launch deferrals.
