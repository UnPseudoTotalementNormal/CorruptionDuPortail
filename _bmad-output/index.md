# Project Documentation Index

> Generated 2026-05-27 by `/gds-document-project` (Deep scan) — entry point for AI-assisted development.
> **Full rescan 2026-06-13**: regenerated after the despaghettification refactor (Epics 1–12, 306 files / +14k LOC since 2026-05-29) — added Domain POCO layer, `CompositionRoot` DI seam, narrow interface seams. Core docs (architecture, state-management, source-tree, dev-guide, overview, assets) updated to match.

## Project overview

- **Type:** Monolith, single-part Unity game
- **Engine:** Unity 6000.2.6f2
- **Primary language:** C#
- **Architecture:** Server-authoritative NGO, **layered** — pure Domain POCO core → thin NGO/Mono adapters → `CompositionRoot` DI seam. Gateway-RPC simulated-client layer for solo debug.
- **Genre:** Asymmetric multiplayer social deduction (Werewolf family)

## Quick reference

- **Tech stack:** Unity + NGO 2.6 + FMOD + UniTask + DOTween + Facepunch (Steam) transport
- **Entry scene:** `Assets/Scenes/BootScene.unity`
- **Entry code:** `Assets/Scripts/GameLogic/GameManager.cs`
- **DI seam:** `Assets/Scripts/GameLogic/CompositionRoot.cs` — the one sanctioned static; `For(nm)` resolves the service graph
- **Domain core:** `Assets/Scripts/Domain/` (`CorruptionDuPortail.Domain.asmdef`) — 19 pure POCO types, no UnityEngine
- **Main branch:** `Dev` — the refactor track (Epics 1–12) is merged here (PR #53, `9daace2`)
- **Critical patterns:** wrap RPCs with `GetSafeRpcTarget(clientId)`; use `IsLocalOrSimulated(clientId)` not `IsLocalClient`; depend on injected slices (`IGameLoop`/`ICharacterQuery`/…), never `instance`/`For` locators

## Generated documentation

- [Project Overview](./project-overview.md)
- [Architecture](./architecture.md)
- [Source Tree Analysis](./source-tree-analysis.md)
- [Development Guide](./development-guide.md)
- [State Management](./state-management.md)
- [Asset Inventory](./asset-inventory.md)

### Refactor architecture (the post-2026-06 shape — read before touching DI / managers)

- [Despaghettification](./refactor-architecture-despaghetti.md) — three-lane injection, the 24→1 static census, CompositionRoot, the guards, whole-track DoD (story 12.3 verified).
- [POCO / testability](./refactor-architecture-poco.md) — Humble-Object + Domain asmdef extraction (Waves 1–4).
- [De-singletoning](./refactor-architecture-desingleton.md) — per-NetworkManager registries (Epic 5 enabler).
- [Deferred work](./implementation-artifacts/deferred-work.md) — recorded follow-ups / opt-outs.

## Pre-existing documentation

- `CLAUDE.md` (repo root) — primary AI-agent contract: architecture, critical patterns, commit conventions, gotchas. Points to this `_bmad-output/` set as the doc source of truth.

## Planning artifacts

- [GDD — Corruption Du Portail](./planning-artifacts/gdds/gdd-Corruption%20Du%20Portail-2026-05-29/gdd.md) — design document (descriptive capture; design-owned sections may be incomplete). Companion: `decision-log.md`.
- [Epics (despaghettification 6–12)](./planning-artifacts/epics.md) + `implementation-artifacts/sprint-status.yaml` — execution source of truth for the refactor track.

## Getting started (for AI agents)

1. **Always read `CLAUDE.md` first** — it carries the load-bearing patterns (Gateway RPC, server authority, FMOD, UniTask).
2. Open this index for system-level orientation; read [architecture.md](./architecture.md) for the layered/DI shape.
3. Before touching managers, DI, or adding a singleton: read [refactor-architecture-despaghetti.md](./refactor-architecture-despaghetti.md) — the three guards will fail a re-introduced locator.
4. New decision logic → a Domain POCO with EditMode tests; keep the adapter thin.
5. Use Unity MCP tools (`mcp__UnityMCP__*`) for build / test / console rather than CLI Unity.

## Workflow routing (BMad)

| Goal | Skill |
|---|---|
| Bug fix (subtle root cause) | `/gds-investigate` → `/gds-quick-dev` |
| Small/medium feature | `/gds-quick-dev` |
| Large feature, multi-story | `/gds-create-epics-and-stories` → `/gds-sprint-planning` → `/gds-create-story` → `/gds-dev-story` → `/gds-code-review` |
| Test coverage strategy | `/bmad-testarch-test-design` → `/bmad-testarch-automate` |
| Course correction mid-sprint | `/gds-correct-course` |
| Refresh these docs | `/gds-document-project` (then `/gds-generate-project-context`) |

## Out-of-scope (intentionally not generated)

The project's `documentation-requirements.csv` profile (`project_type_id = game`) does **not** require:
- API contracts (no HTTP service)
- Data models (no relational DB / ORM — only Unity SO + in-memory state + Domain value-objects, covered in [State Management](./state-management.md))
- Component inventory (UI components are not the dominant architectural element here; see [Source Tree](./source-tree-analysis.md))
- Deployment guide (game-ci handles builds via `Build.yml`)
- Hardware docs

## Verification recap

- **2026-06-13 full rescan:** `git diff df2c15d..HEAD` = **306 files, +14019/-877, 18 commits** — the behaviour-preserving despaghettification track (Epics 1–12) merged via PR #53, plus the character-bar sorting/grouping feature and tooltip/UI work. Tech stack, engine, transport unchanged. Core docs regenerated to capture the Domain POCO layer, `CompositionRoot`, and the interface seams; counts refreshed (670 `.cs` total / ~272 runtime / 19 Domain).
- **Test baseline at scan (story 12.3):** EditMode 205/205, PlayMode 148/148 green (incl. `MultiClientGameFixture`); the three architecture guards green.
- **Outstanding risks / follow-ups:** legacy/backup scenes still present (`(old)MenuScene.unity`, `GameScene_backup.unity`) — consider archiving. `_bmad-output/implementation-artifacts/deferred-work.md` tracks recorded opt-outs.
- **Recommended next checks before relying on these docs:** open Unity, run `mcp__UnityMCP__run_tests` to confirm the 205/148 baseline locally; refresh `project-context.md` with `/gds-generate-project-context` (it predates the refactor too).
