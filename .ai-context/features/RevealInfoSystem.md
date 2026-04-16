# Game Info Revealer System

## Rôle
Agit comme la source de vérité pour déterminer quelles informations cachées (Rôles, état de corruption) sont actuellement révélées à un client spécifique ou publiquement à tous. C'est l'arbitre du "brouillard de guerre" asymétrique.

## Déclencheur (Point d'entrée)
- **Initialisation :** S'abonne à la fin de la distribution des rôles (`RoleAttributionState`). Par défaut, seul chaque joueur connaît son propre rôle (`RevealLevel.Personal`).
- **Révélation :** Invoqué par d'autres systèmes (ex: la mort d'un joueur, un pouvoir asymétrique) via `SetRevealLevel` ou potentiellement son RPC `SetRevealLevelRpc`.

## Composants Clés
- `GameInfoRevealer` : NetworkBehaviour agissant en tant que "Dictionary Manager" des révélations pour synchroniser l'affichage public/privé.
- `CharacterInfoReveal` : Classe de données encapsulant les variables de révélation d'un joueur (`isRoleRevealed`, `isCorruptRevealed`).
- `RevealLevel` : Énumérateur délimitant la portée de l'information (`False`, `Personal`, `Public`).

## Données & État
- `charactersInfoRevealed` : Dictionnaire localisant quel ClientID dispose de quel niveau de révélation sur ses propres attributs.
- Utilise massivement la **Réflexion C#** (`GetField()`, `SetValue()`) pour mapper les Enum "noms de variables" (`FixedString64Bytes`) directement à l'instance de `CharacterInfoReveal`.

## Couplage & Dépendances
- **Couplage Front-end agressif :** Dépend du `BoardManager.instance.visibleCards`. Par exemple, le déblocage visuel de la carte d'un rôle (retournement complet) est hard-codé dans un `switch-case` qui observe le nom de variable "isRoleRevealed".

## Points d'attention
- **Réflexion C# :** L'usage de `typeof(CharacterInfoReveal).GetField(_revealVariableName.ToString())` pour éviter du boiler-plate peut avoir un coût de performance (mineur) mais surtout un risque silenicieux. Si le nom d'une variable de `CharacterInfoReveal` est mal typé lors d'un appel RPC, le système lèvera une assertion et plantera.
- La méthode `GetCharacterInfo` s'assure silencieusement qu'une clé existe en l'ajoutant si manquante. Si envoyée sur des Fake IDs corrompus, la dictionnaire absorbera indéfiniment de la fuite de donnée.
