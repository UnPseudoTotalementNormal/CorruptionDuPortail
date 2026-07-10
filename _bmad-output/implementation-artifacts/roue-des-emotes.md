# Roue des emotes — artefact d'implémentation

Branche worktree `worktree-feat+roue-des-emotes` (base `origin/Dev`). Deux parties :

1. **Pose défaut avatar = A-pose** (au lieu du coucou en boucle actuel).
2. **Roue d'emotes UITK** ouverte en maintenant `T` dans la vue embodied (vote), pointage à la
   direction/hover, relâche = joue l'emote survolée. Une seule emote pour l'instant : coucou.

Le code (scripts + UITK + tests) a été écrit dans le worktree SANS Unity (l'Editor pointait sur le
checkout principal). **Rien n'a été compilé ni testé.** Ce doc liste tout ce qui reste à faire côté
Editor + la vérif. À exécuter quand Unity ouvre le projet depuis ce worktree.

---

## Fichiers créés / modifiés (code — fait)

| Fichier | Rôle |
|---|---|
| `Assets/Scripts/Avatars/EmoteWheelSelection.cs` | Math pure : direction → index de secteur (CW depuis le haut, deadzone). |
| `Assets/Scripts/Tests/Editor/EmoteWheelSelectionTests.cs` | Golden EditMode de la math (18 asserts). |
| `Assets/Scripts/Avatars/EmoteDefinition.cs` | Data d'une emote : nom, icône, `animatorEmoteId`. |
| `Assets/Scripts/Avatars/EmoteSet.cs` | ScriptableObject : liste ordonnée d'emotes (ordre = layout CW). |
| `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs` | Controller UITK (calqué `RoleCardController`). |
| `Assets/UI/Screens/EmoteWheel/EmoteWheel.uxml` | Structure de la roue. |
| `Assets/UI/Screens/EmoteWheel/EmoteWheel.uss` | Layout radial (tokens gold/scrim existants, provisoire). |
| `Assets/Scripts/Avatars/EmoteWheelInput.cs` | Input local : hold `T`, gate embodied, direction/hover, joue au relâche. |
| `Assets/Scripts/Avatars/PlayerAvatar.cs` | **modifié** : `RequestEmote(id)` + `[Rpc(SendTo.Server)] PlayEmoteRpc` + ref `NetworkAnimator`. |

Les `.meta` seront générés par Unity au premier import (worktree jamais ouvert par l'Editor).

---

## À FAIRE côté Unity (handoff)

### A. Phase 1 — A-pose par défaut (Animator/rig)
Le coucou est joué UNIQUEMENT parce que c'est l'état par défaut de l'Animator — aucun script ne le
déclenche.

- Fichier : `Assets/Animators/Cat_Avatar.controller`.
  - État `Wave` = `m_DefaultState` (clip guid `cd9685b2eac22964293fe29a474965f4`).
  - Un état vide `New State` (`m_Motion: 0`) existe déjà mais n'est pas le défaut.
- **Décision asset A-pose** (inspecter le rig `Cat_Avatar` d'abord) :
  - Si la **bind pose du rig est déjà A-pose** → régler l'état défaut sur un état vide (`m_Motion: 0`) :
    le modèle affiche sa bind pose. `AvatarIdleMotion.cs` (respiration/oreilles) se superpose par-dessus.
  - Sinon → créer un **clip A-pose 1-frame** (poser le rig en A-pose, key toutes les rotations de bones,
    1 frame) et le mettre comme motion de l'état défaut.
  - A-pose > T-pose (demandé). Vérifier visuellement en play.

### B. Phase 2 — Animator emotes
- Sur `Cat_Avatar.controller`, ajouter :
  - un paramètre **int `EmoteId`** (les noms DOIVENT matcher `PlayerAvatar.EmoteIdParam` = `"EmoteId"`),
  - un trigger **`Emote`** (`PlayerAvatar.EmoteTriggerParam` = `"Emote"`).
- Câbler : depuis l'état défaut (A-pose/Idle) → transition sur le trigger `Emote` vers l'état `Wave`
  (réutiliser le clip coucou), retour auto vers Idle en fin de clip (has-exit-time).
  - Pour plusieurs emotes plus tard : sous-états conditionnés sur `EmoteId` (1 = coucou, 2 = …). Avec
    une seule emote, un simple trigger→Wave→Idle suffit (EmoteId ignoré tant qu'il n'y a qu'un état).

### C. NetworkAnimator
- Le `NetworkAnimator` vit sur le child modèle `Cat_Avatar` (dans `PlayerAvatar.prefab`).
- **Wirer** le champ `_networkAnimator` de `PlayerAvatar` (composant sur la racine du prefab) vers ce
  `NetworkAnimator`. Sans ça, `RequestEmote` est un no-op silencieux.
- Vérifier l'autorité du NetworkAnimator = **Server** (les triggers sont posés côté serveur dans le RPC).

### D. Data — EmoteSet + icône coucou
- Créer l'asset : `Create → Corruption → Emote Set` (ex. `Assets/Data/Emotes/EmoteSet.asset`).
- Ajouter 1 entrée : `displayName = "Coucou"`, `icon =` (sprite coucou), `animatorEmoteId = 1`.

### E. Scène — GameObject de la roue (UITK)
Calqué sur le GameObject RoleCard dans `GameScene.unity` :
- Nouveau GameObject `EmoteWheel` avec un **`UIDocument`** :
  - `sourceAsset` = `Assets/UI/Screens/EmoteWheel/EmoteWheel.uxml`,
  - `PanelSettings` = **`PS_ScreenOverlay`** (celui qui porte le thème `theme.tss` → les tokens `--cdp-*`),
  - `sortingOrder` élevé (ex. 1000, comme RoleCard).
- Ajouter le composant **`EmoteWheelController`** sur le même GameObject ; wirer `document` (le UIDocument)
  et `emoteSet` (l'asset créé en D).

### F. Scène — Input
- Ajouter le composant **`EmoteWheelInput`** (sur un GameObject de scène — au choix, ex. à côté de
  `AvatarCameraArbiter` ou sur le GameObject EmoteWheel).
- Wirer :
  - `wheel` = le `EmoteWheelController`,
  - `cameraModeChannel` = le même asset `CameraModeChannel` que l'arbiter (broadcast du mode caméra).
- Feel (défauts placeholder, Poyo tune) : `openKey=T`, `stickSensitivity`, `stickMaxRadius`, `deadzone`.

### G. Vérif
1. `mcp__UnityMCP__read_console` → 0 erreur de compil.
2. `mcp__UnityMCP__run_tests` (EditMode, filtre `EmoteWheelSelectionTests`) → vert.
3. Playtest (Poyo) : au spawn l'avatar est en A-pose (plus de coucou en boucle) ; en vote embodied,
   maintenir `T` ouvre la roue, flick vers le coucou + relâche → l'avatar fait coucou, visible par les
   autres joueurs.

---

## Notes de design / décisions
- **Relâche `T`** = joue l'emote survolée (pas de clic). Roue affichée même avec 1 seule emote.
- **Pointage** : curseur locké → direction (delta souris accumulé en stick virtuel) ; curseur unlock
  (tablette ouverte, overview…) → hover classique. Même math, pas d'edge case.
- **Réseau** : emote = cosmétique mais visible par tous → autorité serveur via `NetworkAnimator`
  (owner → `ServerRpc` → serveur pose le trigger → réplique). Pas de `GetSafeRpcTarget` (pas de RPC ciblé
  par clientId ; les bots n'ont pas de corps).
- **Input** : lecture directe `Keyboard.current` / `Mouse.current` (pas d'édition du `.inputactions`).
  Migrable vers une action `EmoteWheel` nommée si souhaité (swap des lectures dans `EmoteWheelInput`).
- Visuels roue = tokens `--cdp-color-role-*` / scrim existants, **provisoires** (palette design-owned).
