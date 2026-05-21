# Plan de Correction — Bugs après ajout d'un nouveau rôle (Uges)

**Pour : autre IA exécutante.**
**Contexte projet :** voir `CLAUDE.md`. Unity 6000.2.6f2, NGO, branche `Dev`.
**Outils à utiliser :** Edit, Read, `mcp__UnityMCP__read_console`, `mcp__UnityMCP__run_tests`.
**À NE PAS faire :** ajouter de nouveaux null-checks défensifs — corriger les causes racines.

---

## 1. Symptômes rapportés par l'utilisateur

1. Erreurs Unity dès la création de la partie (lobby).
2. Erreurs supplémentaires quand on ajoute des rôles dans le panneau d'attribution.
3. L'**Introduction (`GameIntroductionState`) ne se déclenche pas** quand la partie démarre.
4. `NetworkManager.Singleton == null` rencontré dans `PowerManager.Start()` — **interdit de masquer par un null-check**, il faut comprendre pourquoi.

Tous les symptômes sont apparus après l'ajout du rôle **Uges** (RoleID `9999`, asset `Assets/ScriptableObjects/Characters/Uges.asset`).

---

## 2. Investigation déjà effectuée — résumé

### 2.1 Asset Uges incomplet
`Assets/ScriptableObjects/Characters/Uges.asset` comparé à `Croupiere.asset` :

| Champ | Uges | Croupiere (référence) |
|---|---|---|
| `role.onChainingSound` | **absent** | présent (vide mais sérialisé) |
| `role.onGameStartRoleRevealSound` | **absent** | présent |
| `RoleDataObject.powers` (extérieur) | `[]` | 1 PowerDataObject |
| `role.isAwakened: 0` (champ obsolète) | **présent** (résidu) | absent |
| `role.powers: []` (champ obsolète, le runtime est `readonly`) | présent | absent |

➜ L'asset Uges a été créé en partant d'une **version obsolète** du sérialisateur de `Role`. Il manque les champs `EventReference` ajoutés ultérieurement. Au runtime, ces champs seront défaut/vide — pas crash direct mais comportement inutile (aucun son joué).

### 2.2 `Role.NetworkSerialize` — fix précédent incomplet
Le précédent fix (session passée) a filtré les nulls dans `winningConditions` pour le COUNT et l'ITERATION (writer path). **Mais le foreach final n'a pas été corrigé** :

```csharp
// Role.cs L131-134 (état actuel)
foreach (var _winningCondition in winningConditions)
{
    _winningCondition.ownerClientId = ownerClientId; // <-- NRE si null dans la liste
}
```

Conséquence : à chaque sérialisation writer (`AskForRefreshSettingsRpc` envoyée à chaque clic dans le panneau d'attribution, ou `GiveRoleToCharacterRpc` au début de partie), si un `[SerializeReference]` slot est vide, NRE → la RPC ne part jamais → désync.

De plus, **muter de l'état dans `NetworkSerialize` est un anti-pattern**. Cette boucle n'a rien à faire ici — c'est un effet de bord caché. Elle propage `ownerClientId` aux `WinningCondition` enfants, ce qui doit se faire au moment où le `Role` est assigné (dans `RoleAttributionState.GiveRandomRole` ou dans `Role.UpdateRole`/setter `ownerClientId`).

### 2.3 `PowerManager.cs` — null check actuellement masquant le vrai problème
État actuel (band-aid posé en session précédente) :
```csharp
private void Start()
{
    Power.onPowerSpawned += OnPowerSpawned;
    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return; // <-- band-aid à RETIRER
    GameManager.instance.onGameStarted += OnGameStarted;
}
```

**Causes racines possibles du `Singleton == null`** :

- **A.** `PowerManager` est un `MonoBehaviour` dans la scène `GameScene` (cf. `BootScene.unity:552` vs `GameScene.unity:11754`). `Start()` peut s'exécuter **avant** que NGO ait fini d'initialiser le réseau lors d'un changement de scène, ou si la scène est rechargée hors flux normal.
- **B.** Workflow dev : ouvrir `GameScene` directement dans l'éditeur sans passer par `BootScene` → `NetworkManager.Singleton` n'existe simplement pas (et c'est légitime de ne pas crasher dans ce cas, mais pas via un null-check sec — via un design correct du cycle de vie).
- **C.** L'ordre d'exécution Awake/Start dans Unity n'est pas garanti entre objets de scènes différentes lors d'un `LoadScene Single`.

Le **vrai** fix est de ne pas dépendre de `NetworkManager.Singleton.IsServer` dans `Start()`. Il faut s'abonner à un événement qui garantit que le réseau est prêt **ET** que le rôle (serveur/client) est connu.

### 2.4 Introduction qui ne se lance pas
Pipeline d'états (cf. `GameManager.cs` + `RoleAttributionState.cs:70`) :
```
LobbyState → RoleAttributionState (OnStartStateServer → NextGameState immédiat) → GameIntroductionState → ...
```

Si une exception est levée DANS `RoleAttributionState.OnStartStateServer` ou pendant les RPCs qu'elle déclenche (`GiveRoleToCharacterRpc` notamment), `NextGameState()` est appelé en effet domino mais la sérialisation cassée fait que **les clients ne reçoivent jamais leur Role**. Le `gameManager.currentGameStateIndex.Value` côté serveur passe à l'intro, mais l'intro côté client a besoin de `GetLocalCharacter().role.onGameStartRoleRevealSound` (cf. `GameIntroductionState.cs:86`) — si `role` est dans un état incohérent, NRE silencieuse côté client.

➜ La cause racine la plus probable de "l'intro ne se lance pas" est l'**erreur de sérialisation #2.2** qui empêche les rôles d'arriver aux clients.

---

## 3. Plan de correction (à exécuter par l'IA suivante)

### TÂCHE 1 — Réparer l'asset Uges
**Fichier :** `Assets/ScriptableObjects/Characters/Uges.asset`

**Action :** Régénérer l'asset proprement. **Ne PAS éditer le YAML à la main** — utiliser Unity Editor via MCP ou demander à l'utilisateur.

Étapes recommandées :
1. Demander à l'utilisateur d'**ouvrir l'asset Uges dans l'Inspector** et de :
   - Vérifier que `winningConditions` ne contient **aucun slot vide** (un `[SerializeReference]` non assigné = `null` → crash).
   - Vérifier que les `EventReference` `onChainingSound` et `onGameStartRoleRevealSound` sont assignés (peuvent être vides explicitement mais doivent exister comme champs).
   - Assigner les `Power` voulus dans `RoleDataObject.powers` (extérieur) — si Uges est censé n'avoir aucun pouvoir, OK de laisser vide, mais c'est suspect par rapport aux autres rôles.
2. Alternative : dupliquer `Croupiere.asset`, renommer en `Uges.asset`, modifier `roleID: 9999`, `roleName` (UTF-8 "Uges, l'Imposteur"), `factionType`, `roleType`, `rolePortrait`, `roleDifficulty`, et les conditions de victoire.

**Vérification :** ouvrir le fichier `.asset` brut et confirmer qu'il a la même STRUCTURE que `Croupiere.asset` (mêmes champs présents, même si valeurs différentes).

---

### TÂCHE 2 — Corriger `Role.NetworkSerialize`
**Fichier :** `Assets/Scripts/Characters/Role.cs`

**Problème :** la boucle finale L131-134 itère sur des entrées potentiellement `null` et fait une mutation d'état (anti-pattern).

**Action :**

A. **Supprimer** la boucle finale (lignes 131-134) :
```csharp
// SUPPRIMER ces lignes :
foreach (var _winningCondition in winningConditions)
{
    _winningCondition.ownerClientId = ownerClientId;
}
```

B. **Déplacer** la propagation d'`ownerClientId` aux endroits où c'est sémantiquement correct :

1. Dans `Role.UpdateRole` (déjà existant) — ajouter à la fin :
   ```csharp
   foreach (var _winningCondition in winningConditions)
   {
       if (_winningCondition != null) _winningCondition.ownerClientId = ownerClientId;
   }
   ```

2. Dans `RoleAttributionState.GiveRandomRole` (`RoleAttributionState.cs:91-93`) après `_character.role.ownerClientId = _character.ownerClientId.Value;` :
   ```csharp
   foreach (var _winningCondition in _newRole.winningConditions)
   {
       if (_winningCondition != null) _winningCondition.ownerClientId = _newRole.ownerClientId;
   }
   ```

C. **Vérifier** que le writer path filtre toujours les nulls (déjà fait par fix précédent). Garder cette protection — c'est un garde-fou contre des assets mal édités côté Inspector.

D. **Bonus** : ajouter dans `RoleDataObject.cs` une validation `OnValidate()` qui retire/log les `null` de `winningConditions` :
```csharp
private void OnValidate()
{
    if (role?.winningConditions == null) return;
    for (int i = role.winningConditions.Count - 1; i >= 0; i--)
    {
        if (role.winningConditions[i] == null)
        {
            Debug.LogError($"{name} a un slot WinningCondition vide à l'index {i}. Assigner ou retirer.", this);
        }
    }
}
```
Note : ne PAS supprimer automatiquement les nulls (perte de données silencieuse), juste logger.

---

### TÂCHE 3 — Régler la vraie cause racine du `NetworkManager.Singleton == null` dans `PowerManager`
**Fichier :** `Assets/Scripts/GameLogic/PowerManager.cs`

**Problème :** `PowerManager.Start()` accède directement à `NetworkManager.Singleton.IsServer` sans garantir que NGO est initialisé.

**Action :** Remplacer le pattern actuel par une **souscription événementielle** au cycle de vie NGO.

État cible :
```csharp
private void Start()
{
    Power.onPowerSpawned += OnPowerSpawned;

    if (NetworkManager.Singleton == null)
    {
        // PowerManager est en scène mais NetworkManager n'est pas chargé (ex: scène ouverte directement en éditeur).
        // Aucune logique réseau ne tournera ; on log et on sort proprement.
        Debug.LogWarning("PowerManager: NetworkManager.Singleton absent. PowerManager restera inactif (probable lancement hors BootScene).");
        return;
    }

    if (NetworkManager.Singleton.IsListening)
    {
        // NGO déjà up : on s'abonne tout de suite
        TrySubscribeServerHooks();
    }
    else
    {
        // NGO pas encore prêt : attendre l'événement
        NetworkManager.Singleton.OnServerStarted += TrySubscribeServerHooks;
        NetworkManager.Singleton.OnClientStarted += TrySubscribeServerHooks;
    }
}

private void TrySubscribeServerHooks()
{
    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
    if (GameManager.instance == null) return;
    GameManager.instance.onGameStarted += OnGameStarted;
}

private void OnDestroy()
{
    Power.onPowerSpawned -= OnPowerSpawned;
    if (NetworkManager.Singleton != null)
    {
        NetworkManager.Singleton.OnServerStarted -= TrySubscribeServerHooks;
        NetworkManager.Singleton.OnClientStarted -= TrySubscribeServerHooks;
    }
    if (instance == this) instance = null;
}
```

**Justification :**
- Le Warning au lieu du LogError documente le cas légitime "scène ouverte directement". L'utilisateur saura que c'est attendu.
- L'abonnement aux events `OnServerStarted` / `OnClientStarted` garantit qu'on agit quand le réseau est VRAIMENT prêt.
- Pas de null-check défensif sur chaque méthode — la subscription est conditionnelle.

**Note :** Les autres null-checks restants dans `PowerManager` (`OnPowerSpawned` L43, `OnPowerUsedServer` L58, etc.) sont OK car ils protègent contre des invocations légitimes depuis des objets réseau. Garder.

---

### TÂCHE 4 — Vérifier le flux d'attribution de rôle pour les nouveaux rôles
**Fichier :** `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs`

**À vérifier (pas forcément à modifier) :**
1. Le `SerializedDictionary<RoleDataObject, RoleAttributionSetting> roleAttributionDictionary` de l'instance `RoleAttributionState` dans `GameScene.unity` contient-il **Uges** ? Si non, c'est la cause du "errors when adding role" : l'utilisateur essaie d'attribuer Uges mais le dictionnaire ne le connaît pas.
2. L'instance `AwakeningState` dans la scène a-t-elle Uges dans son `awakeningOrder` ? Si non, Uges ne se réveillera jamais.

**Comment vérifier :** ouvrir `GameScene.unity` dans l'Inspector via Unity, sélectionner les GameObjects portant `RoleAttributionState`/`AwakeningState` (ce sont probablement des ScriptableObjects référencés depuis `GameManager.gameStates`), et confirmer que Uges est listé. Sinon, ajouter une entrée dans chaque.

---

### TÂCHE 5 — Diagnostiquer côté runtime
Une fois les tâches 1-3 appliquées :

1. `mcp__UnityMCP__read_console` action `clear`.
2. Demander à l'utilisateur de reproduire le bug (créer lobby → ajouter Uges → lancer la partie).
3. `mcp__UnityMCP__read_console` action `get`, count `'100'`, format `'detailed'`, `include_stacktrace: true`, types `['error', 'warning']`.
4. Analyser les NRE/exceptions restantes. Cibler en priorité :
   - Exceptions dans `Role.NetworkSerialize` → tâche 2 incomplète.
   - Exceptions dans `GameIntroductionState.StateUpdateClient` (L86) → `GetLocalCharacter().role` ou ses sous-champs null.
   - Exceptions dans `PowerManager.OnGameStarted` → un `Character` sans `role` correctement assigné.

---

### TÂCHE 6 — Lancer la suite de tests
```
mcp__UnityMCP__run_tests
```
Aucune régression attendue. Si des tests `Role`/`RoleAttributionState`/`PowerManager` existent, ils valident les changements.

Tests pertinents probables :
- `Assets/Scripts/Tests/PlayMode/GameManagerTests.cs`
- `Assets/Scripts/Tests/PlayMode/PowerTests.cs`
- `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`

---

## 4. Anti-patterns à NE PAS reproduire

- ❌ Ajouter `if (X == null) return;` sans investiguer pourquoi `X` est null.
- ❌ Muter de l'état dans `NetworkSerialize` (cf. tâche 2).
- ❌ Capter `Exception` silencieusement dans les RPC pour faire taire le crash — ça cache le bug aux autres clients.
- ❌ Modifier `RoleID` en réutilisant un ID existant (chaque ID doit être unique).
- ❌ Éditer manuellement les `.asset` YAML — passer par l'Inspector Unity.

---

## 5. Critères de succès

- [ ] Pas d'erreur dans la console à la création du lobby.
- [ ] Pas d'erreur en cliquant sur les sliders de `RoleAttributionSettingTab`.
- [ ] `GameIntroductionState` se déclenche bien (texte de révélation de rôle visible).
- [ ] Le pattern `if (NetworkManager.Singleton == null) return;` ne subsiste qu'à un seul endroit dans `PowerManager.Start()` avec un commentaire justifiant le cas (scène ouverte hors BootScene).
- [ ] Les tests PlayMode passent.
- [ ] L'asset Uges a la même structure YAML que les autres rôles (champs `EventReference` présents).
