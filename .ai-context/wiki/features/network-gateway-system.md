# 🛠️ Feature : Network Gateway System

> [!IMPORTANT]
> **Rôle** : Permet au serveur de communiquer avec des "joueurs simulés" (bots) en reroutant dynamiquement les RPC destinés à ces IDs virtuels vers la machine Host.

## 🎬 Déclencheur (Point d'entrée)
L'appel à `CharacterManager.instance.GetSafeRpcTarget(clientId)` avant l'envoi d'un RPC.

## 📦 Composants Clés
| Composant | Responsabilité |
| :--- | :--- |
| `CharacterManager` | Implémente le routage `GetSafeRpcTarget` et l'abstraction d'identité `IsLocalOrSimulated`. |
| `Character` | Stocke le `clientId` (ID réel ou Virtuel >= 100). |
| `RpcParams` | Paramètre de destination Netcode injecté avec la destination redirigée. |

## 💾 Données & État
- **Entrées** : `clientId` du destinataire (0-99 pour réels, 100+ pour simulés).
- **Persistance** : Convention d'ID en mémoire dans le `CharacterManager`.
- **Sorties** : `RpcSendParams` pointant vers l'Host (ID 0) si le destinataire est un bot.

## 🕸️ Couplage & Dépendances
```mermaid
graph LR
    PowerAction --> CM[CharacterManager]
    CM --> NGO[Netcode for GameObjects]
    NGO --> Host[Host Client / ID 0]
```

## ⚠️ Points d'Attention & Risques
- [ ] **Performance** : Léger surcoût CPU pour le check d'ID sur chaque RPC routé.
- [ ] **Sécurité** : `IsLocalOrSimulated` doit être utilisé scrupuleusement dans toutes les gardes client/serveur pour éviter les triches.
- [ ] **Dette** : Risque de **Stack Overflow** si un RPC client simulé redéclenche immédiatement un RPC serveur sans garde.

---
> [!SUCCESS]
> **Critères de Validation** : Un bot déclenchant un pouvoir sur le serveur voit ses effets visuels s'afficher correctement sur l'écran de l'Host (qui possède alors son identité).
