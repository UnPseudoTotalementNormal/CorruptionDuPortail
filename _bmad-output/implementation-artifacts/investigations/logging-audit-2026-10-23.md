# Audit de couverture des logs de playtest — 2026-10-23

**Statut : trous Haute + Moyenne comblés le jour même (branche feat/full-logging, 97 fichiers).** Vérifié : compile
avec et sans `CDP_PLAYTEST_LOGS`, EditMode 850/850, build de playtest + partie réseau Relay 4 processus (0 erreur,
0 désync), 3361 lignes en ligne, 21 catégories. Non fait : Lié par l'encre (le refus serveur arrive après le `OnUsed`
client ; le corriger demande un RPC de refus comme Luma, décision séparée).

Base : origin/Dev 874f3ec3. Méthode : 4 relectures du code par domaine (règles serveur, réseau, UI/entrées,
avatars/rendu/audio) + lecture d'une vraie partie réseau autoplay en ligne (`20261020-1934-083f`, 1089 lignes).
Vérifié à la main : Facepunch LogLevel, audio FMOD muet, Lié par l'encre, Portail ; écarté : bug des notes (la
suppression passe l'instance stockée).

Légende couverture : **GameLog** = événement structuré ; **[TAG]** = `Debug.Log` taggé capturé (catégorie `unity`,
non cherchable par clé) ; **texte** = `Debug.Log` sans tag ; **rien** = aucune trace.

## Bilan par domaine

| Domaine | État |
|---|---|
| Phases, jour, votes, enchaînement, pouvoirs (start/used/verdict/rejected/effect), chat, F8 | bien couvert |
| Règles serveur hors pouvoirs (pouvoirs donnés/retirés, savoir donné, nuit, Portail, raison de victoire) | trous |
| Réseau (connexion, Relay, Steam, rejoin, siège réservé, voix) | surtout texte/[TAG], décisions clés muettes |
| UI joueur hors jour (boutons, lobby, picker, menus, tablette, messages, notes) | presque rien |
| Avatars, caméra, emotes, cosmétiques, bandeau de nuit, audio FMOD | rien |

## Constats runtime (partie autoplay réseau)

- `minLevel` distant = `I` : les `GameLog.Verbose` (`screen.show/hide`) ne remontent jamais par défaut.
- 122 lignes `unity` sans tag sur 1089 (dont 48 × `card visible from: N`, bruit) ; login, lobby, Relay en texte libre.
- Facepunch : les logs de connexion/déconnexion Steam sont `LogLevel.Developer` (FacepunchTransport.cs:94/110/144)
  alors que BootScene.unity:629 est en `LogLevel: 1` (Normal) → muets en build ; `EndReason` jamais loggé.

## Points d'entrée uniques (un ajout couvre beaucoup)

| Endroit | Événement | Couvre |
|---|---|---|
| UI/CustomButton.cs:58-60 | `ui.button.click` / `ui.button.refused` (nom) | pause, quitter, vote plateau, Skip, barre perso, message anonyme… |
| Characters/CharacterManager.cs:523 / 553 | `power.granted` / `power.removed` (source) | distribution, Ugës, Luma, Héritage, Réincarnation |
| GameLogic/GameInfoRevealer.cs:145-205 | `knowledge.reveal` (observateur, cible, champ, niveau) | Marque, Luma, intro faux rôles, Portail, chaînes, fin |
| Avatars/CameraModeChannel.cs:39 | `camera.mode` | vue assise / plateau / emote / overlay |
| AudioSystem/GameAudioManager.cs:104 (+ PlayMusic/PlayEventInstance) | `audio.event.missing` | tout son manquant (aujourd'hui muet ou exception brute) |
| Avatars/AvatarManager.cs:308 / 366 / 287 | `avatar.spawn` / `avatar.despawn` / `avatar.rejoin` | « chat absent / déplacé » |
| Characters/Powers/Power.cs:437 + GameLogic/PowerUsageManager.cs:72-75 | `power.target` (serveur) / `power.click.refused` (client, raison CanUse) | « j'ai cliqué mon pouvoir, rien » |

## Trous Haute priorité restants

Réseau : `JoinHandshake.WaitForConnectedOrTimeout` (Network/JoinHandshake.cs:54-122, ni début/fin/raison) ;
`RelayConnector.ConnectClientAsync` échec final muet (Network/RelayConnector.cs:123, 140) ; refus de rejoin confondus
(ConnectionApprovalGate.cs:93, GameManager.cs:804/826, MainMenu.cs:399-441) ; expiration du siège réservé 120 s
(GameManager.cs:899-923, `seat.expired`) ; chargement de GameScene non vérifié (MainMenu.cs:714) ; init UGS lancée sans
attendre (UnityServicesInit.cs:12) ; voix Steam indisponible muette (VoiceChatManager.cs:87, 121).

Règles : tirage des faux rôles montrés aux anomalies (GameIntroductionState.cs:50-66) ; raison de victoire absente de
`game.end` (VictoryConditionCheckState.cs:71) ; Portail : carte choisie / devinette / résultat (TakeDownThePortalState.cs:99-149) ;
nuit : fin du minuteur (AwakeningState.cs:313-318), sortie muette couche ≠ couche courante (:205-208) ; Ugës vol et Luma
copie réussie (PMarqueHurluberluges.cs:119-175, PCardsShuffling.cs:188-219) ; expiration du piratage Omniscience
(POmniscience.cs:60-90).

UI : case Prêt + démarrage auto bloqué sans raison (LobbyRolesUitkController.cs:547, LobbyState.cs:91-94) ; boutons ‹ ›
du lobby no-op pour un non-hôte (GameSettingsManager.cs:209) ; picker ouvert / annulé faute de cible
(CardPickerManager.cs:125-256) ; clic sur la barre de pouvoirs (PowerBarObject3D.cs:208-229) ; envoi du message anonyme
(SendMessagePanel.cs:40-64, MessageManager.cs:99) ; menu pause / Échap ignoré (PauseMenuInput.cs:37) ; refus muets du menu
principal (MainMenu.cs:594-601).

Avatars : cosmétiques reçus/appliqués/retour défaut (PlayerAvatar.cs:250-270, CatAppearance.cs:55-62) ; roue d'emote et
lecture (EmoteWheelInput.cs:111-185, EmotePlaybackController.cs:40-72) ; tablette (TabletToggleInput.cs:38-108) ;
avatar local jamais lié (AvatarCameraArbiter.cs:449) ; bandeau de nuit (UI/NightTurnBanner.cs:123-179, NRE brute :104) ;
chaînes 3D non construites (CatChains.cs:209-212).

## Bugs possibles vus en passant (hors logs)

- PBoundByInk.cs:80-83 : refus d'une cible déjà visée APRÈS `ServerAuthorizeEffect`, sans `ServerCancelAuthorizedEffect`
  → le `OnUsed` qui suit consomme une utilisation sans effet (course rare, le client filtre déjà).
- TakeDownThePortalState.cs:99/121 : le serveur ne vérifie pas que le clic vient du Mage (robustesse, pas de triche en playtest).
- Lobby : boutons ‹ › actifs mais sans effet pour un non-hôte (UX).

## Moyenne / Basse

Détail complet dans les rapports des 4 relectures (session Claude du 2026-10-23) ; principaux : `player.left` sans
« réservé », `client.stopped` sans DisconnectReason, jeton de rejoin émis/reçu, LobbyManager en texte libre, liveness
sans GameLog, micro/canal voix, couches de nuit, retrait de pouvoir, héritage, reparent, vote refusé (raison), Skip,
notes, grille de déduction, grimoire, journal, réglages (disposition), visibilité des chats, cercle du jour éteint,
bougie allumée/soufflée, shader de contour absent, banks FMOD.
