---
name: sound-maker
description: Give the game a new or better sound effect — search free sound libraries on the internet (Freesound first, then Kenney, Sonniss, OpenGameArt, Pixabay), keep only licenses a sold game may use, shape the takes (cut, layer, pitch, filter), level them to the game's own loudness per category so nothing jumps out, let Poyo pick by ear on a listening page, then put them in FMOD (event, random takes, pitch/volume variation, bus, bank), build, check, and record the credits. Use when the user asks for a sound ("trouve un son pour…", "fais-moi un son de…", "remplace le son provisoire", "il manque un son quand…", "sound effect", "SFX", "bruitage", "ce son est trop fort / pas harmonisé", "harmonise les volumes"), or when a feature being coded needs a sound that does not exist yet.
metadata:
  template: sound-maker
  template-version: 1.0.1
---

# Sound maker (Corruption du Portail)

A sound reaches the game as: free source → shaped and leveled take → chosen by Poyo **by ear** → put into the
engine → credited. Claude cannot hear: it measures (loudness, spectrum, waveform) and proposes; Poyo decides on the
listening page. Tool: `tools/sound/sound.py` (its README: recipe format, loudness, licenses). Made from the
`sound-maker` template of the skill marketplace (`~/Projects/skill-marketplace`, `templates/sound-maker/`).

## 1. Understand the moment

- Which sound, when it plays, how long what we see lasts, how often it repeats, what code plays it and at which
  volume: read it in the code, not in a memory of it.
<!-- local:moment -->
- Sounds are FMOD events played through `AudioSystem/GameAudioManager` (`PlayOneShot(event, volume)`): the
  component holds an `EventReference`; the volume it passes scales the event (the candle ignites at 0.8).
- What plays now: `python tools/fmod/banks.py recover <event> <out.wav>` renders it exactly as the game does.
<!-- /local -->
- Its **category** in `tools/sound/loudness.json`: it sets the target loudness. A sound heard often (a click, a
  tick) needs several takes and random variation.
- What plays today: render it to a file, it is the « Actuel » variant.

## 2. Search

- `python tools/sound/sound.py search "<english words>" --max-dur S --work tools/sound/work/<subject>` (Freesound;
  API when a key is found — `Freesound` in the Bitwarden « Clawd » folder, or `FREESOUND_API_KEY` — else its public
  pages). Several wordings, 10 to 15 candidates per sound.
- Sort by what the sound **is** (title, tags), then rating and downloads, then a duration close to the moment.
- Freesound thin or blocked: other libraries through WebFetch, or Poyo's Zen through `clawd-use` `browser_use`
  when a site needs a real browser. Kenney (CC0 packs), Sonniss (royalty-free GDC bundles), OpenGameArt (license
  file by file). A file from elsewhere: `sound.py add <W> <file> --title --author --license --url` (never without
  its license page).
- **Licenses**: CC0, CC BY (credited), or a license allowing the project's use without credit. Never Sampling+,
  never an unclear license.
<!-- local:licences -->
- The game is sold on Steam: **never NonCommercial**. `fetch` refuses NC and Sampling+ by itself.
<!-- /local -->

## 3. Fetch, look, shape

- `sound.py fetch <W> <id>...`: HQ preview + author + license into `<W>/raw/` and `<W>/sources.json`.
- `sound.py sheet <W>/raw/*.ogg --out <W>/sheet.png`, and **look at it** (Read): where the hit is, the useful
  part, mic rumble (yellow band at the bottom of the spectrum), breath, noise floor, clipping. That is how cuts are
  placed without hearing.
- A recipe `<W>/drafts.json`: 3 to 5 **really different** takes per sound (another source, another idea: a hit vs
  the material alone vs a pitched-down layer), not shades. Cut to the moment, high-pass the rumble, fade the tail,
  layer when one source lacks weight.
- `sound.py make <W>/drafts.json`, then `sheet` on the result: every take is on its category's target (loudest
  400 ms, true peak under -1 dBTP). No rumble left, no noise before the hit.

## 4. Pick by ear (maquettes site)

A listening page on the `maquettes` skill's site (`tools/maquettes/<subject>-sons/v<N>.html`, port 5198), same
rules (read `retours.txt`, never re-ask the same choices). Reference page: `tools/maquettes/bougie-sons/v1.html`.
- Takes as `.ogg` in `audio/` (`ffmpeg -c:a libvorbis -q:a 6`), a waveform PNG each, the source as a link with its
  author and license.
- One section per sound, an « Actuel » variant (X0) first, « (par défaut) » on Claude's pick.
- Played through Web Audio **at the volume the game uses**, with the game's music underneath (a toggle) and a
  sound of the game to compare: loudness is judged in context, not alone.
- A level section (quieter / as is / louder): the voted level becomes the recipe's `target_max_m`.
- Captures of the page in `captures/v<N>/`, link bare on its line in the message.

## 5. Integrate

- The chosen takes, redone in `tools/sound/recipes/<subject>.json` (`"out":
  "FMODProject/Corrupted/Assets/SoundMaker/<subject>"`, `"event"` = the sound's name in the engine), then
  `sound.py make`.
- `sound.py credit tools/sound/recipes/<subject>.json`: every source used enters `FMODProject/credits.json` (what
  it became, how it was changed), the credits page is rewritten. **No sound without its credit line.**
<!-- local:moteur -->
- `tools/sound/engine.py` = FMOD. A spec next to the recipe, `tools/sound/recipes/<subject>.engine.json`
  (format: `tools/sound/README.md`): existing event = replaced in place (GUID, effects, code references kept); new
  event = created in its folder, bus `bus:/Sfx` (or Music), bank. Several takes = random multi; `pitch_random` /
  `volume_random` for sounds heard often.
- `sound.py fmod <spec>` runs FMOD Studio's command line (FMOD Studio must be closed), builds **every** bank and
  checks banks against the project (`tools/fmod/README.md`).
- `sound.py check <event>...`: renders the built event with the game's FMOD runtime and compares it with its target
  (±1.5 LU).
- A new event the code must play: wire it like the existing ones (`EventReference` on the component), with its
  playtest log (`GameLog`), and say so.
<!-- /local -->

## 6. Keep

<!-- local:garder -->
One commit with the recipe and its spec, the finished `.wav`, `FMODProject/` (Metadata + Assets),
`Assets/FMODBanks/`, `credits.json` + `CREDITS.md`, the listening page and its captures. The `fmod_guard` hook and
the CI job `FMOD project in sync` refuse banks that disagree with the project. Body: `UX:` line (what the player
now hears).
<!-- /local -->

## Traps

- Freesound previews are ~192 kb/s Ogg: fine for a short effect. For a long or very exposed sound, the original
  file needs a Freesound login: ask Poyo.
- A take shorter than 400 ms is measured padded with silence (the momentary window): its loudness is right.
<!-- local:pieges -->
- Never build one bank alone, never commit banks without the project (`tools/fmod/README.md`).
- FMOD Studio open on the project (Poyo, Wouh): the command line cannot open it; ask before closing it.
- `banks.py render` is bit-exact repeatable; FMOD's random modulators repeat too in one process: render several
  instances to hear the variation.
<!-- /local -->
