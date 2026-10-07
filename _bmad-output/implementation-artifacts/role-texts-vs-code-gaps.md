# Role texts vs code: open gaps

**Snapshot, not a source of truth.** Written 2026-10-07 (board task T7, PR #115), when the game designer's wording was
put on the role card. The code is the truth: it can change without this file being updated. Whoever closes a gap
(code changed, or wording changed) should update or strike its row here, and re-check the evidence before relying on a
row.

Where the texts live: power names / descriptions in `Assets/Prefabs/Powers/*.prefab` (`powerName`,
`powerDescription`); the role card's "Passif" lines per role in `Assets/ScriptableObjects/RoleCardTexts.asset`.
Designer's source: Discord thread T7 « Revoir les descriptions des personnages pour la bulle info » (2026-07-18).

Policy used: the designer's text is shown verbatim (typos fixed); **no behaviour was changed**. Each row below is a
place where the text promises something the code does not do (or the reverse). Decide with the designer: change the
code, or change the text.

| # | Role / power | Designer's text says | Code does (2026-10-07) | Evidence |
|---|---|---|---|---|
| 1 | Mage Occulte, Corruption Ciblée | « S'il n'a pas utilisé Étreintes des Ombres ce tour-ci, il apprend également le rôle du joueur, **puis finit son tour** » | Corrupts, and reveals the role if Étreintes was untouched this night (then Étreintes drops to 0 uses). Nothing ends the turn: if Étreintes succeeded once first, its 2nd use is still available after Corruption Ciblée. | `PCConcentrated.cs:38-46`, `PCorruptingMark.cs:131-135`, `CorruptingMarkDecision.cs:150-154` |
| 2 | Mage Occulte + Abyss, passives | « Les Anomalies se connaissent et communiquent entre elles » | The anomaly chat comes only from Abyss's `EyeOfTheVoid` (opened for every anomaly): with no Abyss in the game, the Mage has no anomaly chat. No mutual identity reveal between anomalies found. | `EyeOfTheVoidDecision.cs:23-28`, `MageOcculte.asset` (no EyeOfTheVoid) |
| 3 | Mage Occulte + Abyss, passives | « Les Anomalies connaissent autant de rôles factices que leur nombre dans la partie » | Not implemented: fake roles are only used by Luma, Ugës and the win / stat filters. | (absence) |
| 4 | Mage Occulte + Abyss, passives | (not mentioned) | Both also have `CorruptionInsight`: they see everyone's corruption state from the start. Hidden from the card now that the designer's passive lines replace the power list. | `CorruptionInsightDecision.cs:17-20`, `MageOcculte.asset`, `Abyss.asset` |
| 5 | Luma, Mélange des cartes | Role absent: « une copie **de ses pouvoirs** à utilisation unique » | Copies **one** random active power of that role (single use). Role present and wrong guess: she also learns the roles that role targeted (not in the new text). | `PCardsShuffling.cs:186-229`, `CardsShufflingDecision.cs:29-40` |
| 6 | Orpheline, passive (« Paranoïa [WIP] ») | « Elle apprend quels **rôles** l'ont ciblée cette nuit » | Puts a private icon on the thumbnail of each **player** who targets her, as soon as it happens: she learns players, not roles. The prefab name still says « [WIP] » (not shown on the card). | `PTargetedByReport.cs` (`OnTargetingAddedServer`), `TargetedByReportDecision.cs:26-30` |
| 7 | Orpheline, Manque d'affection | Sends « sa propre identité (ainsi que son état de corruption) » ; a non-élu « ne recevra que la visite et non l'identité » | An élu target learns her role; her corruption state is never sent. Every target, élu or not, gets a chat line naming her role (« X est venu(e) vous voir »), so a non-élu learns more than the visit. | `LackOfAffectionDecision.cs:54-61` |
| 8 | Dr. Gloubi + Dryade, passive | « Il / Elle connaît l'état de corruption **et de soin** des différents élus » | `CorruptionKnowledge` shows the corruption icon of everyone (anomalies included). No UI shows the heal state (`isHealed` is never read for display). | `CorruptionKnowledgeDecision.cs:17-20` |
| 9 | Dr. Gloubi, Soin Baveux | « Il ne peut pas cibler des élus […] qui ont été **enchaînés** » | Target flags 120 (élus, Corrupted, Blessed, Chained) exclude healed élus but **allow chained** ones. Public announcement « X a été soigné(e) » is sent even if the target was healthy (matches « innocenté publiquement » loosely). | `DroolyHealing.prefab` `targetIncludeFlags: 120`, `PDroolyHealing.cs:96-114` |
| 10 | Dryade, Bénédiction | « Elle ne peut pas cibler des élus ayant déjà été **soignés** » | Target flags 136 (élus + Healed) exclude chained élus but **allow healed** ones. Immunity to Corruption Ciblée is a client-side picker filter only; the server does not re-check the target. The designer also titled her power « Soin Baveux » (kept « Bénédiction », likely a copy-paste). | `Blessing.prefab` `targetIncludeFlags: 136`, `TargetUtils.cs:79-81`, `Power.cs:388-419` |
| 11 | Chasseuse de Prime | Lists only Observation Clandestine (« La chasseuse de Prime sera à revoir ») | Still has `HighPriorityBounty` « Prime Prioritaire » (active, refills each night, targets marginals: Robot hit = public elimination, miss = she is chained). Still shown on her card. | `Traqueuse.asset`, `HighPriorityBountyDecision.cs:43-58` |

Checked and matching (no gap): Étreintes des ombres (2 tries per night max), Abyss (one extra use per night when the
only live anomaly), Robot (always counts as hacked, Piratage), Technomancien (self beacon, sees the Robot's
corruption), Repenti, Observation Clandestine, Ugës (Marque d'Hurluberluges).
