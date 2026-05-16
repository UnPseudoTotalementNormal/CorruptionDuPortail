# Plan Global des Tests Unitaires

Ce document recense l'ensemble des fichiers, classes et fonctionnalités du projet `CorruptionDuPortail` qui nécessitent une couverture par des tests unitaires.
Les dossiers de bibliothèques externes (`Plugins`), les exemples (`Samples`) et les tutoriels (`TutorialInfo`) ont été exclus de cette liste pour se concentrer sur le code métier.

## 1. Extensions, Parseurs et Utilitaires Purs (Priorité Haute)
*Candidats parfaits pour débuter. Les méthodes y sont souvent statiques et pures, ne nécessitant pas de mocks complexes.*

- `Assets/Scripts/Extensions/ArrayExtensions.cs`
- `Assets/Scripts/Extensions/CanvasGroupExtensions.cs`
- `Assets/Scripts/Extensions/FmodEventReferenceExtensions.cs`
- `Assets/Scripts/Extensions/FMODGUIDExtensions.cs`
- `Assets/Scripts/Extensions/GameObjectExtension.cs`
- `Assets/Scripts/Extensions/ListExtensions.cs`
- `Assets/Scripts/Extensions/NetworkObjectExtensions.cs`
- `Assets/Scripts/Extensions/RectTransformExtensions.cs`
- `Assets/Scripts/Extensions/TransformExtensions.cs`
- `Assets/Scripts/Extensions/UlongExtensions.cs`
- `Assets/Scripts/ReflectionHelper.cs`
- `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs`
- `Assets/Scripts/TooltipSystem/TooltipLinkParser.cs` (Parsing de chaînes de caractères)
- `Assets/Scripts/Inputs/ActionStack.cs` (Logique de pile LIFO isolée)

## 2. Logique de Jeu Centrale (Priorité Haute)
*Le cœur du déroulement d'une partie. Les tests ici garantissent que la machine à état et les validations ne comportent pas de failles.*

- `Assets/Scripts/GameLogic/GameManager.cs`
- `Assets/Scripts/GameLogic/ChainingManager.cs`
- `Assets/Scripts/GameLogic/PowerManager.cs`
- `Assets/Scripts/GameLogic/PowerUsageManager.cs`
- `Assets/Scripts/GameLogic/Validation/Validator.cs`
- `Assets/Scripts/GameLogic/GameState.cs` (Classe de base)
- **Etats de jeu** (`Assets/Scripts/GameLogic/GameStates/`) : `AwakeningState.cs`, `ChainingState.cs`, `RoleAttributionState.cs`, `VoteState.cs`, `GameEndingState.cs`, `GameIntroductionState.cs`, `LobbyState.cs`, `TakeDownThePortalState.cs`, `VictoryConditionCheckState.cs`, `VoteRecapState.cs`, `AwakeningRecapState.cs`.

## 3. Personnages, Rôles et Pouvoirs (Priorité Haute)
*Les règles spécifiques à chaque joueur. Assurer que chaque pouvoir et condition de victoire fonctionne de manière isolée est crucial.*

- `Assets/Scripts/Characters/CharacterManager.cs`
- `Assets/Scripts/Characters/Character.cs`
- `Assets/Scripts/Characters/Role.cs`
- `Assets/Scripts/Characters/RoleDataObject.cs` (Validation des données)
- `Assets/Scripts/Characters/Powers/PowerDataObject.cs` (Validation des données)
- **Pouvoirs** (`Assets/Scripts/Characters/Powers/`) : Tester indépendamment la logique de chaque pouvoir (`PAutoCorruption.cs`, `PBlessing.cs`, `POmniscience.cs`, `PTruthChains.cs`, etc. - *liste exhaustive à traiter*)
- **Composants de Pouvoirs** (`Assets/Scripts/Characters/Powers/PowerComponents/`) : `PCChainer.cs`, `PCConcentrated.cs`, `PCPowerUnlockWhenChain.cs`, `PCReparentOnChain.cs`.
- **Conditions de Victoire** (`Assets/Scripts/Characters/WinningConditions/`) : `WAnomalyCorruption.cs`, `WChosenChainedAllAnomaly.cs`, `WMarginalIsChainedWin.cs`, `WOmniscienceHackedCharacter.cs`.

## 4. Plateau, Cartes et Flux de Sélection (Priorité Moyenne)
*Logique d'interaction, flux de sélection UI et effets sur le plateau de jeu.*

- `Assets/Scripts/Board/BoardManager.cs`
- `Assets/Scripts/Board/Card.cs`
- `Assets/Scripts/Board/CardEffects/CardEffectManager.cs`
- **Composants d'effets** (`Assets/Scripts/Board/CardEffects/Components/`) : `CardEffectBoolEnabler.cs`, `CardEffectTechnoBeacon.cs`.
- `Assets/Scripts/UI/BoardUI/Selection/SelectionFlowService.cs` (Logique de flux de sélection, sans UI)
- `Assets/Scripts/UI/BoardUI/Selection/SelectionFlowOptions.cs`

## 5. Systèmes Transverses et Logique UI (Priorité Moyenne)
*Les différents gestionnaires (Managers) et la logique décorrélée de l'affichage.*

- `Assets/Scripts/MessageSystem/MessageManager.cs`
- `Assets/Scripts/NoteSystem/NoteManager.cs`
- `Assets/Scripts/ChatSystem/ChatManager.cs`
- `Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs`
- `Assets/Scripts/Inputs/InputManager.cs`
- `Assets/Scripts/FocusSystem/FocusManager.cs`
- `Assets/Scripts/ArrowSystem/ArrowManager.cs`
- `Assets/Scripts/TooltipSystem/TooltipManager.cs`
- `Assets/Scripts/Smartphone/SmartphoneController.cs` (Logique de gestion des apps)
- `Assets/Scripts/UI/InfoTable/InfoRoleChecker.cs` (Logique de vérification des rôles)
- `Assets/Scripts/UI/InfoTable/InfoTablePlayerRoleHandler.cs` (Traitement des données de rôle)

## 6. Réseau et Données (Priorité Moyenne à Basse)
*Tests nécessitant probablement des Mocks (ex: Netcode for GameObjects).*

- `Assets/Scripts/Network/Services/LobbyManager.cs`
- `Assets/Scripts/Network/NetworkDictionary.cs`
- `Assets/Scripts/Network/NetworkSerializableObject.cs`
- `Assets/Scripts/Network/Player/LocalPlayerInfo.cs`
- `Assets/Scripts/Network/GameCode.cs`
- `Assets/Scripts/GameValues.cs`
