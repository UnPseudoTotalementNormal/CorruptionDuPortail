# RAPPORT — R2 : refactoring de GameSettingsManager

## Préambule

- `ProjectSettings/ProjectVersion.txt` indique **6000.5.0f1**, alors que `CLAUDE.md` et `_bmad-output/project-context.md` indiquent **6000.2.6f2 (exact)**. La règle projet dit de s'arrêter et de le signaler en cas d'écart. Je le signale ici. Je n'ai pas modifié ce fichier, et le refactoring n'utilise aucune API propre à l'une ou l'autre version.
- Aucun éditeur Unity n'était disponible : **rien n'a été compilé ni exécuté**. Les tests ajoutés n'ont jamais tourné.

## Fichiers modifiés / créés

| Fichier | Nature | Pourquoi |
|---|---|---|
| `Assets/Scripts/Domain/RoleSettingValues.cs` (+ `.meta`) | **nouveau** | POCO de la couche Domain (`readonly struct`, sans moteur). Il porte les invariants max/forced : `MaxRoleCount = 15`, `WithMax` (borne à [0,15] puis ramène `forced` à `min(forced, max)`), `WithForced` (borne à [0, max]), `CanBeFake` (`forced < max`), égalité par valeur. Il renvoie une décision et n'applique rien. |
| `Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs` | refactor | Voir le détail plus bas. |
| `Assets/Scripts/Tests/Editor/RoleSettingValuesTests.cs` (+ `.meta`) | **nouveau** | Tests EditMode du POCO, dont des tests de parité avec les anciennes formules. |
| `Assets/Scripts/Tests/PlayMode/GameSettings/GameSettingsManagerTests.cs` | ajout de 3 tests + 1 commentaire mis à jour | Couvrir le chemin `RequestSetForced`, qui n'avait aucun test, et le re-bornage de forced quand on baisse max. |

### Détail du refactor de `GameSettingsManager`

1. **Recherches par rôle** : `ContainsRole` et les quatre boucles identiques (`GetRoleCount`, `GetForced`, `GetCanBeFake`, les deux `Apply*Server`) passent toutes par un seul `private int IndexOf(RoleID)`, qui renvoie le premier index trouvé ou -1.
2. **Chemins Request/Submit/Apply** : les deux copies sont fusionnées en trois méthodes privées, paramétrées par un `private enum RoleSettingField { Max, Forced }` imbriqué :
   - `RequestEdit(field, roleId, value)` : le serveur applique directement ; un client passe par le RPC du champ, uniquement si `_allowClientEditing` est actif.
   - `OnClientEditReceived(field, roleId, value)` : second contrôle côté serveur (défense en profondeur) sur `_allowClientEditing`, puis application.
   - `ApplyEditServer(field, roleId, value)` : garde `IsServer`, `IndexOf`, calcul de la décision par `RoleSettingValues.WithMax/WithForced`, puis écriture dans la `NetworkList` seulement si la valeur change.
3. Les méthodes publiques `RequestSetRoleCount` / `RequestSetForced` gardent leur signature ; elles délèguent à `RequestEdit`.
4. `private const int MaxRoleCount` est supprimé du manager. La valeur vit maintenant dans `RoleSettingValues.MaxRoleCount`.
5. `RoleSettingEntry` gagne une propriété calculée en lecture seule, `Values => new RoleSettingValues(max, forced)`. Aucun champ ajouté ; `NetworkSerialize`, `Equals` et `GetHashCode` sont inchangés.

## Comment le comportement est préservé

- **Protocole réseau** *(certain pour ce qui est visible dans le code)* : les deux RPC `SubmitRoleCountServerRpc(RoleID, int)` et `SubmitForcedServerRpc(RoleID, int)` gardent le même nom, la même signature et le même attribut `[Rpc(SendTo.Server)]`. Seul leur corps change : il délègue à `OnClientEditReceived`. Je ne les ai volontairement **pas** fusionnés en un seul RPC paramétré, car cela aurait changé l'identité des messages RPC. Aucun `GetSafeRpcTarget` n'était présent ni requis (RPC vers le serveur, sans clientId cible), et je n'en ai pas ajouté.
- **Données sérialisées** *(certain)* : aucun champ `[SerializeField]` renommé, réordonné ou retypé (`_authoredSettingsSource`, `_allowClientEditing`). `RoleSettingEntry` garde les mêmes champs dans le même ordre et le même `NetworkSerialize`. La propriété `Values` n'est pas sérialisée. `NetworkList<RoleSettingEntry>` reste inchangé.
- **Sémantique des bornes** *(certain pour la logique, incertain pour la conformité exacte à Mathf dans 6000.5, voir risques)* : le Domain n'a pas accès à `Mathf`. `RoleSettingValues` réimplémente donc `Mathf.Clamp(int,int,int)` (borne basse testée en premier, y compris quand min > max) et `Mathf.Min(int,int)`. Les tests `LegacyParity_*` comparent le POCO aux **anciennes formules exactes** écrites avec le vrai `UnityEngine.Mathf`, sur une grille de -3 à 20 pour max, forced et valeur demandée, valeurs hors bornes comprises (valeurs authored négatives ou supérieures à 15).
- **Détection des no-op** *(certain)* : l'ancien chemin max comparait `max == clampedMax && forced == clampedForced`. L'ancien chemin forced comparait `forced == clamped`, max ne pouvant pas changer. Le nouveau code compare `next == current` sur le couple (max, forced), ce qui donne exactement la même décision dans les deux cas. Un no-op n'écrit toujours rien, donc ni réplication ni `OnSettingsChanged`.
- **Rôle absent** *(certain)* : les getters renvoient toujours 0 / 0 / false, et les éditions restent des no-op.
- **Premier match** *(certain)* : `IndexOf` renvoie le premier index, comme les anciennes boucles. Le seed rejette toujours les RoleID en double avec le même `Debug.LogError`.
- **Seed** *(certain)* : inchangé. Les valeurs authored sont copiées telles quelles, sans bornage. Le constructeur de `RoleSettingValues` ne borne volontairement pas.
- **Autorité serveur** *(certain)* : les gardes `IsServer` (application) et `_allowClientEditing` (côté client et côté serveur) sont conservées, dans le même ordre.
- Totaux (`GetTotalRolesToAttribute`, `GetTotalForced`) et cycle de vie (`OnNetworkSpawn`/`OnNetworkDespawn`, abonnement `OnListChanged`) : non modifiés.

## Tests ajoutés (non exécutés)

**EditMode**, `Tests.Editor/RoleSettingValuesTests` (`[Category("RoleSettingValues")]`) :
- constante `MaxRoleCount == 15` ; le constructeur ne borne pas ;
- `CanBeFake` : 5 cas (forced < max, ==, >, pool vide) ;
- `WithMax` : valeur dans les bornes (forced n'est jamais augmenté), au-dessus de 15, exactement 15, valeur négative (forced ramené à 0), max passé sous forced (forced ramené), même valeur (égal à l'actuel, donc no-op), max authored hors bornes (bornage dès la première édition, donc pas un no-op), non-mutation ;
- `WithForced` : valeur dans les bornes, au-dessus de max, valeur négative, pool vide, borné par le max de l'entrée et non par 15, même valeur (no-op) ;
- égalité par valeur (`Equals`, `==`, `!=`, `GetHashCode`) ;
- **parité** : `LegacyParity_WithMax_MatchesOldApplyRoleCountServer`, `LegacyParity_WithForced_MatchesOldApplyForcedServer`, `LegacyParity_CanBeFake_MatchesOldGetCanBeFake`.

**PlayMode**, `Tests.PlayMode/GameSettingsManagerTests`, 3 cas ajoutés à la fixture existante (hôte unique) :
- `HostEditForced_ClampsToRoleMax_AndDrivesCanBeFake`
- `HostEditMax_BelowForced_ReclampsForcedDown`
- `HostEditForced_NoOpValueOrUnknownRole_DoesNotRaiseEvent`

## Risques et hypothèses

- **Certain** : aucune API inventée. Le Domain n'utilise que `System` (`IEquatable`, `HashCode.Combine`), déjà utilisés dans `GameSettingsManager`. `Tests.Editor` référence déjà `CorruptionDuPortail.Domain`, et l'asmdef Domain n'est pas modifié (`references: []`, `noEngineReferences: true`).
- **Incertain** : la réimplémentation de `Mathf.Clamp`/`Mathf.Min` s'appuie sur leur implémentation publique connue (UnityCsReference). Si 6000.5 avait changé ce comportement dans les cas limites (min > max), les tests `LegacyParity_*` le détecteraient.
- **Incertain** : l'explication « NGO dérive l'identifiant du RPC de la méthode » (commentaire dans le code) décrit ce que je comprends de l'ILPP de NGO. Quoi qu'il en soit, garder les RPC à l'identique est le choix sûr.
- **Incertain** : je n'ai pas pu vérifier que l'ILPP de NGO accepte une propriété calculée sur un struct `INetworkSerializable` utilisé dans une `NetworkList<T>`. Je m'attends à ce que ça passe, puisqu'aucun champ n'est ajouté et que la contrainte `unmanaged` porte sur les champs. Si l'ILPP se plaint, il suffit de remplacer `Values` par une méthode statique privée du manager.
- **Incertain** : les fichiers `.meta` ont été créés à la main avec des GUID aléatoires, au format minimal utilisé par les autres `.cs.meta` du dépôt. Unity devrait les compléter à l'import.
- **Certain** : les guards `DiSeamGuard`/`StaticAbsenceGuard` ne devraient pas être affectés. Aucun `.instance`, `For(` ou `CompositionRoot` ajouté, aucun static mutable ni singleton.
- Hors périmètre, non modifié : `DemoLobbyRolesDataSource` duplique sa propre constante `MaxRoleCount = 15` et ses bornes ; il pourrait réutiliser `RoleSettingValues`. Le désaccord entre 15 et `[Range(0,10)]` est toujours signalé en commentaire.

## À vérifier dans Unity

1. `read_console` : compilation sans erreur de `CorruptionDuPortail.Domain`, `Game`, `Tests.Editor` et `Tests.PlayMode`. Vérifier en particulier qu'il n'y a pas d'erreur ILPP de Netcode sur `GameSettingsManager` / `RoleSettingEntry`.
2. EditMode : catégories `RoleSettingValues`, `DomainPurity`, `DiSeamGuard`, `SceneWiringGuard`, `StaticAbsenceGuard`, puis la suite complète.
3. PlayMode : `GameSettingsManagerTests` (6 tests existants + 3 nouveaux), puis la suite complète, y compris les golden masters de distribution des rôles.
4. Smoke test manuel en lobby hôte + client : sliders max/forced côté hôte, bornes à 15/0, forced qui suit quand on baisse max, client en lecture seule par défaut. Si possible, `_allowClientEditing = true` sur un build de test pour exercer les deux RPC de bout en bout.
5. Vérifier que les `.meta` générés n'entrent pas en conflit de GUID à l'import.
