# Rapport — F1 : un bot ne reçoit jamais ses infos révélées personnellement (« Simulated 1 »)

## Cause racine (certain)

Dans `Assets/Scripts/GameLogic/GameInfoRevealer.cs`, la méthode `SendRevealLevelRpc`
décidait de rediriger une révélation vers l'Hôte (au lieu de l'envoyer sur le réseau)
avec une comparaison stricte :

```csharp
if (_toObserverId > 100)   // BUG
```

Or les bots simulés reçoivent des `clientId` à partir de **100 inclus**
(`CharacterManager.SpawnSimulatedPlayer`, ligne 407 :
`ulong _debugId = 100 + ...`, nommé `Simulated {_debugId - 99}`). Le **premier** bot
a donc l'id `100` et l'étiquette « Simulated 1 ».

Avec `> 100`, l'id `100` (Simulated 1) ne remplissait pas la condition et tombait dans
la branche `else`, qui envoie un `SetRevealLevelRpc` ciblé
`RpcTarget.Single(100, ...)` sur le transport. Comme un bot simulé n'ouvre jamais de
connexion réseau (voir `ConnectionApprovalGate`, `AvatarManager`, etc. — convention
« clientId >= 100 » dans tout le code), ce RPC n'atterrit nulle part : Simulated 1 ne
reçoit jamais ses révélations personnelles. Les bots suivants (101, 102, …) passaient
`> 100` et étaient correctement redirigés vers l'Hôte, d'où « c'est toujours Simulated 1 ».

C'est une erreur off-by-one : tout le reste du code utilise `>= 100` (dont
`SendClearHackedRpc` juste en dessous, ligne 252). Seul `SendRevealLevelRpc` divergeait.

## Correctif

Fichiers modifiés :

- `Assets/Scripts/Domain/RevealVisibilityRules.cs`
  - Ajout d'une constante `SimulatedBotClientIdFloor = 100` et d'une fonction pure
    `RequiresSimulatedRedirect(ulong observerId) => observerId >= SimulatedBotClientIdFloor`.
    Placée dans la classe de règles pures existante (sans dépendance Unity/NGO) pour être
    testable en EditMode, comme `ShouldRefreshLocalUi`.

- `Assets/Scripts/GameLogic/GameInfoRevealer.cs`
  - `SendRevealLevelRpc` : remplacement de `if (_toObserverId > 100)` par
    `if (RevealVisibilityRules.RequiresSimulatedRedirect(_toObserverId))`.
  - `SendClearHackedRpc` : le `>= 100` littéral (déjà correct) réutilise désormais la
    même fonction, pour une source unique de la règle et éviter toute nouvelle divergence.

- `Assets/Scripts/Tests/Editor/RevealVisibilityRulesTests.cs`
  - Ajout de tests couvrant la frontière : `RealClient_DoesNotRedirect` (0, 1, 99),
    `FirstSimulatedBot_RedirectsToHost` (id 100 = la frontière = « Simulated 1 »),
    `LaterSimulatedBots_RedirectToHost` (101, 102).

## Comportement préservé (certain)

- Pour les bots 101, 102, … : condition inchangée (redirection Hôte) — comportement
  identique à avant.
- Pour les vrais clients (id < 100) : condition toujours fausse — envoi direct inchangé.
- Seul le cas id == 100 change, précisément le cas bogué. `SendClearHackedRpc` est
  strictement équivalent (`>= 100` factorisé, pas de changement de valeur).

## Tests ajoutés

4 tests EditMode dans `RevealVisibilityRulesTests` (voir ci-dessus). Ils vérouillent
la frontière `>= 100`, notamment l'id 100.

## Risques et hypothèses

- Certain : la frontière des bots est bien 100 inclus (confirmé par
  `CharacterManager.SpawnSimulatedPlayer` et la convention `>= 100` omniprésente).
- Certain : la branche `else` envoyait bien un RPC réseau vers un id inexistant pour 100.
- Incertain : je n'ai pas d'éditeur Unity ; je n'ai ni compilé ni exécuté les tests.
  La classe `RevealVisibilityRules` est déjà référencée par le fichier de tests, donc
  l'assembly de tests a bien accès au nouveau membre, mais cela reste à confirmer.

## À vérifier dans Unity

1. Compilation sans erreur (`read_console`).
2. `run_tests` EditMode : les 4 nouveaux tests de `RevealVisibilityRulesTests` passent.
3. Test manuel en partie de debug avec au moins un bot : révéler un rôle en personnel
   à « Simulated 1 » (via un pouvoir) et confirmer que son cerveau simulé reçoit bien
   l'info (et que la carte s'affiche correctement quand on inspecte ce bot).
