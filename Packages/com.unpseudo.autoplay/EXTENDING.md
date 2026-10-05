# Extending autoplay

For whoever (human or agent) needs autoplay to cover something it does not cover yet. Default stance: **when a goal
is not covered, extend the framework** (a lever, an event, a check), then prove the goal. Report "not covered → human
playtest" only when the extension is out of scope for the task, and then say what extension would cover it.

Catalogue of what exists: the game project's reference (in Corruption du Portail: `tools/autoplay/REFERENCE.md`, which
also holds its game-specific rules and backlog recipes). Read it before adding anything: the lever you want may
already exist under another name.

## Where does it go?

| Need | Put it in |
|---|---|
| Works for any game (run loop, journal, capture, recorder, command line, a scenario check, a launcher) | the package: `Runtime/` (C#) or `Tools~/` (Python / shell) |
| Knows the game (roles, powers, chat, lobby, netcode objects) | the game adapter (`IAutoplayGame` implementation = levers and process roles; its bot driver = actions and game events) |
| Needs to observe or steer prod code the adapter cannot reach | a **small, dev-only seam** in prod (an interface the adapter plugs into, or a read-only accessor), never autoplay logic inside prod code |
| Game-specific analysis | the game project's tools folder |

The package must never reference the game (no game types, no game names). If a package feature needs game data, add
an optional interface the adapter implements (pattern: `IAutoplayAnimationSource`).

## Building blocks

- **Lever** = `-autoplay-<key>` argument. No parsing to write: unknown keys land in `config.options`. Read it with
  `config.Flag("key")`, `Option("key", default)`, `OptionInt(...)` where the adapter sets up, and pass it to the driver
  through `AutoplayOptions`. Name it after the situation it forces (`vote-focus`, `quit-at`), kebab-case.
- **Event** = `journal.Record("area.verb", detail)`. Events are the contract scenarios assert on: stable kind, detail
  with `key=value` pairs, numbers formatted with `CultureInfo.InvariantCulture` (a French locale writes `0,5`).
  Record it on the process where the thing is *observed* (a client receiving something records it on the client).
- **Capture** = `capture.Request(label, delay)` / `RequestBurst(label, delays)` at the moment worth seeing;
  `capture.AddProbe(name, read)` or a new field of the exported state for a value at that moment.
- **Recording** = `-autoplay-record "kindRegex:seconds"` + a track from `IAutoplayAnimationSource.TracksFor`.
- **Check** = a new `"type"` in `Tools~/run_scenario.py` (`check(...)` dispatch + docstring). Prefer composing existing
  checks (`event` with `detail` regex and `min` / `max`) before adding a type.
- **Scenario** = a JSON file in the game's scenarios folder: goal sentence, levers, expectations. It is the deliverable:
  a passing scenario stays as a regression test.
- **Real input** = `AutoplayVirtualInput`: a virtual Input System mouse + keyboard (`MoveTo`, `Click`, `Look`,
  `Scroll`, `PressKey`, `KeyDown` / `KeyUp`) for "can a player actually do it" checks. Install it only behind a lever;
  verify every click by its game effect and journal what the raycast hit when it fails.

## Rules that bite

1. **Force the situation.** A lever must *guarantee* the case under test (forced roles, holder kind, deterministic
   target, scripted leave). Then assert in the journal that it happened (`min: 1` on the event). A run where the
   situation did not occur is "not covered", never a pass.
2. **Prove both sides.** Add the negative check that would fail if the feature were broken the other way (the message
   reaches members *and* `max: 0` for non-members; exactly one leaver, not "at least one").
3. **Client vs server.** Each process has its own copy of the game state; a client bot must act on what the server
   sends the client, never on a field only the server writes. Anything touching a client path is validated with
   `play-net` (real client processes), not only with host + simulated bots, which run everything on the server and hide
   client bugs.
4. **Dev-only.** Every file and seam is under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; a lever must do nothing unless
   its argument is present (release players and normal dev runs behave exactly as before).
5. **No foreground, no focus, no cursor lock.** Players run through the launchers; never minimize one (black captures).
   Only exception: with `-autoplay-real-input` the window guard lets the game lock the cursor while the player is
   unfocused (an unfocused lock never captures the OS cursor, measured 2026-10-05); it is freed as soon as the window
   gets focus.
6. **Measure thresholds, never invent them.** A numeric expectation comes from a reference run, cited in the goal.
7. **Never drive the user's real mouse or keyboard** (SendInput, SetCursorPos…), not even to measure: real input works
   on virtual devices only, the real ones are disabled while it runs.
8. **Input System traps:** `InputSystem.onDeviceChange` fires inside `AddDevice`, before the returned device is
   stored (guard your own devices); events of a disabled device are dropped before `InputSystem.onEvent`; a
   `RectTransform`'s centre may lie on no raycastable graphic (aim at what the raycast reaches); code reading legacy
   `UnityEngine.Input` or IMGUI events (`TMP_InputField` typing) is out of reach of virtual devices.

## Definition of done

1. `unityctl.sh compile` clean, `editmode` green (adapter code is compiled in the game assemblies).
2. `build`, then the scenario runs and **passes** (`run_scenario.py`), on `play-net` if a client is involved.
3. Show it can fail: break the condition once (wrong lever value, tighter `max`) and see the check fail.
4. Docs updated in the same commit: the game's reference (lever, events, check, scenario row), the usage skill if the
   workflow changed, this file if a generic rule was learned.
