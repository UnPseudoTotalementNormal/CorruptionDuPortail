# Playtest log gaps

One line per gap found while investigating a real playtest report: a question the online logs could not answer.
Written by the `playtest-logs` skill (step 4). A kind of gap that comes back means the harness misses a rule or a
check there: fix that too (see CLAUDE.md, "When a preventable problem slips through").

| Date | Report | Unanswered question | Added |
|---|---|---|---|
| 2026-10-27 | Steam joins failed, "Lobby without a host SteamID" (playtest 2026-10-24, Wouh + Flox) | Which lobby keys the host wrote and which the client saw | `steam.lobby.created`, `steam.lobby.joined`, `steam.client.hostFromOwner` (PR #165) |
| 2026-10-30 | Cork portraits vanishing after a click / while hovered in pause (playtest 2026-10-29, Wouh, game 20261029-2120-77aa) | Which portrait was hovered and where its rest pose was on screen | `ui.bar.hover` (seat, rest viewport point); `ui.tooltip.kept.on.screen` for the robot counter tooltip off screen |
| 2026-10-30 | "We still cannot vote" (playtest 2026-10-29, Wouh, game 20261029-2120-77aa) | What sat under the seated reticle when a click reached nothing (`reticle.refused no-target`) | `reticle.refused` + `uiHits`, `uiTop`, `hit`, `layer`, `inMask` |
| 2026-11-02 | No voice heard on Steam (playtest 2026-10-31, Hat Kid + Wouh, game 20261031-1729-2be8) | Whether every packet failed to decode or only the first; how big the sent packets were; whether a press sent anything at all | `voice.decode.failed` counts (`failed`, `ok`, `bytes`) every 100, `voice.decode.ok` (first per speaker), `voice.mic on=false` + `packets`, `maxBytes` |
| 2026-11-02 | Crash alert with an empty session (playtest 2026-11-01, Flamy, session 20261101-142046-4e839e) | When and why the launch died: our first line was still in the 1 s buffer, the native stack only in Unity's Player-prev.log | `session.start` flushed at once (+ `vsync`, `targetFps`); `session.previous.crashed unityLog` (Player-prev.log excerpt, also shown in the Discord alert) |
| 2026-11-02 | Players at ~480 ms ping in two games out of three, 60 ms in the third (playtest 2026-11-01, games 20261101-1449-7371 and 20261101-1529-f1cc) | Which Relay region the host's allocation landed in, and the one each client joined | `region` on `relay.host.ready` and `relay.join.done` |
| 2026-11-14 | By day an emote left the player "stuck in their own face" (Coucou, 2026-11-13, game 20261112-2357-1076) | Where the emote camera sat, what it pivoted on and what stopped it (`orbit-blocked` gave only a radius and a collider named "Collider") | `camera.emote.pose` (avatar, pivot, camera, eye, blocker path); `cat.light.owner` (own cat lit for the emote view) |
