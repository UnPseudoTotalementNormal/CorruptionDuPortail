# Light & FX Managers

## Rôle
Génèrent et maintiennent la chromatique et l'éclairage de la salle en fonction de la narration (Phase de jour / Phase de nuit / Corruption).

## Déclencheur (Point d'entrée)
- Abonnement structurel aux événements du `GameManager` (Ex: `onStateStart` ou `onDayPassed`).

## Composants Clés
- `LightManager` : Modifie les Global Lights, le post-processing et teinte l'ambiance avec des fondus animés (DOTween/Mathf.Lerp).
- `CardEffectManager` : Injecte structurellement des PostFX ou particules locaux à certaines Cartes ciblées par la corruption (Board).

## Points d'attention
- Bien que mineurs, ces systèmes s'interposent sur la performance visuelle (Fill-rate des shaders de brume/vignettage). Ils doivent répondre prestement aux interruptions (`DOKill()`) si le serveur saute plusieurs phases prématurément pour cause de déconnexion.
