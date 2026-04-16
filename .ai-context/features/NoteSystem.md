# Note System

## Rôle
Une "Application" contenue dans le Smartphone permettant à un joueur humain de conserver une trace écrite, colorée ou visuelle de ses déductions quant à l'identité des autres.

## Déclencheur (Point d'entrée)
- Navigation par le `SmartphoneController` ouvrant l'U.I. spécifique des Notes.

## Composants Clés
- `NoteManager` : Logique interne d'enregistrement et historisation.
- `NoteRibbon`, `NoteChoosePanel` : Les rubans et panneaux encapsulant l'UI pour modifier ou affecter une note sur un joueur adverse.

## Données & État
- Totalement Client-Side. C'est purement une donnée locale isolée (à moins que le serveur ait besoin de tracker l'assiduité, ce qui est peu probable pour la logique métier brute).

## Couplage & Dépendances
- Couplé au `CharacterManager` client pour récupérer la grille des joueurs connectés nécessitant une feuille de note.

## Points d'attention
- A ne pas confondre avec le GameLogic : Ce système N'AFFECTE PAS les variables réelles du réseau et n'interfère avec aucun Callback de vote. C'est du "Quality of Life" diégétique pour le joueur.
