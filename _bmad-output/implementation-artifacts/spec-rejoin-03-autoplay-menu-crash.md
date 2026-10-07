---
title: 'Rejoin 03 — the menu button and a real crash + relaunch, proven by autoplay'
type: 'feature'
created: '2026-10-06'
status: 'done'
baseline_commit: 'b4bc9bf0'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-rejoin-02-reclaim-seat.md'
  - '{project-root}/tools/autoplay/REFERENCE.md'
---

## Intent

**Problem:** rejoin 02 was proven only for a drop where the autoplay reconnected directly. Two real paths were untested:
the main menu's "Rejoindre la partie en cours" button (shown only for Relay / Steam sessions), and a crash followed by a
relaunch of the game (a fresh process that must find its session on the PC).

**Approach (Poyo 2026-10-06: "fais en sorte que tout ça soit testable en autoplay"):**
- The menu also rejoins a "direct" session (address:port: LAN / dev builds, the autoplay).
- Autoplay lever `rejoin-via menu`: the rejoin goes through the real button, clicked with the virtual pointer.
- Lever `crash-at <phase>`: the client game process is killed (journal `crash` first). The launcher
  (`launch-net -RelaunchArgs`, scenario `client1Relaunch`) starts the game again with `-autoplay-relaunched`: it keeps
  the session saved on the PC (per-seat key), shows the button, clicks it, takes its seat back.
- Lever `video` (Poyo, same day): film a run, `video.mp4` per process (`make_videos.py`, ffmpeg).

## Found while proving it

- **A killed client made the host deaf, and every other player dropped** (3 runs, also without any relaunch).
  Cause, proven by `UdpDeadPeerTests` (raw Unity Transport, no game code, with a control case): on Windows each ICMP "port
  unreachable" answered by the dead game's port fails one UDP receive request, and Unity Transport 6.5 never released
  that request's buffer; after a few seconds no receive is scheduled any more. Fix: Unity Transport embedded in
  `Packages/com.unity.transport` with a 2-line `[CdP patch]` (release the buffer). Not proven whether Relay traffic can
  hit it (the host only talks to the Relay server); the patch protects every UDP path anyway.
  - An earlier attempt froze the crashed process instead of killing it, to dodge this: reverted (Poyo: no test
    workarounds). `crash-at` kills the process.
- A fast relaunch can come back before the host noticed the crash (liveness ~15 s): the token now takes the seat over
  from the dead connection (dropped, seat reserved, then claimed). PlayMode `FastRelaunch_TokenTakesOverASeatStillHeldByTheDeadConnection`.
- A fresh game joining mid-game spawned characters before its role pool was loaded: those roles stayed empty for the
  whole game (Roles desync). `RoleRegistry.onRegistered` now rebuilds them. EditMode `RoleRegistryTests`.
- The menu read the session before the autoplay set its per-seat key: `MainMenu.RefreshRejoinButton`.
- **A rejoined player's personal knowledge was wiped** (Relay crash run, seen once in 4, then reproduced with logs): the
  host also pushed knowledge slices for his CONNECTION id (`AllViewers` read `ConnectedClientsIds`); a real client keeps
  one local store, so that near-empty slice replaced what his seat knew. `AllViewers` now maps connections to seats.
  Invisible whenever the seat had no personal knowledge (why most runs passed): the analyzer caught it.
- Race found while reading the logs: after a crash the liveness reserves the seat (~15 s) but the dead connection stays
  open until the transport timeout (~30 s); a player back in between had his "left" cleared, then the late disconnect of
  his old connection (same id as his seat) made him leave again. The claim now closes any old connection of the seat,
  and `HandlePlayerLeft` ignores a connection whose seat is played through another one.
- The menu's rejoin button overlapped Join / Quit (screenshot): moved to the top centre, label auto-sized.
- A Mage voted out while away came back without knowing he was the Mage (`SetMageCharacterRpc` was broadcast once, at
  his chaining): the portal step waited forever on him. `CompleteRejoin` re-sends it when the portal is active.
  (The autoplay driver also compared the Mage id with the connection id instead of the seat: fixed.)
- A player still loading his rejoin received the state transitions broadcast meanwhile (OnStart / OnEndStateClient)
  and ran them without seat nor cards (NRE in the game-ending animation / vote recap). They are skipped while his start
  is deferred; the catch-up starts the state current then.
- A relaunched player's power uses were higher after the rejoin: his own awakening regenerates them
  (`Role.AwakenRole`), journaled (`awake`) before the late capture. `analyze_rejoin` now compares powers on the last
  capture taken before that awakening.
- Possibly the same Unity Transport leak: the rare host-side close of a rejoin connection seen in rejoin 02 (2 of 8
  runs). Not proven.

## Deviations from the real case (stated, not hidden)

- Direct (non-relay) runs lift the UGS login screen (`login.skip`): they never sign in. The relay runs (`relay`, Poyo
  2026-10-06) sign in through the real login screen, host and join through the real menus and rejoin through Relay.
- Text fields: the virtual keyboard's text never reaches TMP fields (they read IMGUI events); the same key events go
  through `TMP_InputField.ProcessEvent` (journaled `mode=events`), the field's own handling.
- Steam is not exercised.

## Verification

- PlayMode `UdpDeadPeerTests` (2), `PlayerRejoinTests` (2); EditMode `RoleRegistryTests`.
- Autoplay `client-crash-relaunch` (real kill), `client-rejoin` (menu button), `relay-game`, `relay-crash-relaunch`,
  plus the leave / sync scenarios.
- Not forced: which seat holds personal knowledge (the role draw); `analyze_rejoin` checks it whenever it exists.
