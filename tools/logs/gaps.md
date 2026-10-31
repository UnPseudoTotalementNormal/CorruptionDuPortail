# Playtest log gaps

One line per gap found while investigating a real playtest report: a question the online logs could not answer.
Written by the `playtest-logs` skill (step 4). A kind of gap that comes back means the harness misses a rule or a
check there: fix that too (see CLAUDE.md, "When a preventable problem slips through").

| Date | Report | Unanswered question | Added |
|---|---|---|---|
| 2026-10-27 | Steam joins failed, "Lobby without a host SteamID" (playtest 2026-10-24, Wouh + Flox) | Which lobby keys the host wrote and which the client saw | `steam.lobby.created`, `steam.lobby.joined`, `steam.client.hostFromOwner` (PR #165) |
| 2026-10-30 | Cork portraits vanishing after a click / while hovered in pause (playtest 2026-10-29, Wouh, game 20261029-2120-77aa) | Which portrait was hovered and where its rest pose was on screen | `ui.bar.hover` (seat, rest viewport point); `ui.tooltip.kept.on.screen` for the robot counter tooltip off screen |
| 2026-10-30 | "We still cannot vote" (playtest 2026-10-29, Wouh, game 20261029-2120-77aa) | What sat under the seated reticle when a click reached nothing (`reticle.refused no-target`) | `reticle.refused` + `uiHits`, `uiTop`, `hit`, `layer`, `inMask` |
