# Project Documentation Index

> Generated 2026-05-27 by `/gds-document-project` (Deep scan) — entry point for AI-assisted development.

## Project overview

- **Type:** Monolith, single-part Unity game
- **Engine:** Unity 6000.2.6f2
- **Primary language:** C#
- **Architecture:** Server-authoritative NGO (Netcode for GameObjects) with Gateway-RPC simulated-client layer
- **Genre:** Asymmetric multiplayer social deduction (Werewolf family)

## Quick reference

- **Tech stack:** Unity + NGO 2.6 + FMOD + UniTask + DOTween + Facepunch (Steam) transport
- **Entry scene:** `Assets/Scenes/BootScene.unity`
- **Entry code:** `Assets/Scripts/GameLogic/GameManager.cs`
- **Main branch:** `Dev`
- **Critical pattern:** wrap RPCs with `GetSafeRpcTarget(clientId)`; use `IsLocalOrSimulated(clientId)` not `IsLocalClient`

## Generated documentation

- [Project Overview](./project-overview.md)
- [Architecture](./architecture.md)
- [Source Tree Analysis](./source-tree-analysis.md)
- [Development Guide](./development-guide.md)
- [State Management](./state-management.md)
- [Asset Inventory](./asset-inventory.md)

## Pre-existing documentation

- `CLAUDE.md` (repo root) — primary AI-agent contract: architecture, critical patterns, commit conventions, gotchas. Points to this `_bmad-output/` set as the doc source of truth.

## Getting started (for AI agents)

1. **Always read `CLAUDE.md` first** — it carries the load-bearing patterns (Gateway RPC, server authority, FMOD, UniTask).
2. Open this index for system-level orientation.
3. For deep dives into a subsystem, jump into `Assets/Scripts/<System>/` directly — the layout is feature-first and self-describing.
4. Use Unity MCP tools (`mcp__UnityMCP__*`) for build / test / console rather than CLI Unity.

## Workflow routing (BMad)

| Goal | Skill |
|---|---|
| Bug fix (subtle root cause) | `/gds-investigate` → `/gds-quick-dev` |
| Small/medium feature | `/gds-quick-dev` |
| Large feature, multi-story | `/gds-create-epics-and-stories` → `/gds-sprint-planning` → `/gds-create-story` → `/gds-dev-story` → `/gds-code-review` |
| Test coverage strategy | `/bmad-testarch-test-design` → `/bmad-testarch-automate` |
| Course correction mid-sprint | `/gds-correct-course` |

## Out-of-scope (intentionally not generated)

The project's `documentation-requirements.csv` profile (`project_type_id = game`) does **not** require:
- API contracts (no HTTP service)
- Data models (no relational DB / ORM — only Unity SO + in-memory state, covered in [State Management](./state-management.md))
- Component inventory (UI components are not the dominant architectural element here; see [Source Tree](./source-tree-analysis.md))
- Deployment guide (game-ci handles builds via `Build.yml`)
- Hardware docs

## Verification recap

- **Tests/extractions executed:** project type detection (game), tech-stack parse from `Packages/manifest.json`, scenes enumeration, asmdef enumeration, asset counts across `Assets/`, sample reads of `GameLogic/`, `Characters/`, `Network/`, `Tests/`.
- **Outstanding risks / follow-ups:** several `Scenes/` files are legacy/backup (`(old)MenuScene.unity`, `GameScene_backup.unity`); consider archiving.
- **Recommended next checks before relying on these docs:** open Unity, hit Play in `BootScene`, run `mcp__UnityMCP__run_tests` to baseline test health; cross-read `GameManager.cs` once for the actual state-transition wiring if precise control-flow matters.
