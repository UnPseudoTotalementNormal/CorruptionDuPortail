# Project Documentation Index

Entry point for AI-assisted work on **Corruption du Portail**. Read `CLAUDE.md` (repo root) first, then [project-context.md](./project-context.md).

## Living docs

| Doc | Read when |
|---|---|
| [project-context.md](./project-context.md) | Before writing any game code. Silent-breakage rules, hand-maintained |
| [architecture.md](./architecture.md) | System-level orientation (layers, managers, flows) |
| [state-management.md](./state-management.md) | Touching game state, replication, the game loop |
| [refactor-architecture-despaghetti.md](./refactor-architecture-despaghetti.md) | Before touching DI, managers, or adding a singleton (three lanes, CompositionRoot, CI guards) |
| [refactor-architecture-poco.md](./refactor-architecture-poco.md) | Extracting decision logic into the Domain asmdef |
| [refactor-architecture-desingleton.md](./refactor-architecture-desingleton.md) | Per-NetworkManager registries, the 2-NM test fixture |
| [spec-powers-poco-v2-architecture.md](./implementation-artifacts/spec-powers-poco-v2-architecture.md) | Adding or changing a power (read "shipped reality" first) |
| [arch-liveness-heartbeat.md](./implementation-artifacts/arch-liveness-heartbeat.md) | Disconnect / liveness / leave pipeline |
| [test-scenarios-catalog.md](./implementation-artifacts/test-scenarios-catalog.md) | Writing network or power tests (~300 scenarios) |
| [deferred-work.md](./implementation-artifacts/deferred-work.md) | Recorded follow-ups from reviews |
| [GDD](./planning-artifacts/gdds/gdd-Corruption%20Du%20Portail-2026-05-29/gdd.md) | Game rules (descriptive capture; design belongs to the game designer, never redesign) |

## Open work (not yet implemented)

- [spec-lobby-role-attribution-uitk.md](./implementation-artifacts/spec-lobby-role-attribution-uitk.md): approved
- [spec-join-started-game-gate.md](./implementation-artifacts/spec-join-started-game-gate.md): ready-for-dev
- [modular-information-system-rewrite.md](./implementation-artifacts/modular-information-system-rewrite.md), [character-info-badge-overlay-system.md](./implementation-artifacts/character-info-badge-overlay-system.md): old backlog (June), check before starting
- [datamosh-shader-prompt.md](./implementation-artifacts/datamosh-shader-prompt.md): shader prompt, status unknown
- [epics-player-embodiment.md](./planning-artifacts/epics-player-embodiment.md): stories 13.5/13.6 (proximity voice) held on Steam

The team's task list is the Discord forum (see `CLAUDE.md`), not these files.

## Where things go

- **Investigations** (`gds-investigate`): `implementation-artifacts/investigations/`
- **Specs** (`gds-quick-dev`): `implementation-artifacts/spec-*.md`. Move to `archive/specs/` once shipped.
- **Archive** (`archive/`): completed refactor stories + sprint file, shipped specs, old generated docs (previous 300-rule project-context, dev guide, overview, source tree, asset inventory). Kept for reference only; may be stale.

## Skills kept (2026-10-04 cleanup)

`gds-quick-dev` (feature/fix with a spec) · `gds-investigate` (forensic bug case) · `gds-code-review` (+ `bmad-review-adversarial-general`, `bmad-review-edge-case-hunter`) · `canvas-to-uitk` / `uitk-to-canvas`. Everything else (personas, sprint/story ceremonies, PRD/market research, doc generators) was removed. It can be reinstalled from `_bmad/` if ever needed.
