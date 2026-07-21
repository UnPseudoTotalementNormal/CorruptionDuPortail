# Investigation: le passif de l'Orpheline « ne fait rien » et n'apparaît pas sur la RoleCard

## Hand-off Brief

1. **What happened.** Le nouveau passif `TargetedByReport` de l'Orpheline est bien attaché au rôle (Inspector : Powers = LackOfAffection + TargetedByReport, tous deux résolus en `Power`), mais il n'apparaît pas sur la RoleCard et un test antérieur « me faire cibler par le Mage » n'a rien produit.
2. **Root cause.** DEUX causes distinctes, non liées : (A) — **Confirmé** — la carte cache tout passif dont `powerDescription` est vide (`RoleCardPowerVisibility.cs:52`), et le prefab a une description vide ; (B) — **Confirmé pour le contexte du test** — le test « n'a rien fait » tournait sur un **build .exe périmé** portant l'ancien `Orpheline.asset` (pouvoir null), avant le fix de fileID.
3. **Fix direction.** (A) renseigner `powerDescription` sur `TargetedByReport.prefab` — cosmétique, une ligne. (B) re-tester dans l'éditeur ou rebuild : la logique est saine, aucune autre cause identifiée.

## Case Info

- **Slug** : orpheline-passive-not-showing
- **Date** : 2026-07-20
- **Branche** : feat/targeting-icons
- **Statut** : Concluded (symptôme carte : High) / le comportement fonctionnel reste à re-vérifier en contexte propre

## Problem Statement

Poyo a câblé le passif, l'Inspector le montre attaché (2 éléments dans Powers), mais la RoleCard n'affiche que « Manque d'affection ». Plus tôt, se faire cibler par le Mage Occulte n'a produit aucune icône.

## Evidence Inventory

| Évidence | Grade | Source |
|---|---|---|
| Powers du SO = LackOfAffection + TargetedByReport, résolus | Confirmé | Screenshot Inspector + `Orpheline.asset` fileID `2751029845932013476` |
| `powerDescription` du prefab = vide (0 octet) | Confirmé | `TargetedByReport.prefab:148` |
| Un passif sans description → `Hidden` | Confirmé | `RoleCardPowerVisibility.cs:52` |
| La carte lit `hasDescription = !IsNullOrEmpty(powerDescription)` | Confirmé | `RoleCardController.cs:243` |
| Le test « rien » tournait sur un build .exe | Confirmé | Console : `Builds/PlayModeScenarios/WindowsBuildProfile`, `WindowsPlayer(Poyoboi)` |
| Le Mage enregistre bien un ciblage (inconditionnel) | Confirmé | `EmbraceOfShadowsDecision.cs:20,31` (`new NewTargeting`) |

## Confirmed Findings

### A — La carte cache le passif car sa description est vide (root cause du symptôme visible)

`RoleCardPowerVisibility.Classify` ([RoleCardPowerVisibility.cs:52](../../../Assets/Scripts/Domain/RoleCardPowerVisibility.cs#L52)) :
```csharp
if (isPassive)
    return hasDescription ? RoleCardSlot.PassiveRow : RoleCardSlot.Hidden;
```
Le prefab `TargetedByReport.prefab` a `powerDescription` à 0 octet → `hasDescription = false` → `Hidden`. **Comportement voulu** : un passif sans texte ne pollue pas la carte. Le placeholder a simplement omis la description.

### B — Le test fonctionnel tournait sur un build périmé

La console montre `WindowsPlayer(Poyoboi)` connecté à un build sous `Builds/PlayModeScenarios/`. Ce .exe embarque l'`Orpheline.asset` d'avant le fix de fileID, où l'entrée du pouvoir résolvait à `null` → le passif n'était pas spawné → aucune souscription fin-de-nuit → rien. Le fix (fileID `7205411800377995968` GameObject → `2751029845932013476` composant) n'existe que côté éditeur tant que le build n'est pas refait.

Chemin fonctionnel vérifié sain une fois le pouvoir attaché : `EmbraceOfShadows` émet `NewTargeting` inconditionnellement → `RoleTargetSystem` enregistre → fin de nuit, `PTargetedByReport` lit `GetAllTargetersForTarget` → `AddPlayerIcon` → poussé à l'Orpheline seule.

## Final Conclusion

- **Symptôme « pas sur la carte » : root cause Confirmée (High).** Description vide → `Hidden` par design. Cosmétique.
- **Symptôme « rien quand ciblée » : Confirmé comme artefact du build périmé (Medium).** Aucune autre cause trouvée ; à re-vérifier en éditeur/rebuild avant de conclure définitivement.

## Fix direction

1. Renseigner `powerDescription` sur `TargetedByReport.prefab` (texte joueur, ex. « Chaque nuit, elle repère les rôles qui l'ont ciblée. »). Débloque l'affichage carte.
2. Re-tester en éditeur (Play, 2 instances) ou rebuild le client. Attendu : icône placeholder sur la vignette du cibleur, **fin de nuit**, côté Orpheline seule.
