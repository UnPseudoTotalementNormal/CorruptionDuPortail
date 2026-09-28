# RAPPORT — R2 : Refactor GameSettingsManager

## Objectif

Refactorer `GameSettingsManager` : supprimer la duplication (recherches par rôle, chemins
Request/Submit/Apply), extraire les invariants `max`/`forced` dans un POCO testable de la couche
Domain, sans changer le comportement réseau ni les données sérialisées, et ajouter des tests EditMode.

## Fichiers modifiés / créés

### Créés

- `Assets/Scripts/Domain/RoleSettingValue.cs` (+ `.meta`)
  Nouveau POCO pur (`readonly struct`) de la couche Domain (`CorruptionDuPortail.Domain`). Porte
  l'invariant unique `0 ≤ Forced ≤ Max ≤ maxRoleCount` et les deux transitions qui étaient inline
  dans l'adaptateur :
  - `WithMax(newMax, maxRoleCount)` : clampe `max` dans `[0, maxRoleCount]` puis re-clampe `forced`
    vers le bas (miroir de `Mathf.Clamp(max,0,MaxRoleCount)` + `Mathf.Min(forced, clampedMax)`).
  - `WithForced(newForced)` : clampe `forced` dans `[0, Max]`, `Max` inchangé (miroir de
    `Mathf.Clamp(forced, 0, max)`).
  - `CanBeFake => Forced < Max` (miroir exact de l'ancienne dérivation).
  Aucune dépendance moteur : `Clamp` réimplémenté à la main (Domain interdit `UnityEngine.Mathf`,
  `noEngineReferences: true`). Le seul dépendant `System` est `IEquatable`/`GetHashCode`, déjà utilisé
  par d'autres types Domain (`ConnectHandshakePolicy`, `IconStackLayout`).

- `Assets/Scripts/Tests/Editor/RoleSettingValueTests.cs` (+ `.meta`)
  13 tests EditMode `[Category("RoleSettingValue")]` (assembly `Tests.Editor`, qui référence déjà
  `CorruptionDuPortail.Domain`). Couvre : construction, `CanBeFake` (forced<max / ==max), `WithMax`
  (clamp haut à 15, clamp bas à 0, forced préservé si ≤ nouveau max, forced re-clampé si max baisse,
  cas pathologique max/forced au-delà du plafond), `WithForced` (clamp à max, clamp à 0, valeur en
  range), immutabilité du `readonly struct`, sémantique de valeur `Equals`/`GetHashCode`.

### Modifiés

- `Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs`
  - Ajout `using CorruptionDuPortail.Domain;`.
  - **Recherches par rôle** : les 6 boucles `for` quasi identiques (dans `ContainsRole`,
    `GetRoleCount`, `GetForced`, `GetCanBeFake`, `ApplyRoleCountServer`, `ApplyForcedServer`) sont
    remplacées par un seul helper `IndexOfRole(RoleID) : int` (retourne -1 si absent). `ContainsRole`
    devient `IndexOfRole(...) >= 0`. `GetTotalRolesToAttribute` / `GetTotalForced` restent des sommes
    sur toute la liste (pas de recherche, pas de duplication).
  - **Invariants max/forced** : `ApplyRoleCountServer` et `ApplyForcedServer` délèguent le calcul au
    POCO via `ToValue(entry).WithMax(...)` / `.WithForced(...)`. L'écriture idempotente dans la
    `NetworkList` est centralisée dans `WriteIfChanged(index, RoleSettingValue)` (un seul endroit pour
    le « skip no-op replication »). `ToValue(RoleSettingEntry)` fait le pont DTO réseau → valeur Domain.
  - Le DTO répliqué `RoleSettingEntry` (`INetworkSerializable`, champs `roleId`/`max`/`forced`) et son
    `NetworkSerialize` sont **inchangés** → format réseau et données sérialisées identiques.
  - La constante `MaxRoleCount = 15` reste dans l'adaptateur (elle reflète le max du slider du prefab,
    une décision de design côté Unity) et est passée en paramètre au POCO.

## Préservation du comportement

- **Réseau / sérialisation** : `RoleSettingEntry` + `NetworkSerialize` intacts ; les deux RPC
  (`SubmitRoleCountServerRpc`, `SubmitForcedServerRpc`) conservent leur signature et leur porte
  `_allowClientEditing` — aucune fusion de RPC (qui aurait modifié le fil réseau). Les chemins
  `Request*` gardent la même logique host-authoritative (IsServer → Apply ; sinon gate → Submit).
- **Maths de clamp** : `WithMax`/`WithForced`/`CanBeFake` reproduisent exactement `Mathf.Clamp` et
  `Mathf.Min` pour les entrées utilisées ici (min = 0 ≤ max). `Clamp` maison : `value<min→min`,
  `value>max→max`, sinon `value`, identique à `Mathf.Clamp`.
- **Idempotence** : `WriteIfChanged` compare `max` ET `forced` avant d'écrire. Pour le chemin `forced`
  (où `max` ne change pas) la comparaison sur `max` est toujours vraie, donc elle se réduit à
  l'ancienne comparaison sur `forced` seul — comportement identique (pas de fire `OnSettingsChanged`
  redondant).
- **Role introuvable** : `IndexOfRole < 0` → return sans mutation, comme les anciennes boucles qui ne
  trouvaient aucune correspondance (couvert par le test PlayMode existant
  `HostEdit_RoleIdNotInSettings_NoOps`).
- Les tests PlayMode existants (`Assets/Scripts/Tests/PlayMode/GameSettings/GameSettingsManagerTests.cs`)
  couvrent seed / read / clamp `[0,15]` / no-op idempotent / role-not-found — tous ces chemins sont
  préservés par le refactor.

## Tests ajoutés

- `RoleSettingValueTests` (EditMode, 13 cas) — voir ci-dessus. C'est la couverture EditMode demandée :
  le POCO extrait est testable sans booter NGO ; la logique réseau reste couverte par la suite PlayMode.

## Risques et hypothèses

- **Certain** : le DTO répliqué et le format de sérialisation ne changent pas.
- **Certain** : les maths de clamp du POCO sont équivalentes aux appels `Mathf` d'origine pour le
  domaine d'entrées de ce code (min = 0).
- **Certain** : `Tests.Editor.asmdef` référence déjà `CorruptionDuPortail.Domain` → les tests compilent
  au niveau des références d'assembly ; `Game.asmdef` référence déjà `CorruptionDuPortail.Domain` →
  l'usage du POCO dans l'adaptateur est autorisé (sens `Game → Domain`, jamais l'inverse).
- **Incertain (à vérifier dans Unity)** : compilation réelle et exécution des tests (pas d'éditeur ni
  de compilateur ici). Vérifier `read_console` puis `run_tests` (EditMode, catégorie `RoleSettingValue`,
  et PlayMode `GameSettingsManagerTests`). Vérifier aussi que le `DomainPurityGuard` reste vert (le
  POCO n'ajoute aucune référence moteur au Domain).
- **Incertain** : GUIDs des `.meta` générés manuellement (uniques, format `fileFormatVersion: 2` +
  guid 32 hex, comme les scripts existants) ; Unity les acceptera, mais un import réel n'a pas été fait.
- **Hypothèse** : `new(...)` typé par cible (`ToValue`) est supporté (C# 9 / Unity 6000) — déjà utilisé
  ailleurs dans le dépôt (`new NetworkList<...>()`, initialisateurs `new()`).

## Choix délibéré

- Les deux RPC `Submit*ServerRpc` n'ont **pas** été fusionnés en un seul RPC paramétré : cela aurait
  changé le format du message réseau (contrainte « ne pas changer le comportement réseau »). La
  déduplication porte donc sur les recherches par rôle, les maths d'invariant (POCO) et l'écriture
  idempotente (`WriteIfChanged`), pas sur la surface RPC.
