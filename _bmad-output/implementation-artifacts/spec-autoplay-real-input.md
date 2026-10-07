---
title: 'Autoplay — mode « vraies entrées » : les sièges à écran jouent par l''interface'
type: 'feature'
created: '2026-10-05'
status: 'done'
baseline_commit: '0071c6f8acb1c2365106e000d5adb14fb3caa84f'
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
- [x] **1. Spike (bloquant)** `Packages/com.unpseudo.autoplay/Runtime/AutoplayVirtualInput.cs` -- créer souris + clavier virtuels, `InputSettings` runtime cloné avec `backgroundBehavior = IgnoreFocus`, désactiver les vrais périphériques souris/clavier (et ceux ajoutés ensuite via `onDeviceChange`) ; API `MoveTo(screen, seconds)`, `Click()`, `PressKey(Key)`. Mesurer sur build sans focus : clic sur `SkipButton`, mouvement de la vraie souris pendant le run, effet de `Cursor.lockState`. Consigner les résultats ici (Design Notes) -- tout le reste en dépend.
- [x] `Unpseudo.Autoplay.asmdef` -- référencer `Unity.InputSystem`.
- [x] `Assets/Scripts/Autoplay/AutoplayUiLocator.cs` -- position écran de chaque cible (collider → `Camera.main.WorldToScreenPoint`, canvas monde → `RectTransformUtility`, overlay → caméra nulle, UITK en RT → inverse de `ScreenToPanel` via seam lecture seule) + condition de cliquabilité.
- [x] `AutoplayDriver.cs` -- branche vraies entrées pour pouvoir, carte du sélecteur, vote (survol 0,5 s puis bouton), passer le vote, sommeil, portail ; vérification d'effet + `input.click`/`input.miss` ; possession avant chaque action.
- [x] `AutoplayDriver.cs` -- interactions en partie : tablette (Tab/flèches), chat (onglet, clic du champ ; envoi par le chemin direct, Entrée étant une lacune connue), notes, roue d'émotes (T + position), infobulles (survol, vérifier l'affichage), pause (`PauseButton`, curseurs audio, `LeaveGameButton` comme variante de `quit-at`), réticule si le spike l'autorise.
- [x] `CdpAutoplayGame.cs` -- levier `lobby-ui` (impliqué par `real-input`) : l'hôte clique « ★ Preset classique » et les « + » forcés au lieu d'`ApplyPreset`/`ForceRoles`, chaque siège clique « Prêt », démarrage par `TryAutoStart` (pas `ForceStart`).
- [x] `CdpAutoplayGame.cs` -- menu principal hors UGS : ouvrir/fermer les panneaux Host/Join, curseurs audio ; capture de l'écran de connexion et du refus de connexion avec vérification que le panneau de notification passe au-dessus.
- [x] `tools/autoplay/scenarios/real-input-*.json` -- un scénario par groupe (actions principales en `play-net`, interactions en partie, lobby, menu) avec `input.miss max: 0` et le contrôle positif de chaque effet.
- [x] `tools/autoplay/REFERENCE.md`, `.claude/skills/autoplay/SKILL.md` -- levier, événements, scénarios, tableau « Not covered yet » (Entrée du chat, liens d'infobulle, F1–F4, UGS/Steam, réticule si exclu) ; retirer la limite périmée « sorting order 1000/999 ».

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

### Spike results (2026-10-05, dev build, `play-build`, player launched without focus)

Runs: `AutoplayRuns/20261005-19*-spike-real-input*` (seeds 777-779, `-autoplay-power-use-probability 0` so every bot sleeps through the button).

- **Unfocused virtual input works.** 11/11 then 18/18 clicks on `BoardWorldCanvas/SkipButton` produced the sleep (`sleep … via=click`, 0 `input.miss`) with `Application.isFocused=False` at click time. The UI module reads the virtual mouse (`point=…:AutoplayMouse`, `IsPointerOverGameObject=True`). Requires the runtime settings clone with `IgnoreFocus` (the module skips pointer processing when unfocused otherwise, `InputSystemUIInputModule.Process`).
- **Trap 1 (fixed):** `InputSystem.onDeviceChange` fires *inside* `AddDevice`, before the returned device is stored: the hook disabled the virtual mouse itself (`mouseEnabled=False`, `module=none`, no effect). Guarded with an `addingVirtual` flag.
- **Trap 2 (fixed):** a `RectTransform` centre can lie on no raycastable graphic (SkipButton: `contains=False` on its `Background`, button half off-screen). The locator aims at the raycastable graphics under the target (centre, then a 3x3 grid, clamped on screen) and keeps the first point where `EventSystem.RaycastAll` reaches the target.
- **User's mouse:** the player exposes one real `Mouse` (no keyboard device seen); it is disabled at install, later devices are disabled by the hook. Events of a disabled device are dropped before `InputSystem.onEvent` and the UI module, so they cannot act. Passive control run (`-autoplay-real-input-control`, real devices kept): `realEvents=0` (inconclusive: no user movement guaranteed). The user's real mouse is never driven by a script (feedback 2026-10-05).
- **Cursor lock:** `Cursor.lockState = Locked` in an unfocused player never clipped the OS cursor (`GetClipCursor` = full virtual screen before / same call / next frame). No HALT.
- **Reticle:** armed (`ReticleInteractor._active`) in `VoteState` and `VoteRecapState` (seated first-person by day), off elsewhere; cursor stays `None` (window guard). A virtual left click there would also fire the reticle's confirm on whatever is at screen centre: decision (Poyo, 2026-10-05): **aim with the reticle**, the human path. In real-input mode the window guard unlocks the cursor only while the player has focus (an unfocused lock never captures the OS cursor, measured above); the bot turns the seated camera with virtual mouse deltas until the target is under the reticle, then clicks.
- Levers added for the spike: `real-input`, `real-input-control` (diagnostic), `power-use-probability <0..1>` (forces sleeps through the button). Spike-only diagnostics (`input.predict-miss`, `input.chain`, `input.reticle`) were removed once the causes were found.

## Verification

**Commands:**
- `tools/autoplay/unityctl.sh compile` -- expected: 0 erreur
- `tools/autoplay/unityctl.sh editmode` -- expected: vert (dont gardes DI/scène)
- `python -X utf8 Packages/com.unpseudo.autoplay/Tools~/run_scenario.py tools/autoplay/scenarios/real-input-<groupe>.json` -- expected: PASS, puis FAIL une fois en masquant un bouton
- `tools/autoplay/campaign.sh` -- expected: scénarios existants inchangés

## Spec Change Log

### Implementation notes (2026-10-05)

- UI Toolkit in a RenderTexture (lobby tablet) needed no production seam: `AutoplayUiLocator.TryLocateRtElement` inverts the presenter's own screen-to-panel function numerically through `RuntimePanelUtils.ScreenToPanel` (an analytic inverse got the axes wrong), validated by `panel.Pick`.
- Reticle aiming is steered in angles (camera rotation per delta unit, measured), not pixels: a hovered card moves by itself and fooled a pixel ratio. Screen-overlay targets are reported `reason=screen-fixed`.
- A target covered by a settling animation is waited for 1.5 s before `input.miss`; a click without effect is retried once (`input.retry`).
- Desync tool (`state.hash`): character flags are left out of the awakening sample and the roster out of a lobby played through the tablet (both differ by sampling time only; the in-game tripwire still checks them once settled).
- Main menu: the login screen covers the menu while autoplay does not log in (UGS out of scope): panel steps are warned in `real-input-menu`; the rejected-join notification is proven on top and dismissable.
- Findings for the designer (not fixed, design-owned): role-picker fan off screen at 16:9 with 10 roles; the vote's Skip button (screen overlay) cannot be reached by the seated reticle; `NoteRibbon` placed in no scene; Escape not wired to pause. Pre-existing errors `CardEffectTechnoBeacon/BoolEnabler: effectData` also occur without real input.

## Review Outcome (2026-10-05)

Three reviewers (blind, edge cases, acceptance). Patched: in-flight clicks cancelled by `End()` and held keys released; a real input lock (`AcquireInput`); exceptions journaled (`input.error`) then the direct path; one click only (no retry), `input.skip` when the effect already holds, re-aim before a reticle click (`input.reaim`); stale turn / closed vote not driven; portal candidates no longer burned; cursor-lock probe removed; audio slider host-only and always restored, panels always closed; `quit-at` through the pause menu only with `real-input`; mask needs `real-input` and is destroyed; desync hash unchanged without a lever; docs corrected. Decision (Poyo): the two design findings are exempted in the scenarios by a targeted, warned rule (`picker … off-screen`, `vote-skip … screen-fixed`); every other miss fails.

Validation: EditMode 564/564; `real-input-actions`, `real-input-tour`, `real-input-lobby`, `real-input-menu`, `real-input-mask` PASS; existing `net-sync-3clients`, `client-leaves-at-vote`, `chat-private`, `client-owner-local-powers` PASS on the final build. Separate finding: an intermittent late-joiner Characters desync (7 clients, lobby) seen once in the campaign, not reproduced; tracked as its own fix (deterministic repro first).

## Suggested Review Order

**Virtual devices (package)**

- Entry point: virtual mouse + keyboard, settings clone with IgnoreFocus, real devices disabled.
  [`AutoplayVirtualInput.cs:48`](../../Packages/com.unpseudo.autoplay/Runtime/AutoplayVirtualInput.cs#L48)
- Install undoes itself on failure and on quit: the user's devices always come back.
  [`AutoplayVirtualInput.cs:71`](../../Packages/com.unpseudo.autoplay/Runtime/AutoplayVirtualInput.cs#L71)
- Release held keys / button at the end of a run or a cancelled action.
  [`AutoplayVirtualInput.cs:282`](../../Packages/com.unpseudo.autoplay/Runtime/AutoplayVirtualInput.cs#L282)
- Window guard lets the game lock the cursor only while unfocused, with real input.
  [`AutoplayWindowGuard.cs:71`](../../Packages/com.unpseudo.autoplay/Runtime/AutoplayWindowGuard.cs#L71)

**Where to click (locator)**

- Aim where the real raycast reaches the target, not at a RectTransform centre.
  [`AutoplayUiLocator.cs:272`](../../Assets/Scripts/Autoplay/AutoplayUiLocator.cs#L272)
- Candidate points on drawn graphics / collider bounds, clamped on screen.
  [`AutoplayUiLocator.cs:207`](../../Assets/Scripts/Autoplay/AutoplayUiLocator.cs#L207)
- Reached = hit inside the target, or its click handler is the target.
  [`AutoplayUiLocator.cs:303`](../../Assets/Scripts/Autoplay/AutoplayUiLocator.cs#L303)
- UI Toolkit (overlay or RenderTexture): invert the panel's own mapping, validate by Pick.
  [`AutoplayUiLocator.cs:325`](../../Assets/Scripts/Autoplay/AutoplayUiLocator.cs#L325)
- Finite-difference Newton solve, no assumption on scale or axes.
  [`AutoplayUiLocator.cs:405`](../../Assets/Scripts/Autoplay/AutoplayUiLocator.cs#L405)

**Clicking with proof (driver)**

- One click, effect required, skip if already done, re-aim before a reticle click.
  [`AutoplayDriverRealInput.cs:363`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L363)
- Reticle aim steered in angles (hovered cards move by themselves).
  [`AutoplayDriverRealInput.cs:525`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L525)
- UI Toolkit click building block, re-querying rebuilt trees.
  [`AutoplayDriverRealInput.cs:427`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L427)
- Shared pointer lock and end-of-session cancellation.
  [`AutoplayDriverRealInput.cs:44`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L44)
- Power by its 3D bar object; direct fallback only on a live turn.
  [`AutoplayDriverRealInput.cs:113`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L113)
- Vote: hover the card, click its button; late / closed vote handled.
  [`AutoplayDriverRealInput.cs:213`](../../Assets/Scripts/Autoplay/AutoplayDriverRealInput.cs#L213)
- Hooks in the bot brain: real-input branches for sleep and powers.
  [`AutoplayDriver.cs:552`](../../Assets/Scripts/Autoplay/AutoplayDriver.cs#L552)
- Desync hash: flags left out of the awakening sample only with an input lever.
  [`AutoplayDriver.cs:391`](../../Assets/Scripts/Autoplay/AutoplayDriver.cs#L391)

**Lobby, tour, menu**

- Lobby levers: lobby-ui implied by real-input; ready + TryAutoStart instead of ForceStart.
  [`CdpAutoplayGame.cs:302`](../../Assets/Scripts/Autoplay/CdpAutoplayGame.cs#L302)
- Preset by mouse on the RenderTexture tablet.
  [`AutoplayDriverLobby.cs:143`](../../Assets/Scripts/Autoplay/AutoplayDriverLobby.cs#L143)
- Wheel scrolling until the element is in view (stuck detection).
  [`AutoplayDriverLobby.cs:85`](../../Assets/Scripts/Autoplay/AutoplayDriverLobby.cs#L85)
- Screen-space UI Toolkit demo: role card overlay opened and closed.
  [`AutoplayDriverLobby.cs:254`](../../Assets/Scripts/Autoplay/AutoplayDriverLobby.cs#L254)
- In-game tour at night while idle; emote at the vote recap.
  [`AutoplayDriverTour.cs:34`](../../Assets/Scripts/Autoplay/AutoplayDriverTour.cs#L34)
- Leave through the pause menu (quit-at with real input).
  [`AutoplayDriverTour.cs:440`](../../Assets/Scripts/Autoplay/AutoplayDriverTour.cs#L440)
- Rejected join: notification on top of the login screen, dismissed by click.
  [`AutoplayMenuTour.cs:115`](../../Assets/Scripts/Autoplay/AutoplayMenuTour.cs#L115)

**Peripherals**

- Install only behind a lever; mask lifecycle.
  [`CdpAutoplayGame.cs:436`](../../Assets/Scripts/Autoplay/CdpAutoplayGame.cs#L436)
- Breakage-test overlay.
  [`AutoplayInputMask.cs:21`](../../Assets/Scripts/Autoplay/AutoplayInputMask.cs#L21)
- Scenarios: [`real-input-actions.json`](../../tools/autoplay/scenarios/real-input-actions.json), [`real-input-tour.json`](../../tools/autoplay/scenarios/real-input-tour.json), [`real-input-lobby.json`](../../tools/autoplay/scenarios/real-input-lobby.json), [`real-input-menu.json`](../../tools/autoplay/scenarios/real-input-menu.json), [`real-input-mask.json`](../../tools/autoplay/scenarios/real-input-mask.json)
- Docs: [`REFERENCE.md`](../../tools/autoplay/REFERENCE.md), [`EXTENDING.md`](../../Packages/com.unpseudo.autoplay/EXTENDING.md), [`SKILL.md`](../../.claude/skills/autoplay/SKILL.md)
