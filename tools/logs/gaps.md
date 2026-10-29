# Playtest log gaps

One line per gap found while investigating a real playtest report: a question the online logs could not answer.
Written by the `playtest-logs` skill (step 4). A kind of gap that comes back means the harness misses a rule or a
check there: fix that too (see CLAUDE.md, "When a preventable problem slips through").

| Date | Report | Unanswered question | Added |
|---|---|---|---|
| 2026-10-27 | Steam joins failed, "Lobby without a host SteamID" (playtest 2026-10-24, Wouh + Flox) | Which lobby keys the host wrote and which the client saw | `steam.lobby.created`, `steam.lobby.joined`, `steam.client.hostFromOwner` (PR #165) |
