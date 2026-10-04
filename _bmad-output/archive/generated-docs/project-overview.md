# Project Overview — Corruption du Portail

## Identity

- **Name:** Corruption du Portail
- **Genre:** Asymmetric multiplayer social deduction (Werewolf / Mafia family)
- **Engine:** Unity **6000.2.6f2**
- **Repository type:** Monolith, single-part Unity project
- **Primary language:** C# (.NET via Unity scripting backend)
- **Main branch:** `Dev` (PR target)
- **Current branch at scan:** `Dev` — the behaviour-preserving refactor (Epics 1–12: POCO + de-singleton + despaghettification) is **merged** (PR #53, commit `9daace2`)

## Tech stack summary

| Category | Technology | Notes |
|---|---|---|
| Engine | Unity 6000.2.6f2 | URP render pipeline |
| Networking | Netcode for GameObjects 2.6.0 | Server-authoritative |
| Transport | Facepunch Steam | Production transport |
| Async | UniTask | All async code (not `Task`) |
| Tweens | DOTween + UniTask integration | `AwaitForComplete` |
| Audio | FMOD | `AudioSystem/GameAudioManager` — never `AudioSource` |
| Input | Unity Input System 1.14.2 | `InputSystem_Actions.inputactions` |
| UI | uGUI 2.0 + TextMeshPro + SoftMaskForUGUI | 2D smartphone OS + 3D board |
| Cameras | Cinemachine 3.1.5 | Board cameras |
| Test mocks | NSubstitute (via tnrd Unity3D-NSubstitute) | Editor tests |
| Assets pipeline | Addressables 2.7.2 (+ Android variant) | |
| Backend services | Unity Services (Core, Multiplayer, Authentication) | Lobby/auth |
| MCP | CoplayDev/unity-mcp | LLM-side editor automation |

## Architecture type classification

- **Pattern:** Server-authoritative client-server (NGO), **layered** into a pure Domain POCO core → thin NGO/Mono adapters → a `CompositionRoot` DI seam.
- **Signature mechanics:**
  - Gateway RPC system — the Host can simulate additional bot/player identities locally (`clientId >= 100`) for full-lobby solo debugging.
  - Composition root + three-lane injection (SerializeField / Initialize / OnNetworkSpawn) — 24 singletons collapsed to **one** sanctioned static (`CompositionRoot`); consumers depend on narrow injected interfaces (`IGameLoop`, `IGameStateQuery`, `ICharacterQuery`, `ICharacterCommand`), enforced by three CI guards.
- **Game loop:** `Lobby → Introduction → Awakening → Chaining → Vote → Recap → GameEnding`
- **Refactor docs:** [refactor-architecture-despaghetti.md](./refactor-architecture-despaghetti.md), [refactor-architecture-poco.md](./refactor-architecture-poco.md), [refactor-architecture-desingleton.md](./refactor-architecture-desingleton.md).

## Repository structure

Monolith Unity project. Source organized **feature-first** under `Assets/Scripts/`. Each system is self-contained in its own folder (Characters/, Board/, GameLogic/, Smartphone/, Network/, etc.).

Assembly definitions:
- `CorruptionDuPortail.Domain.asmdef` — **pure POCO decision core, no UnityEngine** (purity-guarded)
- `Game.asmdef` — main runtime (references Domain)
- `Game.Editor.asmdef` — editor tooling
- `Game.Rendering.asmdef` — render-pipeline-bound code
- `Tests.Editor.asmdef` — pure C# tests (NSubstitute) — incl. Domain golden masters + DI guards
- `Tests.PlayMode.asmdef` — networked tests (NetworkTestHelper, MultiClientGameFixture)
- `com.community.netcode.transport.facepunch.asmdef` — Steam transport bundle

## Pre-existing documentation discovered

| Path | Role |
|---|---|
| `CLAUDE.md` | Primary AI-agent contract (architecture + critical patterns + commit conventions) |

## Links

- [Architecture](./architecture.md)
- [Source tree](./source-tree-analysis.md)
- [Development guide](./development-guide.md)
- [State management](./state-management.md)
- [Asset inventory](./asset-inventory.md)
- [Master index](./index.md)
