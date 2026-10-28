---
name: sound-maker
description: Give the game a new or better sound effect — search free sound libraries on the internet (Freesound first, then Kenney, Sonniss, OpenGameArt, Pixabay), keep only licenses a sold game may use, shape the takes (cut, layer, pitch, filter), level them to the game's own loudness per category so nothing jumps out, let Poyo pick by ear on a listening page, then put them in FMOD (event, random takes, pitch/volume variation, bus, bank), build, check, and record the credits. Use when the user asks for a sound ("trouve un son pour…", "fais-moi un son de…", "remplace le son provisoire", "il manque un son quand…", "sound effect", "SFX", "bruitage", "ce son est trop fort / pas harmonisé", "harmonise les volumes"), or when a feature being coded needs a sound that does not exist yet.
---

# Sound maker (Corruption du Portail)

A sound reaches the game as: free source → shaped and leveled take → chosen by Poyo **by ear** → FMOD event →
built banks → credited. Claude cannot hear: it measures (loudness, spectrum, waveform) and proposes; Poyo decides
on the listening page. Tools: `tools/sound/sound.py` (README there: recipe and spec formats), `tools/fmod/`
(`README.md`: the FMOD project and the banks are always committed together).

## 1. Understand the moment

- Which event, when it plays, how long the visual lasts, how often it repeats, what code plays it and at which
  volume (`GameAudioManager.PlayOneShot(event, volume)`): read the code, not a memory of it.
- Its **category** in `tools/sound/loudness.json` (ui click, table object, announce, power, ambience, music): it
  sets the loudness target. A sound heard often (a click, a tick) needs several takes and random variation.
- What plays now: render it (`python tools/fmod/banks.py recover <event> <out.wav>`), it is the « Actuel » variant.

## 2. Search

- `python tools/sound/sound.py search "<english words>" --max-dur S --work tools/sound/work/<subject>` (Freesound;
  API with a `Freesound` key in the Bitwarden « Clawd » folder or `FREESOUND_API_KEY`, else its public pages).
  Several wordings (« match strike », « candle light match », « flame ignite »…), 10 to 15 candidates per sound.
- Sorting order: what the sound **is** (title, tags), rating and downloads, duration close to the moment.
- Freesound thin or blocked: other libraries through WebFetch, or Poyo's Zen through `clawd-use` `browser_use`
  when a site needs a real browser (Pixabay, Sonniss). Kenney (CC0 packs), Sonniss GDC bundles (royalty free),
  OpenGameArt (check each file's license). A file from there: `sound.py add <W> <file> --title --author --license
  --url` (no file without its license page).
- **Licenses**: CC0, CC BY (credited), or a license allowing commercial use without credit. Never NonCommercial,
  never Sampling+, never unclear: the game is sold on Steam. `fetch` refuses NC by itself.

## 3. Fetch, look, shape

- `sound.py fetch <W> <id>...`: HQ preview + author + license into `<W>/raw/`, `<W>/sources.json`.
- `sound.py analyze <W>/raw/*.ogg --spectro <W>/spectro`, then a contact sheet of waveform + spectrogram per
  candidate, and **look at it** (Read the PNG): where the hit is, the useful part, mic rumble (yellow band at the
  bottom), breath, noise floor, clipping. That is how cuts are placed without hearing.
- A recipe `<W>/drafts.json` (format: `tools/sound/README.md`): 3 to 5 **really different** takes per sound
  (another source, another idea: a strike vs the flame alone vs a pitched-down layer), not shades. Cut to the
  moment, high-pass the rumble, fade the tail, layer when one source lacks weight.
- `sound.py make <W>/drafts.json --spectro <W>/out_spec`: every take lands on its category's target (loudest
  400 ms, true peak under -1 dBTP). Look at the result sheet again: no rumble left, no noise before the hit.

## 4. Poyo picks by ear (maquettes site)

A listening page with the `maquettes` skill's site (`tools/maquettes/<subject>-sons/v<N>.html`, port 5198), same
rules (read `retours.txt`, never re-ask the same choices). Reference page: `tools/maquettes/bougie-sons/v1.html`.
- Takes as `.ogg` in `audio/` (`ffmpeg -c:a libvorbis -q:a 6`), a waveform PNG each, the source as a link with its
  author and license.
- One section per sound, an « Actuel » variant (X0) first, « (par défaut) » on Claude's pick.
- Played through Web Audio **at the volume the game uses** (the code's PlayOneShot volume), with the game's music
  underneath (a toggle) and a click of the game to compare: loudness is judged in context, not alone.
- A section for the level (quieter / as is / louder) when the category target is new or doubtful.
- Captures of the page in `captures/v<N>/`, link bare on its line in the message.

## 5. Into FMOD

- The chosen takes, redone in `tools/sound/recipes/<subject>.json` with `"out":
  "FMODProject/Corrupted/Assets/SoundMaker/<subject>"` and `"event"` per sound, then `sound.py make` on it.
- `sound.py credit tools/sound/recipes/<subject>.json`: records every source used in `FMODProject/credits.json`
  (what it became, how it was changed), keeps the recipe's sources next to it, rewrites `FMODProject/CREDITS.md`.
  **No sound in the game without its credit line.**
- A spec (`tools/sound/README.md`): existing event = replaced in place (GUID, effects, code references kept); new
  event = created in its folder, bus `bus:/Sfx` (or Music), bank. Several takes = random multi; `pitch_random` /
  `volume_random` for sounds heard often. `sound.py fmod <spec.json>` runs FMOD Studio's command line (FMOD Studio
  must be closed), builds **every** bank and checks banks against the project.
- `sound.py check <event>...`: renders the built event and compares it with its category's target (±1.5 LU).
- A new event the code must play: wire it like the existing ones (`EventReference` on the component), with its
  playtest log (`GameLog`), and say so.

## 6. Commit

One commit with the recipe, the finished `.wav`, `FMODProject/` (Metadata + Assets), `Assets/FMODBanks/`,
`credits.json` + `CREDITS.md`, the listening page and its captures. The `fmod_guard` hook and the CI job `FMOD
project in sync` refuse banks that disagree with the project. Body: `UX:` line (what the player now hears).

## Traps

- Never build one bank alone, never commit banks without the project: see `tools/fmod/README.md`.
- FMOD Studio open on the project (Poyo, Wouh): the command line cannot open it; ask before closing it.
- Freesound previews are ~192 kb/s Ogg: fine for short effects (FMOD re-encodes to Vorbis anyway). For a long or
  very exposed sound, the original file needs a Freesound login: ask Poyo.
- A take shorter than 400 ms is measured padded with silence (the momentary window); its loudness is right.
- `banks.py render` is bit-exact repeatable; FMOD's random modulators repeat too in one process: render several
  instances to hear the variation.
