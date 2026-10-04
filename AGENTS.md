# AGENTS.md

Guidance for Codex and other non-Claude agents working in this repository.

**`CLAUDE.md` is the single source of truth. Read it in full before acting.** Its rules apply to every agent: commit conventions, critical patterns, Unity CLI tooling, Discord board. This file only repeats the parts that must never be missed.

- Before writing game code, read `_bmad-output/project-context.md` (silent-breakage rules). Docs index: `_bmad-output/index.md`.
- Every RPC target goes through `GetSafeRpcTarget(clientId)`; use `IsLocalOrSimulated(clientId)`, never `IsLocalClient`.
- Server-authoritative state; async = UniTask, never `Task`; audio = FMOD via `GameAudioManager`, never `AudioSource`.
- Commits: English, conventional subject, always a body (+ `UX:` line for player-facing changes), **no AI attribution or co-author trailer**.
