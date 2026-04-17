# 🤖 WIKI COMPILER : Instructions de Maintenance

Ce document contient les règles strictes que toi, l'IA, dois suivre pour maintenir ce Wiki. Tu agis comme un "Compilateur de Connaissance".

## 🎯 Objectif
Transformer les informations brutes du dossier `raw/` et les changements du code source en une documentation structurée, cohérente et premium dans le dossier `wiki/`.

## 🏗️ Structure de Compilation

1. **RAW -> WIKI** : Analyse les fichiers de `raw/` et fusionne l'info pertinente dans `wiki/`.
2. **GARBAGE COLLECTION** : Une fois l'info d'un fichier `raw/` compilée, tu DOIS impérativement le **supprimer** (ou le déplacer dans `raw/archive/`) pour éviter la double-compilation.
3. **CODE -> WIKI** : Utilise le skill `update-llm-wiki` pour automatiser cette vérification lorsque la Matrice d'Impact est déclenchée.

## 🎯 Matrice d'Impact (Quand mettre à jour ?)
Mise à jour obligatoire SI :
- [ ] Modification d'une **interface publique** ou d'une signature de fonction.
- [ ] Changement du **flux de données** entre deux composants.
- [ ] Nouvelle **dépendance** externe ou interne ajoutée.
- [ ] Changement de la logique de **persistance** ou structure de données.

## 🎨 Règles de Style (Premium MD)

- **Aesthetics First** : Utilise des émojis pour chaque titre de section.
- **Alertes GitHub** : Utilise `> [!IMPORTANT]`, `> [!TIP]`, `> [!WARNING]` pour hiérarchiser l'information.
- **Visuals** : Utilise des schémas Mermaid (`graph TD`, `sequenceDiagram`) pour les flux complexes.
- **Concision** : Pas de blabla. Des listes à puces, des tableaux, et des descriptions directes.
- **Nomenclature** : Nomme impérativement les fichiers de fonctionnalités (dans `features/`) en **kebab-case anglais** (ex: `payment-gateway.md`).

## 🚫 Interdictions
- NE JAMAIS copier de code.
- NE JAMAIS halluciner de feature.
- **Principe de Taille** : Si une info devient évidente ou obsolète, supprime-la. La concision est la clé de la survie du wiki.

---
> [!TIP]
> Si tu identifies une contradiction entre une note dans `raw/` et le code actuel, signale-le immédiatement à l'utilisateur au lieu de mettre à jour le wiki aveuglément.
