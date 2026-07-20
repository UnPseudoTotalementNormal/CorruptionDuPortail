# Investigation — Power use verdict feedback

## Hand-off Brief

Playtest feedback (Discord, 6 joueurs) : les joueurs veulent une confirmation que leur pouvoir a réussi ou raté. Le
codebase **calcule déjà** la correction pour 12 des 25 pouvoirs, mais ne la surface jamais : `PowerOutcome` n'a pas de
champ verdict, et le seul canal client générique (`OnUsedClientRpc` → `StopUse` → `onStopUse`) est outcome-blind et
owner-only. La demande initiale (`onCorrectUse` / `onIncorrectUse` sur chaque pouvoir) dupliquerait 22 fois un verdict
qui existe déjà en amont ; le point d'insertion correct est `PowerOutcome` + le RPC owner existant.

- **Case Info** — slug `power-use-verdict-feedback` · date 2026-07-20 · statut **Active** · type exploration
  (pas de défaut : le comportement demandé n'existe pas)
- **Source** — message Discord de Wouh (playtest famille à 6) relayé par Poyo, + demande `/gds-investigate`

## Problem Statement

Verbatim (Discord, Wouh) : « La chose que les gens ont le plus demandé, c'est d'avoir une confirmation que leur pouvoir
a marché ou non. Donc, l'idéale serait qu'un logo 🚫 apparaissent si on se trompe dans une prediction. Une animation qui
confirme que le pouvoir a bien été envoyé (avec l'orpheline par exemple) ».

Poyo : « Pour l'instant je vais coder le fait que le jeu detecte que le pouvoir a réussi ou non avec le mini feedback
comme ça on poura brancher le vrai feedback dessus quand on se sera décidé ».

Hypothèse utilisateur (**Hypothèse #1**) : « actuellement y'a juste onUsed mais faudrais onCorrectUse/onIncorrectUse ».

## Evidence Inventory

| Catégorie | État | Note |
|---|---|---|
| Source code (Powers engine + Domain decisions) | Available | 25 adapters `Power`, 23 POCOs `IPowerDecision` |
| Source code (client feedback layer) | Available | `PowerBarObject3D`, FMOD `onUsedSound` |
| Playtest report | Partial | résumé Discord seulement ; pas de liste des pouvoirs cités hors Orpheline |
| Tests existants sur le verdict | Missing | aucun test n'assert un verdict de correction en tant que tel |
| Direction de design du feedback visuel (logo 🚫, anim) | Missing | design-owned, explicitement différé par Poyo |

## Confirmed Findings

1. **L'event générique s'appelle `onPowerUsed` / `onPowerUsedServer`, pas `onUsed`.** `onUsed` dans le repo = `onUsedSound`,
   un `EventReference` FMOD. Déclarations : `Assets/Scripts/Characters/Powers/Power.cs:138-139`.
2. **`onPowerUsed` est un `NetworkAction` sans payload**, répliqué à tous les peers
   (`Assets/Plugins/NetworkAction/NetworkAction.cs:63`) mais son unique listener est enregistré sous un garde
   server-only (`Assets/Scripts/GameLogic/PowerManager.cs:54,64`) → aujourd'hui effectivement server-only.
3. **Le seul canal client générique est owner-only et outcome-blind.** `Power.OnUsed` (`Power.cs:279`) → `OnUsedClientRpc`
   (`Power.cs:268-272`, `SendTo.SpecifiedInParams`, ciblé owner via `GetSafeRpcTarget`, `Power.cs:296`) → corps = `StopUse()`
   seul → `onStopUse` (`Power.cs:393`) → `PowerBarObject3D.StopUsePower` (`Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs:62,247`).
   Aucune donnée de résultat ne transite.
4. **`PowerOutcome` n'a pas de champ verdict.** `Assets/Scripts/Domain/Powers/PowerOutcome.cs:11-17` = `Accepted`,
   `Effects`, `UsesConsumed`, `RejectReason`. `Accepted` signifie « la décision a été prise », pas « la prédiction était
   juste » — toutes les décisions porteuses de correction retournent `Accept` sur **les deux** branches.
5. **12 pouvoirs calculent déjà un booléen de correction**, majoritairement dans le POCO :

   | Pouvoir | Prédicat | Emplacement |
   |---|---|---|
   | PCardsShuffling | `ICardsShufflingGuess.IsCorrect` (roleID) | adapter `PCardsShuffling.cs:36-38`, consommé `CardsShufflingDecision.cs:19,24` |
   | PVisionOfTheImpossible | `VisionGuess.Matches` | adapter `PVisionOfTheImpossible.cs:53`, consommé `VisionOfTheImpossibleDecision.cs:23,30` |
   | PEmbraceOfShadows | `ctx.Roster.SameRole(...)` | `EmbraceOfShadowsDecision.cs:15` (succès :20 / échec :26) |
   | PDroolyHealing | `_healSuccess` (rôle + `isCorrupted‖isHealed`) | **inline, pas de POCO** — `PDroolyHealing.cs:54-59` |
   | PBlessing | `SameRole` | `BlessingDecision.cs:18` |
   | PChainedByTheShadows | `SameRole` | `ChainedByShadowsDecision.cs:20` |
   | PHighPriorityBounty | `IsRobot(Target)` | `HighPriorityBountyDecision.cs:18` |
   | POmniscience | `isChosen` | `OmniscienceDecision.cs:19,22,27` |
   | PCursedVision | `targetIsChosen` | `CursedVisionDecision.cs:20-24` |
   | PTruthChains | `FactionOf == anomaly` | `TruthChainsDecision.cs:18` |
   | PLackOfAffection | `FactionOf == chosen` | `LackOfAffectionDecision.cs:19` |
   | PCorruptingMark | pas de branche échec dans la décision ; rejet de cible côté adapter | `CorruptingMarkDecision.cs:16` (succès inconditionnel) ; `PCorruptingMark.cs:59-63` |

6. **13 pouvoirs n'ont aucune notion de correction** : PAutoCorruption, PCorruptionInsight, PCorruptionKnowledge,
   PCorruptionParanoia, PEyeOfTheVoid, PPersonalBeacons, PInfiniteMessage, PLegacy, PMarqueHurluberluges, PReincarnation,
   PClandestineObservation, PBoundByInk, PNothing. Effets inconditionnels.
7. **Un contrat succès/échec existe déjà mais est quasi-mort.** `IFailablePower`
   (`Assets/Scripts/Characters/Powers/Interfaces/IFailablePower.cs:5-8`, `onPowerSuccessful` / `onPowerFailed`) n'a
   **qu'un seul implémenteur** : `PEmbraceOfShadows.cs:22,26-27,81,87`. `PCorruptingMark` a sa propre paire
   (`ICorrupterPower`, `PCorruptingMark.cs:28-29`). Unique consommateur : `PowerComponents/PCChainer.cs:36-37`, avec un
   fallback `onPowerUsedServer` quand le pouvoir n'est pas `IFailablePower` (`PCChainer.cs:32`) — usage purement
   gameplay serveur, **aucun feedback**.
8. **Aujourd'hui le verdict ne survit que sous 4 formes non exploitables par une UI générique** : (a) listes d'effets
   divergentes, (b) une chaîne de chat en français, (c) les 2 RPC `ICorruptionEvents`, (d) un one-shot FMOD local
   (`PEmbraceOfShadows.cs:24-25,69,73` ; `PDroolyHealing.cs:26-27,66-68`).
9. **`PDroolyHealing` (l'Orpheline, le pouvoir cité nommément dans le playtest) est le seul pouvoir à correction
   entièrement inline**, hors du pipeline POCO (`PDroolyHealing.cs:24` — pas de `IPowerDecision`).

## Deduced Conclusions

- **D1 — La demande `onCorrectUse` / `onIncorrectUse` par pouvoir est au mauvais niveau d'abstraction.** Le verdict est
  produit dans les POCOs de décision (Confirmed #5), qui retournent tous déjà un `PowerOutcome` unique remontant au
  `Power` adapter (`Power.cs:186-206`). Ajouter 2 events × 22 adapters duplique un canal existant et laisse chaque
  pouvoir libre de l'oublier. Un champ sur `PowerOutcome` est produit une fois et consommé une fois.
- **D2 — Un booléen binaire ne suffit pas.** Trois issues sont distinctes dans le code actuel : le rejet côté serveur
  (`PowerOutcome.RejectReason`, `PCorruptingMark.cs:59-63`), le verdict de prédiction (Confirmed #5), et l'absence de
  verdict (Confirmed #6, 13 pouvoirs). Un `bool` force les 13 pouvoirs sans verdict à mentir dans un sens ou l'autre.
- **D3 — Le transport client existe déjà et est le bon.** `OnUsedClientRpc` (`Power.cs:268-272`) est déjà owner-only,
  déjà appelé sur chaque usage, déjà passé par `GetSafeRpcTarget` (bot-safe). Lui ajouter un paramètre verdict est le
  changement de transport minimal. Le `NetworkAction onPowerUsed` (payload-free, all-peers) est le mauvais canal : le
  feedback demandé est privé au lanceur.
- **D4 — `PDroolyHealing` demandera un traitement à part.** Sans POCO, son verdict ne peut pas transiter par
  `PowerOutcome` sans d'abord l'extraire — ce qui est un refactor, pas un ajout de feedback. C'est aussi le pouvoir
  explicitement cité par les playtesteurs.

## Hypothesized Paths

- **H#1 (utilisateur)** — « il faut `onCorrectUse` / `onIncorrectUse` sur chaque pouvoir ». **Status : Refuted (en tant que
  design).** Le besoin est réel ; l'emplacement ne l'est pas. **Resolution** : voir D1/D2. Réfutation cherchée d'abord —
  un event par pouvoir serait justifié si le verdict était calculé dans chaque adapter ; Confirmed #5 montre que 11 des
  12 le calculent en amont, dans les POCOs, sur un chemin déjà convergent.
- **H#2** — « `IFailablePower` est le contrat prévu pour ça, il suffit de l'étendre à tous les pouvoirs ».
  **Status : Open.** Confirme si : le consommateur `PCChainer.cs:36-37` peut absorber 25 implémenteurs sans changer la
  logique de chaînage. Réfute si : câbler `IFailablePower` partout modifie le comportement du chaîner (le fallback
  `PCChainer.cs:32` traite « pas `IFailablePower` » comme « succès » — donner un `onPowerFailed` à un pouvoir sans
  verdict casserait des chaînes). **Risque gameplay réel** — à vérifier avant toute implémentation.
- **H#3** — « le verdict doit être visible par tous les joueurs, pas juste le lanceur ». **Status : Open, design-owned.**
  Le playtest ne le dit pas. Diffuser un « raté » publiquement est une décision de game design (information de
  déduction), pas une décision technique. À trancher par Poyo.

## Source Code Trace

- **Point de production du verdict** — `Assets/Scripts/Domain/Powers/Decisions/*.cs` (12 fichiers, Confirmed #5)
- **Point de convergence** — `Assets/Scripts/Domain/Powers/PowerOutcome.cs:11-17`
- **Point de dispatch** — `Assets/Scripts/Characters/Powers/Power.cs:186-206` (`RunDecisionEffects` /
  `RunClientDecisionEffects`)
- **Point de transport client** — `Assets/Scripts/Characters/Powers/Power.cs:268-272,296`
- **Point de consommation UI** — `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs:61-62,239-255`
- **Exception hors pipeline** — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:54-70`
- **Risque de régression** — `Assets/Scripts/Characters/Powers/PowerComponents/PCChainer.cs:32,36-37`

## Investigation Backlog

| # | Item | Priorité | Statut |
|---|---|---|---|
| 1 | Vérifier H#2 : impact de `IFailablePower` généralisé sur `PCChainer` | Haute | Open |
| 2 | Trancher H#3 : verdict privé (lanceur) ou public | Haute — design-owned | Open |
| 3 | Décider si `PDroolyHealing` est extrait en POCO ou traité en cas spécial | Moyenne | Open |
| 4 | Classer les 13 pouvoirs sans verdict : `None` ou verdict implicite | Moyenne | Open |
| 5 | Confirmer avec les playtesteurs quels pouvoirs précis ont posé problème | Faible | Blocked on evidence |

## Final Conclusion

**Confiance : High** pour le diagnostic (chemins de code confirmés, cités). **Low** pour la forme finale du feedback —
c'est du design non tranché, explicitement différé.

Le besoin est réel et le code est à ~80 % prêt : la correction est déjà calculée pour tous les pouvoirs qui en ont une,
au bon endroit (les POCOs), sur un chemin déjà convergent (`PowerOutcome`), avec un transport owner-only déjà en place
(`OnUsedClientRpc`). Ce qui manque est exactement un maillon : **un champ verdict sur `PowerOutcome`, propagé au
lanceur via le RPC existant, exposé comme un seul event C# côté client.**

## Fix direction (proposée, non implémentée)

Trois changements, pas 22 :

1. `Domain/Powers/PowerOutcome.cs` — ajouter `PowerVerdict Verdict` (enum `None` / `Correct` / `Incorrect` /
   `Rejected`). Défaut `None` → les 13 pouvoirs sans verdict et les tests existants restent inchangés.
2. Les 11 POCOs à verdict — passer `Verdict` dans le `Accept(...)` des deux branches. Modification d'une ligne chacun.
3. `Power.cs` — capturer `outcome.Verdict` dans `RunDecisionEffects`, le passer en paramètre à `OnUsedClientRpc`,
   exposer `event Action<PowerVerdict> onPowerVerdict` côté client. `PDroolyHealing` alimente le même event à la main
   depuis son `OnHealSuccessfulRpc` existant, en attendant son extraction POCO.

Le feedback visuel (logo 🚫, animation) se branche alors sur ce seul event, sans retoucher aucun pouvoir.

## Verification Plan (original)

Domaine pur, testable sans réseau : un test EditMode par POCO à verdict asserte `Verdict` sur les deux branches
(12 × 2 cas). Le transport se vérifie dans le harnais 2-NM existant (`MultiClientGameFixture`) : un pouvoir spawné,
verdict observé sur la réplique cliente du lanceur uniquement. Aucun playtest requis pour valider la détection —
seulement pour valider le feedback visuel, quand il existera.

---

## Follow-up: 2026-07-20 — implementation

Decisions ratified by Poyo before coding: (1) zero regression on `PCChainer` — `IFailablePower` untouched;
(2) verdict private to the caster; (3) `PDroolyHealing` (Glooby) extracted to a POCO; (4) Glooby's verdict is
graded on the ROLE GUESS ALONE — guessing right on an uncorrupted target is still a success, because the
deduction is what the player controlled.

### Correction to the proposed fix direction (Confirmed, found during implementation)

**The `OnUsedClientRpc` piggyback proposed above is not viable.** `OnUsed()` is called BEFORE the decision
runs in nearly every power — `PBlessing.cs:39` then `:45`, `PChainedByTheShadows.cs:39` then `:44`,
`PTruthChains.cs:41` then `:47`, `PHighPriorityBounty.cs:41` then `:52`, `POmniscience.cs:100` then `:110`,
`PCorruptingMark.cs:67` then `:73`. At `OnUsed` time the verdict does not exist yet. Only `PCursedVision`
(`:43` then `:45`) and `PCardsShuffling` (`:118` then `:121`) run the decision first. Ordering is
inconsistent across the codebase, so no single hook on the use path can carry the grade.

**Resolution:** the verdict travels on its OWN channel, emitted from the decision seam
(`RunDecisionEffects` / `RunClientDecisionEffects`) rather than from the use seam. Same one-hop,
owner-only, `GetSafeRpcTarget` guarantees.

### Correction to the power count: 11 → 9

Two powers listed as verdict-bearing in Confirmed #5 were reclassified to `None` on the "did the caster make
a guess that can be wrong?" test:

- **PCursedVision** — an information query. `CursedVisionDecision.cs:22` computes "est / n'est pas un élu";
  both answers are a usable result and the caster predicted nothing. Grading it would show a red verdict to a
  player who did nothing wrong.
- **PLackOfAffection** — the outcome depends on WHO was contacted, not on a prediction. It also resolves on
  the contacted target's client (`PLackOfAffection.cs:63`), so grading it would need a bounce hop for no gain.

**PCorruptingMark** stays `None` as already noted (no failure branch in the decision).

Final verdict-bearing set (9): Blessing, ChainedByShadows, EmbraceOfShadows, DroolyHealing, TruthChains,
Omniscience, HighPriorityBounty, CardsShuffling, VisionOfTheImpossible.

### Changes made

| File | Change |
|---|---|
| `Assets/Scripts/Domain/Powers/PowerVerdict.cs` | NEW — enum `None` / `Correct` / `Incorrect` |
| `Assets/Scripts/Domain/Powers/PowerOutcome.cs` | `Verdict` property + graded `Accept` overload; default `None` |
| `Assets/Scripts/Domain/Powers/PowerId.cs` | `DroolyHealing = 24` |
| `Assets/Scripts/Domain/Powers/IRosterView.cs` | `IsCorrupted(int slot)` |
| `Assets/Scripts/Characters/Powers/Runtime/CharacterManagerRoster.cs` | `IsCorrupted` impl |
| 8 decision POCOs | verdict on both branches (one line each) |
| `Assets/Scripts/Domain/Powers/Decisions/DroolyHealingDecision.cs` | NEW — Glooby extracted from the adapter |
| `Assets/Scripts/Characters/Powers/PDroolyHealing.cs` | rewired to the POCO; dropped dead `_power` local (`:52`, assigned never read) and the now-redundant `OnHealSuccessfulRpc` |
| `Assets/Scripts/Characters/Powers/Power.cs` | `onPowerVerdict` event + `EmitVerdictServer` / `EmitVerdictFromClient` + 2 RPCs; `RunDecisionEffects` now returns the verdict |
| 2 EditMode test fakes | `IsCorrupted` impl |
| `Assets/Scripts/Tests/Editor/PowerVerdictTests.cs` | NEW — 21 tests, both branches of all 9 + the None family |

### Deliberate non-change (flagged, needs a design call)

`PDroolyHealing.cs` — the FMOD one-shot still keys on the EFFECTIVE heal (role match AND target corrupted),
not on the role guess. Under Poyo's ratified rule a right guess on a healthy target is `Correct`, so the
power now plays the FAILURE sound while `onPowerVerdict` reports `Correct`. v1 parity was preserved rather
than silently changing audio. One-line fix available (`_healSuccess` → `_roleGuessed`) once decided.

### Verification performed

- EditMode **496/496 passed**, 0 failed (`run_tests`, 17.1s)
- PlayMode **244/244 passed**, 0 failed (`run_tests`, 20.5s)
- `read_console` filtered on `error CS` → 0 entries; only pre-existing serialization warnings remain

No playtest run — per standing policy, playtesting is Poyo's.

### Open

- No client consumer is wired yet, by design (Poyo: detection first, real feedback later). Consequence: the
  channel cannot be observed in a playtest until something subscribes. A tagged `[VERDICT]` debug log on
  `onPowerVerdict` is the cheapest way to make it verifiable in-game — proposed, not added.
- The Glooby audio contradiction above.
- `PDroolyHealing.OnGameStartedServer` / `OnNightEndedServer` still adapter-side (per-night healed roster).
  Correct for now — it is replicated-state bookkeeping, not an effect intention.

**Status: Concluded** for the detection layer; the feedback layer stays design-owned and unstarted.

---

## Follow-up: 2026-07-20 #2 — placeholder feedback layer

Poyo: "met une vignette (contour d'écran) vert/rouge […] environ 1 seconde en animation (genre dotween) […]
c'est du placeholder donc le but c'est que l'archi soit propre pour qu'on ai juste a placer le bon feedback
travaillé".

### Architecture — one swap point

```
Power.onPowerVerdict            (gameplay, caster-only, already built)
   → PowerVerdictFeedbackRouter (PERMANENT — owns "which power / is it mine / when")
      → IPowerVerdictFeedback   (THE SEAM)
         → VerdictVignetteFeedback  (PLACEHOLDER — delete on swap)
```

Replacing the placeholder = write a MonoBehaviour implementing `IPowerVerdictFeedback`, put it in the
router's `feedbackTargets`, delete the vignette component. `Power.cs`, the 9 decision POCOs and the router
are untouched by that swap. The router's list is an array, so the real feedback can also run *alongside* the
placeholder during a transition.

### Evidence that shaped the design (Confirmed)

- **No existing overlay/HUD manager to reuse.** No `ScreenFade` / `ScreenFlash` / `Vignette` / `Overlay`
  script exists. The nearest structural precedent is `FrostCanvas` + `CardPickerManager.frostCanvasGroup`
  (`Assets/Scripts/UI/BoardUI/CardPickerManager.cs:453-473`). A new canvas was therefore required.
- **`FocusCanvas` holds sorting order 400**, the highest in `GameScene` (`FocusManager.cs:185-186`). The new
  `PowerVerdictCanvas` sits at **500** so the flash is never occluded.
- **DOTween house style** = `DOKill(true)` immediately before re-driving, `.onComplete +=` for chaining,
  `Ease.OutQuint`, no `Sequence()` / `SetLink()` anywhere in the repo. Closest existing shape to a 1s
  in-then-out flash: `Assets/Scripts/UI/StateUI/AwakeningStateUI.cs:65-73`. Matched exactly.
- **Ownership must be tested at VERDICT time, not at spawn.** A power's replicated `ownerClientId` is not
  guaranteed to have arrived when `onPowerSpawned` fires, so filtering there would be a spawn-order race
  (cf. the known NGO spawn-order hazard). The router subscribes to every power and tests ownership when the
  grade arrives, which is always well after the spawn handshake.
- **Strict local test, not `IsLocalOrSimulated`.** On the host, a simulated bot's verdict is delivered
  locally through `GetSafeRpcTarget` interception. `IsLocalOrSimulated` would flash the host's screen for a
  bot's power — visual noise AND an information leak about the bot's result. The router uses
  `GetLocalClientId() == power.ownerClientId.Value`, the same distinction `PLackOfAffection.cs:57-59` draws.

### Files added

| File | Role |
|---|---|
| `Assets/Scripts/UI/PowerFeedback/IPowerVerdictFeedback.cs` | the seam — `Play(PowerVerdict)` |
| `Assets/Scripts/UI/PowerFeedback/PowerVerdictFeedbackRouter.cs` | permanent bridge, subscribe in `Start` / unsubscribe in `OnDestroy` (mirrors `PowerManager`) |
| `Assets/Scripts/UI/PowerFeedback/VerdictVignetteFeedback.cs` | **PLACEHOLDER** screen-border flash |

### Scene wiring (GameScene, backed up first)

`---GameVisuals--- / GameCanvases / PowerVerdictCanvas` — Canvas (ScreenSpaceOverlay, order 500) +
CanvasScaler (ScaleWithScreenSize, 1920×1080) + `PowerVerdictFeedbackRouter`.
└ `VerdictVignette` — Image (`raycastTarget` false) + CanvasGroup (alpha 0, non-interactable,
  `blocksRaycasts` false) + `VerdictVignetteFeedback`, RectTransform stretched to full screen.

Backup: `BackupToolkit/GameScene.unity.before-verdict-vignette` (repo root, gitignored).

**Wiring gotcha hit and confirmed again:** setting the object-reference array in one call reported success
but wrote `[null]`. Re-verified by reading the component back, then fixed with the per-element form
`feedbackTargets.Array.data[0]`. `feedbackTargets.Array.size` is rejected outright
("Unsupported SerializedPropertyType: ArraySize"). Final read-back confirms the reference is live.

### Deliberate deviation from house style (placeholder only)

The vignette sprite is generated at runtime (64² alpha-falloff `Texture2D` + `Sprite.Create`) so the
placeholder ships no art asset that would need cleaning up later. Runtime texture generation is near
unprecedented here — one prior occurrence, `LobbyRolesUitkController.cs:220` — and the house pattern for
permanent full-screen effects is material/shader based (`UIGlitchGroup.cs:79-84`). Acceptable for throwaway
code; the real feedback should NOT copy it.

### Timing

Fade in 0.18s → hold 0.16s → fade out 0.66s ≈ 1.0s total, all `Ease.OutQuint`, peak alpha 0.85. Every value
is a serialized field so it can be tuned in the inspector without a recompile. Colours likewise — flagged
in the header as provisional, not a design decision.

### Verification

- EditMode **496/496**, PlayMode **244/244**, both after the scene save
- `read_console`: 0 `error CS`; remaining entries are the pre-existing `*StubState` PlayMode noise
- Scene wiring verified by read-back, not by the tool's success report

Not verified: the on-screen result. Rendering, colour and timing need Poyo's eyes in a playtest.

---

## Follow-up: 2026-07-20 #3 — vignette toned down + centre icon

Poyo: vignette too aggressive and the object was not findable in the hierarchy; add a centre-screen icon
with a scale pop-in and fade in/out. Then a correction: keep BOTH verdicts connected — a ✅ icon on success,
the 🚫 on failure. Plus the constraint: **TMP's default font has no emoji glyphs.**

### Icons are drawn, not typed

The TMP constraint rules out any text-based icon: "✅" / "🚫" through the default font asset renders as tofu.
`VerdictIconFeedback` therefore rasterises both glyphs procedurally into a runtime sprite — a two-segment
tick and a ring-plus-diagonal-bar — as a smoothed distance field so the edges stay antialiased at any size.
White line-art; the `Image` tint supplies green/red.

Escape hatch for authored art without touching code: `correctSpriteOverride` / `incorrectSpriteOverride`.
When set, the procedural build is skipped entirely.

### Vignette toned down (scene values, not just code defaults)

Changing a field initialiser does NOT touch an already-serialised scene instance, so these were rewritten on
the component itself and verified by read-back:

| Field | Before | After |
|---|---|---|
| `peakAlpha` | 0.85 | **0.22** |
| `borderThickness` | 0.32 | **0.15** |
| `fadeInDuration` | 0.18 | 0.22 |
| `holdDuration` | 0.16 | 0.12 |
| `fadeOutDuration` | 0.66 | 0.62 |

Both components also gained `playOnCorrect` / `playOnIncorrect` inspector switches, both true, so either half
can be muted without a recompile.

### Where the objects live

```
---GameVisuals---
  GameCanvases
    PowerVerdictCanvas      ← PowerVerdictFeedbackRouter (the feedbackTargets list)
      VerdictVignette       ← VerdictVignetteFeedback   (screen-border glow)
      VerdictIcon           ← VerdictIconFeedback       (centre icon, 260×260)
```

Icon animation: scale 0.55 → 1.0 over 0.16s on `Ease.OutBack` (the overshoot is what makes it read as a pop)
with a simultaneous fade to alpha 0.9, hold 0.36s, then fade out over 0.45s while drifting to scale 1.12.
`Ease.OutBack` is the one deliberate departure from the project's default `Ease.OutQuint` — OutQuint grows,
it does not pop.

### Verification

- EditMode **496/496**, PlayMode **244/244**, both after the scene save
- 0 `error CS`
- Router array re-verified by read-back: `[VerdictVignette, VerdictIcon]`, both non-null (the array-writes-null
  gotcha struck again on the first attempt and was corrected per-element)

Still not verified: how any of it actually looks. That needs Poyo's playtest.
