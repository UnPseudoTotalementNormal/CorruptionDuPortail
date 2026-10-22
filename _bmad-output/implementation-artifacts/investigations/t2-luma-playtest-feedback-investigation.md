# Investigation: T2 Luma la tricheuse, GD playtest feedback of 2026-10-18

## Hand-off Brief

1. **What happened.** The GD's three remarks on T2 are, in order: (1) a real gap, Luma's Mélange des cartes drops every
   passive power, and Ugës's only power (Marque d'Hurluberluges) is passive, so a factice Ugës yields nothing
   (Confirmed in code and in an autoplay run); (2) a real gap, a factice found by Luma is never written into her
   knowledge, so it never turns grey in the top bar like the factices the anomalies are told (Confirmed); (3) already
   true in code, Luma's copies are coated in slime exactly like Ugës's (same one-shot config), but nobody has seen it in
   play: the GD could not copy Ugës, and no autoplay capture ever caught a Luma copy on the table (Deduced).
2. **Where the case stands.** Concluded. Both gaps are local to `PCardsShuffling.GrantCopyFromFakeRole` / the fake
   branch of `OnCharacterBarObjectClickedRpc`; the late-spawned-Marque path it needs already works for l'Incomplet.
3. **What's needed next.** One small `gds-quick-dev` change: let Luma copy the Marque (exception to the "active only"
   filter) and send `isFakeRevealed` to Luma when she lands on a factice; plus an autoplay capture of a Luma copy so
   point 3 is shown, not assumed.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | Discord board T2 « Remodifier Luma la tricheuse », thread 1524509553805496490, message of 2026-10-18 21:56 (Wouh, GD) |
| Date opened      | 2026-10-20 |
| Status           | Concluded |
| System           | Unity 6000.5.0f1, `origin/Dev` f0fa3c00 (local Dev 6 behind, none of the 6 commits touch powers); last player build = Main 55006db8 (2026-10-16), contains slime PR #118 |
| Evidence sources | Code, SO / prefabs, autoplay run logs (2026-10-08, 2026-10-13), project memories, prior case files |

## Problem Statement

Verbatim (GD, 2026-10-18):

> 1) Luma n'a pas réussi à voler le pouvoir d'Ugës. Le pouvoir n'est juste pas copié.
> 2) Les rôles factices n'apparaissent pas en haut une fois découverts, comme ça apparaît pour les anomalies.
> 3) L'idée serait que tous les rôles copiés par Luma devraient être visuellement comme Ugës, baveux, comme ça ça
>    devient universellement le visuel des pouvoirs temporaires.

GD rule for Luma (power text, `Assets/Prefabs/Powers/CardsShuffling.prefab`, `powerDescription`): « Elle choisit un rôle
élu : si le rôle est présent dans la partie, Luma doit deviner à quel joueur il appartient. Si le rôle est absent de la
partie, Luma obtient une copie de ses pouvoirs à utilisation unique. »

## Evidence Inventory

| Source | Status | Notes |
| ------ | ------ | ----- |
| Luma power code | Available | `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` |
| Ugës role / power data | Available | `Assets/ScriptableObjects/Characters/Uges.asset` (faction chosen, 1 power), `Assets/Prefabs/Powers/MarqueHurluberluges.prefab` (`isPassive: 1`) |
| Copy eligibility kernel | Available | `Assets/Scripts/Domain/StolenPowerSelector.cs` |
| Factice knowledge + top bar | Available | `GameIntroductionState.cs`, `CharactersBarObject.cs`, `RoleCardController.cs` |
| Slime code | Available | `PowerBarObject3D.cs`, `SingleUseSlimeMark.cs`, `Power.ConfigureAsOneShotStolenCopy` |
| Autoplay run, Luma on factice Ugës | Available | `.claude/worktrees/bridge-cse_01CrCeVBmaqCtry9M2iaqvw8/AutoplayRuns/net-20261008-000355-c3-seed777/host.log` |
| Autoplay runs, Luma copies a factice | Available | `.claude/worktrees/avec-le-better-0acddf/AutoplayRuns/net-20261013-155835-c3-seed4304`, `…171826-c3-seed4307` |
| The GD's own game (log, composition, build) | Missing | See Missing Evidence |

## Timeline of Events

| Time | Event | Source | Confidence |
| ---- | ----- | ------ | ---------- |
| 2026-07-17 | Copied Réincarnation skips passives (« option A »): a passive is never spent, so a one-shot copy of it would live forever | `PReincarnation.cs:42-48` | Confirmed |
| 2026-10-07 | « Luma trouvant un faux Ugës = rien » noted as a design observation for the GD, not fixed (PR #114) | memory `project_uges_copies_2026_10_07` | Confirmed |
| 2026-10-07 | Factices known to anomalies, grey in the top bar (`isFakeRevealed`, PR #115 b8236b40) | `git log -S isFakeRevealed` | Confirmed |
| 2026-10-07 | Slime on every `isStolenCopy` power (PR #118 27bfc373) | memory `project_t2_single_use_slime` | Confirmed |
| 2026-10-08 00:03 | Autoplay: Luma (bot 100) picks factice Ugës, gets « aucun pouvoir actif à copier » | run net-20261008-000355, host.log ~line 1988 | Confirmed |
| 2026-10-16 | Main build 55006db8, contains #114, #115, #118 | `git merge-base --is-ancestor 27bfc373 origin/Main` | Confirmed |
| 2026-10-18 21:56 | GD's feedback | Discord T2 | Confirmed |

## Confirmed Findings

### Finding 1: Ugës is a chosen role whose only power is passive

**Evidence:** `Assets/ScriptableObjects/Characters/Uges.asset` (`factionType: 1` = chosen, single power guid
`204fb94d…` = `MarqueHurluberluges.prefab`); `Assets/Prefabs/Powers/MarqueHurluberluges.prefab:693` `isPassive: 1`.

**Detail:** Ugës passes Luma's "chosen role" picker rule (`PCardsShuffling.cs:54-58`, `:120`), so his card can be
picked. Everything then depends on the copy filter.

### Finding 2: Luma's copy filter drops passive powers

**Evidence:** `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:186-200` builds
`PowerCandidate(true, false, _isPassive, _isCopied)`; `Assets/Scripts/Domain/StolenPowerSelector.cs:112`
`IsEligible => OwnerIsChosen && !OwnerIsUgues && !IsPassive && !IsCopiedPower`.

**Detail:** For a factice Ugës the only candidate is passive, `_picks.Count == 0`, Luma receives
`noActivePowerToCopyMessage` (« Le rôle sélectionné n'est pas en jeu, mais n'a aucun pouvoir actif à copier. »,
`PCardsShuffling.cs:176-177`) and the use is consumed (`OnUsed()`, `:97`).

### Finding 3: The run log shows it happening

**Evidence:** `net-20261008-000355-c3-seed777/host.log`: `roles.fakes Le Messager, Ugës, l'Imposteur, Dr Gloubi`, then
`input.click picker #13 role:Ugës, l'Imposteur` → `[LUMA] Mélange des cartes : fausse carte élue sans pouvoir actif
copiable.` → `chat.recv … Le rôle sélectionné n'est pas en jeu, mais n'a aucun pouvoir actif à copier.`

**Detail:** Matches the GD's « le pouvoir n'est juste pas copié ». The autoplay sweep encodes this as the expected
result: `tools/autoplay/sweep_copies.py:78` skips Ugës in the `luma` family, `:139` « his Mélange picks a factice Ugës
(no active power) ».

### Finding 4: A late-spawned Marque copy already works (l'Incomplet path)

**Evidence:** `PMarqueHurluberluges.cs:48-50` (`_hasStolen`, « OnGameStartedServer can be reached twice for a
late-spawned power »), `:167-169` (« the night of the copy for an Incomplet who took the Marque »);
`Assets/Scripts/GameLogic/PowerManager.cs:52-62` calls `OnGameStartedServer()` on any power spawned after game start;
`sweep_copies.py:9` (« l'Incomplet … reincarnates into Ugës (gets a Marque, steals) »).

**Detail:** A Marque given mid-game steals up to 3 one-shot copies at once, locked that night, then one per night. The
owner's own powers are excluded (`ownerIsUgues` = owner slot, `PMarqueHurluberluges.cs:143`), so a Marque on Luma would
not steal her own Mélange.

### Finding 5: Only the anomaly hint ever writes `isFakeRevealed`

**Evidence:** the single writer is `Assets/Scripts/GameLogic/GameStates/GameIntroductionState.cs:64`
(`TellAnomaliesTheirFakeRoles`). The fake branch of Luma, `PCardsShuffling.cs:92-99`, adds the slot to
`discoveredClientIds` and grants the copy, nothing else.

**Detail:** The top bar greys a factice only when `isFakeRevealed > False` for the local viewer
(`Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs:232-238`, grey material + tooltip « Rôle factice »);
the role card adds its « rôle factice » line on the same test (`Assets/Scripts/UI/RoleCard/RoleCardController.cs:161-163`).

### Finding 6: Runtime knowledge confirms Luma learns nothing about the factice

**Evidence:** same run net-20261008-000355, every `[AUTOPLAY] knowledge` dump: `fake=` entries exist only for viewer 0
(the anomaly Mage Occulte: `0>…613 fake=10 role=Dr Gloubi`, `0>…615 fake=10 role=Le Messager`); viewer 100 (Luma) has
none after picking factice Ugës.

### Finding 7: Luma's copies carry the same one-shot flag as Ugës's

**Evidence:** `PCardsShuffling.cs:206` `InheritMarqueBudget(Power.ConfigureAsOneShotStolenCopy)` →
`Assets/Scripts/Characters/Powers/Power.cs:665-676` sets `isStolenCopy = true`; the slime follows that flag
(`Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs:76`, `:112-115`, `:145-157`), and its doc names « Luma's
fake-card copy » (`SingleUseSlimeMark.cs:13`).

**Detail:** Ugës's Marque copies go through the same `ConfigureAsOneShotStolenCopy` (`PMarqueHurluberluges.cs:217-219`),
so in code there is one visual for every temporary copy, which is what the GD asks for.

## Deduced Conclusions

### Deduction 1: Point 1 is the "active only" rule, not a broken copy

**Based on:** Findings 1, 2, 3.

**Reasoning:** Luma copies only from a factice; a factice Ugës has no active power; the filter returns nothing. If Ugës
was instead played by someone, the GD rule sends Luma into the guess flow (`PCardsShuffling.cs:101-102`), which never
copies either. In both cases the code does what it was written to do; the GD expects otherwise.

**Conclusion:** A design gap the GD has now decided: Luma should be able to copy Ugës. The Réincarnation precedent
(« option A », passives skipped because a one-shot passive never despawns) is the reason the filter exists; for the
Marque it does not apply the same way, since its effect is a one-off steal and its stolen copies are themselves
one-shot.

### Deduction 2: Point 3 looked missing because the GD never got a Luma copy to look at

**Based on:** Findings 3, 7, the Main build containing PR #118.

**Reasoning:** The slime is in the build he played. His only reported Luma attempt was Ugës, which gave no copy, so no
slime could show. Luma also tends to use the copy in the same turn (run 155835: `power.start 3 … Soin Baveux` right
after the grant), and a spent copy despawns, so the slime is on the table for seconds.

**Conclusion:** The premise "Luma's copies are not slimy" is contradicted by the code; it is unverified on screen.

## Hypothesized Paths

### Hypothesis 1: Luma copied a factice Ugës and got nothing

**Status:** Confirmed (mechanism); the GD's own game is not available.

**Theory:** In the GD's game Ugës was a factice, Luma picked it, the filter found no active power.

**Would refute:** Ugës was played by someone in that game (then Luma had to guess, a different flow).

**Resolution:** Mechanism reproduced in autoplay run net-20261008-000355. Which branch the GD hit stays open (Missing
Evidence), but neither branch copies anything today.

### Hypothesis 2: Luma's copies are not slimed

**Status:** Refuted in code, unverified on screen.

**Theory:** Only Ugës's copies get the slime.

**Would confirm:** a capture of Luma's power bar right after a factice copy, `coat=False`.

**Resolution:** Same flag, same component (Finding 7). The autoplay slime scenarios only cover Ugës
(`tools/autoplay/scenarios/slime-copies-uges.json`, `slime-copies-uges-host.json`); in runs 155835 and 171826 the
`coat=True` lines belong to Ugës, Luma's copy was spent before any capture.

### Hypothesis 3: "En haut" means the top characters bar

**Status:** Confirmed.

**Resolution:** The anomaly hint shows only in the top bar (grey portrait + « Rôle factice » tooltip) and the role card
opened from it (Finding 5); the GD's « comme ça apparaît pour les anomalies » matches that exact display.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| The GD's game: composition, whether Ugës was factice or played | Tells which branch he hit for point 1 (no change to the conclusion: neither copies) | Ask him; or his `Player.log` |
| A capture of a Luma copy on the table | Turns point 3 from Deduced into Confirmed | Autoplay `copies-luma-<role>` with a capture right after the grant (hold the copy, `-autoplay-hold-power`) |
| Which "discoveries" the GD means beyond Luma | Scope of point 2 (l'Incomplet's Réincarnation on a factice also learns nothing) | Ask him |

## Source Code Trace

| Element | Detail |
| ------- | ------ |
| Point 1 origin | `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:188-200` (`_isPassive` fed to the kernel), rule in `Assets/Scripts/Domain/StolenPowerSelector.cs:112` |
| Point 2 origin | `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:92-99` (fake branch, no reveal); only writer `GameIntroductionState.cs:64` |
| Point 3 path | `Power.cs:665-676` → `PowerBarObject3D.cs:76,112,145` → `SingleUseSlimeMark.Apply` |
| Trigger | Luma picks a factice chosen role in her role picker (`StartUse` → `OnRolePicked` → `OnCharacterBarObjectClickedRpc`, server) |
| Related files | `PMarqueHurluberluges.cs`, `PReincarnation.cs`, `PowerManager.cs:52-62`, `GameInfoRevealer.cs:145` (`SendRevealLevelRpc`), `CharactersBarObject.cs:232-260`, `RoleCardController.cs:155-170`, `tools/autoplay/sweep_copies.py` |

## Conclusion

**Confidence:** High for points 1 and 2 (code + runtime log), Medium for point 3 (code only, no capture).

1. Luma cannot copy Ugës because her copy keeps active powers only and the Marque d'Hurluberluges is passive. Known
   since 2026-10-07 and left for the GD; he has now answered: he wants it to work.
2. A factice Luma lands on is never added to her knowledge, so the top bar never greys it for her; only the anomalies'
   start-of-game hint writes `isFakeRevealed`.
3. Luma's copies already get the slime through the same flag as Ugës's: the « visuel universel des pouvoirs
   temporaires » exists. He has not seen it because of point 1 and because a copy used right away lives only seconds.

## Recommended Next Steps

### Fix direction

- **Point 1 (eligibility):** in `GrantCopyFromFakeRole`, also accept the Marque d'Hurluberluges (passive but a one-off
  grant). Its spawn after game start already runs the steal (`PowerManager.OnPowerSpawned`); give it
  `ConfigureAsOneShotStolenCopy` + `InheritMarqueBudget` like the other copies, and update the chat message (« vous
  copiez temporairement sa Marque… »). Keep Réincarnation's option A untouched. Open for the GD: should Luma's stolen
  copies follow the same « pas la nuit du vol, puis un par nuit » budget as Ugës? (Today the Marque code locks them the
  night of the steal, so yes by default.) The Marque copy itself stays in her powers as an invisible passive (the bar
  skips passives, `PowersBar.cs:188,211`), harmless after the steal.
- **Point 2 (knowledge):** in the fake branch of `OnCharacterBarObjectClickedRpc`, call
  `gameInfoRevealer.SendRevealLevelRpc(_clientIdClicked, nameof(CharacterInfoReveal.isFakeRevealed),
  RevealLevel.Personal, ownerClientId.Value, false)`. The ledger re-sends it on rejoin. Same call to consider in
  `PReincarnation` when l'Incomplet picks a factice (ask the GD).
- **Point 3 (visual):** no code. Show it: autoplay `copies-luma-*` with the copy held and a capture, then send the
  image to the GD.
- **Tests:** EditMode for the new eligibility rule; update `sweep_copies.py` (`:78` skip of Ugës, `:139` expected
  "nothing") so it asserts the Marque copy; autoplay `luma-copies-uges` checking `power.copies` on Luma + a
  `knowledge 100>fake fake=10` line.

### Diagnostic

None needed for the mechanism. Ask the GD whether Ugës was a factice in his game, only to close Hypothesis 1 fully.

## Reproduction Plan

`python -X utf8 tools/autoplay/sweep_copies.py --family combos` (scenario `incomplet-into-luma-copies-uges`), or a
single run with `-autoplay-force-roles <Luma> -autoplay-role-holder client -autoplay-force-fakes <Ugës>
-autoplay-target-map lange=<Ugës>`. Expected today: `[LUMA] … sans pouvoir actif copiable`, no `power.copies` on Luma,
no `knowledge <luma>><fake> fake=` line. After the fix: three slimed copies on Luma (`coat=True`), locked until the next
day, and the factice Ugës grey in her top bar.

## Side Findings

- The use is consumed even when nothing is copied (`PCardsShuffling.cs:97`): Luma loses her turn on a passive-only
  factice. Becomes moot for Ugës after the fix; may still apply to other passive-only roles (Confirmed in code).
- l'Incomplet's Réincarnation on a factice also writes no `isFakeRevealed` (Deduced: single writer, Finding 5).
- Older memory note « Luma copie UN pouvoir actif au hasard » is outdated: since PR #115 she copies every active power
  (`SelectAllEligible`, `PCardsShuffling.cs:195`) (Confirmed).

## Follow-up: 2026-10-20

### New Evidence

- Poyo on point 1: the Marque d'Hurluberluges is an active power for the GD. Confirmed the `isPassive: 1` flag is an
  implementation choice since 7bd162ef (2026-07-09, story `archive/specs/role-ugues-marque-hurluberluges.md:60`, "never
  in the bar"), never a GD rule; the stolen copies are the active part. A factice Ugës never steals
  (`PowerManager.cs:89` skips factice owners), so copying it can only mean "Luma gets a Marque that steals for her".
  Poyo: her stolen copies follow Ugës's budget (none the night of the theft, then one per night).

### Additional Findings

- Point 2 fixed on branch `fix/t2-luma-fake-reveal` (base `origin/Dev` f0fa3c00): the fake branch of
  `PCardsShuffling.OnCharacterBarObjectClickedRpc` sends `isFakeRevealed = Personal` to the owner.
- Regression test `CorruptionTests.PCardsShuffling_FakeRolePickedTellsOwnerItIsFake`: red without the fix
  ("Expected: Personal, But was: False"), green with it; PlayMode `CorruptionTests` 10/10, EditMode 757/757.

### Updated Conclusion

Point 2 closed in code (not yet playtested). Point 1 open: exception for the Marque in Luma's copy filter, to code.
Point 3 unchanged (needs a capture).
