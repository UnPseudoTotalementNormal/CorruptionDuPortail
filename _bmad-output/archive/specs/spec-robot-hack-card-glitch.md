---
status: in-progress
slug: robot-hack-card-glitch
---

# Spec — Glitch visuel sur carte piratée (pouvoir omniscient du Robot)

## Intent

Quand le Robot pirate une carte (POmniscience / "hack"), afficher l'effet glitch vidéo
(`Custom/UI_GlitchVideo`) sur cette carte — **visible uniquement par le joueur Robot**.
Le glitch suit l'état "piraté" (posé au hack, retiré si dé-piraté). L'expiration par tour
n'est PAS traitée ici (logique du pouvoir revue plus tard — le code ne dé-pirate jamais
aujourd'hui, donc de facto le glitch reste toute la partie).

Décisions Poyo :
- Gating = **knowledge system** (nouveau flag robot-only).
- Durée = tant que "piraté".
- Portée = carte entière (Images ; **TMP text = limite technique, hors scope, voir Notes**).

## Approche

Réutilise l'infra reveal existante (`GameInfoRevealer` / `CharacterInfoReveal`), déjà
robot-only via RPC ciblé — exactement comme le reveal de rôle que le hack fait déjà.

1. **Knowledge** : nouveau champ `CharacterInfoReveal.isHacked` (RevealLevel), + valeur
   d'enum `RevealField.Hacked` (Domain), mappée dans les 2 field-name switches.
2. **Décision** : `OmniscienceDecision` émet en plus
   `RevealInfo(target, Hacked, Personal, viewer=owner, broadcast=true)`. Le RPC ciblé
   (`SendRevealLevelRpc`) ne touche que le client Robot (redirection bot déjà gérée).
3. **Visuel carte** : nouveau `CardHackGlitch` (MonoBehaviour sur la carte) lit
   `GameInfoRevealer.GetCharacterInfo(id).isHacked >= Personal` (observer = client local)
   → `UIGlitchGroup.Apply()` / `Remove()`. Recalcule sur `onCharacterInfoRevealedChanged`
   + `onCardSetInfo` + OnEnable (respawn/flip-safe). `UIGlitchGroup` posé sur `FrontCanvas`,
   matériau = `Custom_UI_GlitchVideo.mat`.

Sur les autres clients `isHacked` reste False → pas de glitch. Robot-only garanti par le
RPC ciblé + observer local.

## Tâches (ordonnées par dépendance)

1. `Assets/Scripts/Domain/EffectDescriptor.cs` — `RevealField { … , Hacked }`.
2. `Assets/Scripts/GameLogic/GameInfoRevealer.cs` — `CharacterInfoReveal.isHacked = RevealLevel.False`.
3. `Assets/Scripts/Characters/Powers/Runtime/Executors/RevealInfoExecutor.cs` — case `Hacked → nameof(isHacked)`.
4. `Assets/Scripts/Characters/Powers/Runtime/EffectExecutorHelpers.cs` — même case (cohérence).
5. `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs` — ajouter le `RevealInfo(Hacked…)`.
6. `Assets/Scripts/Tests/Editor/PowerDecisionTests.cs` — MAJ liste attendue Omniscience.
7. `Assets/Scripts/Board/CardEffects/CardHackGlitch.cs` — driver (nouveau).
8. Prefab `Assets/Prefabs/Card.prefab` — `UIGlitchGroup` sur FrontCanvas (mat assigné),
   `CardHackGlitch` sur Card root (ref glitchGroup).

## AC

- Given un Robot humain hacke la carte de X, When le hack se résout, Then SEUL le client
  Robot voit le glitch sur la carte de X ; les autres clients ne voient rien.
- Given un client non-Robot, When il regarde la carte de X, Then `isHacked` = False et aucun glitch.
- Given la carte de X est re-spawn / flip, When le Robot la revoit, Then le glitch persiste.
- Given EditMode, When `OmniscienceDecision.Decide`, Then la liste d'effets contient
  `RevealInfo(target, Hacked, Personal, owner, true)` dans l'ordre attendu.

## Notes / limites

- **TMP text** : `UIGlitchGroup` ignore les TMP (shader SDF non remplaçable par un shader
  sprite). Le glitch "carte entière" couvre donc les Images (portrait, holders, faction,
  foreground), pas les textes. Faire glitcher le texte = shader TMP dédié OU rendu carte→RT
  (pattern InfoTable) — follow-up séparé.
- Expiration 1 tour du hack = follow-up (touche win condition).
