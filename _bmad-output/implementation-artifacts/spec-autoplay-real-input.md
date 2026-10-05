---
title: 'Autoplay — mode « vraies entrées » : les sièges à écran jouent par l''interface'
type: 'feature'
created: '2026-10-05'
status: 'draft'
context:
  - '{project-root}/tools/autoplay/REFERENCE.md'
  - '{project-root}/Packages/com.unpseudo.autoplay/EXTENDING.md'
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Les bots autoplay agissent par appels directs (`Power.StartUse`, RPC de vote, `SleepCharacterServerRpc`, gestionnaires de cartes appelés à la main, `TrySendChatMessage`) et sautent menus, lobby et pause. Un bouton masqué, une couche qui intercepte les clics, un collider désactivé ou un écran jamais affiché passent donc inaperçus.

**Approach:** Un levier opt-in `-autoplay-real-input` : chaque siège qui a un écran (siège hôte et bots possédés tour à tour sur l'hôte, chaque vrai client en `play-net`) agit en déplaçant une souris/un clavier **virtuels Input System** jusqu'à l'élément réel puis en cliquant ; la vraie chaîne EventSystem/raycasters/UITK décide. Sans le levier, l'ancien chemin direct reste le défaut (plus rapide).

## Boundaries & Constraints

**Always:**
- Sans `-autoplay-real-input`, comportement strictement identique à aujourd'hui (règle 4 d'`EXTENDING.md`).
- Tout code sous `#if UNITY_EDITOR || DEVELOPMENT_BUILD` ; le package reste agnostique du jeu (aucun type du jeu).
- Un clic qui ne produit pas l'effet attendu = événement `input.miss` (cible, position, objet réellement touché par `EventSystem.RaycastAll`), puis repli sur le chemin direct pour que la partie continue. Jamais de repli silencieux.
- Sur l'hôte, posséder (`SetPossessedIdentity`) le siège qui agit avant **chaque** action UI (vote, sommeil, chat compris) et attendre une frame (reconstruction de la barre de pouvoirs).
- En mode vraies entrées, le pilote n'appelle plus `UsingPowerUpdate` (déjà fait par `PowerUsageManager.Update`).
- Validation sur `play-net` dès qu'un client est concerné ; seuils mesurés, jamais inventés.

**Ask First:**
- Toute modification de code de production au-delà d'une seam lecture seule dev-only (ex. inverse de `ScreenToPanel`).
- Si le spike (tâche 1) montre que les entrées virtuelles ne passent pas sans focus, ou que la vraie souris de l'utilisateur interfère malgré la désactivation : HALT, la conception change.
- Si `Cursor.lockState = Locked` dans un joueur sans focus capture le curseur de l'utilisateur : HALT avant tout travail sur le réticule.

**Never:**
- UGS (Authentication, Lobby, Relay) et Steam : reportés avec le point 1. Le bouton Login, Host/Join du menu principal ne sont pas cliqués.
- Ne pas migrer `ChatPanel.cs:56` (Entrée), `TooltipWindow.cs:35` (`Input.mousePosition`), `DevIdentityController` : lacunes connues, documentées.
- Ne pas modifier l'asset de réglages Input System du projet (réglages runtime clonés, autoplay uniquement).
- Pas de focus, pas de fenêtre minimisée ; `AutoplayWindowGuard` garde la priorité.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Clic réussi | élément visible, cliquable | effet de jeu observé (`power.start`, `vote`, `picker.click`…) + `input.click target=… hit=…` | N/A |
| Élément occulté | autre élément au-dessus | `input.miss target=X hit=Y` puis repli direct | scénario `max: 0` sur `input.miss` |
| Élément absent / collider coupé | `CanUse` faux, UI non affichée | `input.miss reason=not-found\|disabled` | repli direct |
| Utilisateur bouge sa souris | joueur sans focus | aucun effet en jeu | vrais périphériques désactivés |
| Levier absent | run habituel | aucun périphérique virtuel, aucun `input.*` | N/A |

</frozen-after-approval>

## Code Map

- `Packages/com.unpseudo.autoplay/Runtime/` -- runner (IEnumerator), journal, window guard (déverrouille le curseur chaque `LateUpdate`), asmdef sans référence.
- `Assets/Scripts/Autoplay/AutoplayDriver.cs` -- actions directes : pouvoir `:462-493`/`:437`, vote `:505-557`, sommeil `:468`, portail `:561-601`, sélecteur visuel `:618-646`, chat `:707-748`, possession `:668-681`.
- `Assets/Scripts/Autoplay/CdpAutoplayGame.cs` -- options/levier, lobby : `ApplyPreset` `:223`, `ForceRoles` `:227-235`, `ForceStart` `:268`.
- `Board/UI/PowerBar/PowerBarObject3D.cs:203` (couche 7, `PowersBar.GetPowerBarObject` `:224`, collider actif ssi `CanUse`) ; `Board/Card.cs:367-389` + `CardPickerManager.PickableCards` (tween 0,5 s) ; `Board/UI/VoteCanvas/VoteCanvas.cs:18,122,145` (survol puis bouton) ; `VoteStateUI.cs:32` (`SkipVoteButton`, overlay) ; `Board/UI/SkipButton.cs:31` (sommeil) ; `TakeDownThePortalState.cs:76,153`.
- `Reticle/ReticleInteractor.cs:185,224,265` + `Avatars/AvatarCameraArbiter.cs:273-286,369-376` -- réticule et verrouillage du curseur.
- `Smartphone/SmartphoneController.cs:85-88,156`, `ChatSystem/ChatPanel.cs`, `NoteSystem/NoteRibbon.cs`, `NoteChoosePanel.cs`, `Avatars/EmoteWheelInput.cs:40,120-129`, `TooltipSystem/HoverTooltipComponent.cs`, `UI/Misc/LeaveGameButton.cs`, `UI/Settings/AudioPanelSettings.cs`.
- `UI/LobbyRoles/LobbyRolesUitkController.cs:267,500-527,568` + `LobbyRolesRtPresenter.cs:69,100` ; `UI/InfoTable/InfoTableRtPresenter.cs:96-113` -- tablette UITK en RenderTexture.
- `UI/MainMenu.cs:76-94`, `UI/LoginMenu.cs`, `Network/ClientDisconnectHandler.cs:345-350` (tri 32000 depuis 7704b27).

## Tasks & Acceptance

**Execution:**
- [ ] **1. Spike (bloquant)** `Packages/com.unpseudo.autoplay/Runtime/AutoplayVirtualInput.cs` -- créer souris + clavier virtuels, `InputSettings` runtime cloné avec `backgroundBehavior = IgnoreFocus`, désactiver les vrais périphériques souris/clavier (et ceux ajoutés ensuite via `onDeviceChange`) ; API `MoveTo(screen, seconds)`, `Click()`, `PressKey(Key)`. Mesurer sur build sans focus : clic sur `SkipButton`, mouvement de la vraie souris pendant le run, effet de `Cursor.lockState`. Consigner les résultats ici (Design Notes) -- tout le reste en dépend.
- [ ] `Unpseudo.Autoplay.asmdef` -- référencer `Unity.InputSystem`.
- [ ] `Assets/Scripts/Autoplay/AutoplayUiLocator.cs` -- position écran de chaque cible (collider → `Camera.main.WorldToScreenPoint`, canvas monde → `RectTransformUtility`, overlay → caméra nulle, UITK en RT → inverse de `ScreenToPanel` via seam lecture seule) + condition de cliquabilité.
- [ ] `AutoplayDriver.cs` -- branche vraies entrées pour pouvoir, carte du sélecteur, vote (survol 0,5 s puis bouton), passer le vote, sommeil, portail ; vérification d'effet + `input.click`/`input.miss` ; possession avant chaque action.
- [ ] `AutoplayDriver.cs` -- interactions en partie : tablette (Tab/flèches), chat (onglet, clic du champ ; envoi par le chemin direct, Entrée étant une lacune connue), notes, roue d'émotes (T + position), infobulles (survol, vérifier l'affichage), pause (`PauseButton`, curseurs audio, `LeaveGameButton` comme variante de `quit-at`), réticule si le spike l'autorise.
- [ ] `CdpAutoplayGame.cs` -- levier `lobby-ui` (impliqué par `real-input`) : l'hôte clique « ★ Preset classique » et les « + » forcés au lieu d'`ApplyPreset`/`ForceRoles`, chaque siège clique « Prêt », démarrage par `TryAutoStart` (pas `ForceStart`).
- [ ] `CdpAutoplayGame.cs` -- menu principal hors UGS : ouvrir/fermer les panneaux Host/Join, curseurs audio ; capture de l'écran de connexion et du refus de connexion avec vérification que le panneau de notification passe au-dessus.
- [ ] `tools/autoplay/scenarios/real-input-*.json` -- un scénario par groupe (actions principales en `play-net`, interactions en partie, lobby, menu) avec `input.miss max: 0` et le contrôle positif de chaque effet.
- [ ] `tools/autoplay/REFERENCE.md`, `.claude/skills/autoplay/SKILL.md` -- levier, événements, scénarios, tableau « Not covered yet » (Entrée du chat, liens d'infobulle, F1–F4, UGS/Steam, réticule si exclu) ; retirer la limite périmée « sorting order 1000/999 ».

**Acceptance Criteria:**
- Given un run sans `-autoplay-real-input`, when il se termine, then aucun événement `input.*` et les scénarios existants passent inchangés.
- Given `play-net 3` avec `-autoplay-real-input`, when la partie se termine, then chaque pouvoir, vote, sommeil et choix de carte des sièges à écran a un `input.click` suivi de son effet, et `input.miss` = 0.
- Given un bouton volontairement masqué (test de casse), when le scénario tourne, then il échoue avec `input.miss hit=<élément masquant>`.
- Given `lobby-ui`, when tous les sièges ont cliqué « Prêt », then la partie démarre par `TryAutoStart` avec la composition choisie à la souris.
- Given l'utilisateur qui bouge sa souris pendant un run, when le run se termine, then aucun clic parasite n'apparaît dans le journal.

## Design Notes

- Plus lent par construction (déplacement, survols, tweens, possession séquentielle) : réservé aux tests poussés.
- Bots simulés (id ≥ 100) : couverts sur l'hôte par possession ; sans écran propre, la preuve la plus forte reste `play-net`.
- `AutoplayWindowGuard` déverrouille le curseur chaque frame : le chemin réticule (curseur verrouillé) n'est jamais exercé aujourd'hui. Une pression de clic virtuelle peut aussi déclencher le réticule (`ReticleInteractor.cs:265`) en plus du module UI → à trancher dans le spike (désactiver le réticule en vue curseur, ou viser par rotation de la caméra).
- Package en IEnumerator (convention existante du runner, pas de dépendance UniTask) ; adaptateur de jeu en UniTask avec `GetCancellationTokenOnDestroy`.

## Verification

**Commands:**
- `tools/autoplay/unityctl.sh compile` -- expected: 0 erreur
- `tools/autoplay/unityctl.sh editmode` -- expected: vert (dont gardes DI/scène)
- `python -X utf8 Packages/com.unpseudo.autoplay/Tools~/run_scenario.py tools/autoplay/scenarios/real-input-<groupe>.json` -- expected: PASS, puis FAIL une fois en masquant un bouton
- `tools/autoplay/campaign.sh` -- expected: scénarios existants inchangés

## Spec Change Log
