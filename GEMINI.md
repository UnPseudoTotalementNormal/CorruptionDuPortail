# Project Instructions

## Essential Workflows

- **Follow CLAUDE.md:** Always prioritize the instructions found in `CLAUDE.md`.
  - **Commits:** English subject/body, atomic, include `UX:` line for player-facing changes. No AI attribution.
  - **RPCs:** Always wrap with `GetSafeRpcTarget(clientId)`. Use `IsLocalOrSimulated(clientId)` instead of `IsLocalClient`.
  - **Async:** Use `UniTask`, never `Task`.
  - **Audio:** Use FMOD (`GameAudioManager`), never `AudioSource`.
  - **Server Authority:** Mutate game state on server only.
