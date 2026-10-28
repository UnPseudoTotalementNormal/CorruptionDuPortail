# Sound maker

Free sound effects found on the internet, shaped and leveled to sit with the game's sounds, put in FMOD, credited.
Workflow: the `sound-maker` skill (`.claude/skills/sound-maker/SKILL.md`). Tool: `sound.py` (`python
tools/sound/sound.py -h`). Needs `ffmpeg` and Python 3; FMOD Studio 2.03.14 for the `fmod` command.

## Files

| Path | What | Git |
|---|---|---|
| `tools/sound/work/<subject>/` | downloads (`raw/`), `candidates.json`, `sources.json`, drafts | ignored |
| `tools/sound/recipes/<subject>.json` | how each finished sound was made (layers, cuts, filters) + its sources | yes |
| `tools/sound/loudness.json` | the game's loudness per category, and the target of each category | yes |
| `FMODProject/Corrupted/Assets/SoundMaker/<subject>/` | finished sounds, imported by FMOD | yes |
| `FMODProject/credits.json` → `FMODProject/CREDITS.md` | every outside sound, its license, where it is used | yes |

## Licenses

Allowed: **CC0** and **CC BY** (credit the author), or a license that says commercial use without credit (Kenney
CC0, Sonniss GDC bundles, Pixabay Content License). Never **NonCommercial** (the game is sold), never Sampling+,
never a sound whose license is unclear. `fetch` refuses NC and Sampling+ by itself; for `add`, check the page.

## Recipe

```json
{
 "out": "FMODProject/Corrupted/Assets/SoundMaker/vote-candle",
 "sounds": [
  {"name": "candle_ignite", "category": "table", "mono": true,
   "layers": [
    {"src": "../work/vote-candle/raw/fs423811.ogg", "start": 0, "end": 0.95, "fade_out": 0.35, "highpass": 80},
    {"src": "...", "pitch": -5, "gain": -6, "delay": 0.05, "lowpass": 4000, "eq": [[300, -3, 1.2]]}
   ]}
 ]
}
```

Per layer: `start` / `end` (s, cut), `reverse`, `pitch` (semitones, speed follows, like FMOD), `highpass` /
`lowpass` (Hz), `eq` (`[freq, gain dB, Q]`…), `fade_in` / `fade_out` (s), `gain` (dB), `delay` (s). Layers are summed.
Per sound: `category` (target from `loudness.json`), `target_max_m` to override it, `mono`, `reverb`
(`{"delays": "40|73|117", "decays": "0.35|0.25|0.15"}`, a small room; prefer FMOD's Reverb send for the room),
`trim_lead: false` to keep leading silence. Every sound is then normalized: loudest 400 ms (`max_m`, LUFS) = its
category's target, true peak under -1 dBTP.

## Loudness

`loudness.json` holds what the game already sounds like, measured on the built banks (`sound.py reference`):
clicks ≈ -29, table objects -24, game moments ≈ -19.5, powers ≈ -14, ambience ≈ -23, music ≈ -22 (max momentary,
LUFS). A new sound lands on its category's target; `sound.py check <event>` renders the built event and checks it.
Code may scale a sound further (`GameAudioManager.PlayOneShot(event, volume)`): the candle ignites at 0.8.

## FMOD spec (`sound.py fmod`)

```json
{"bank": "VoteCandle",
 "events": [
  {"path": "event:/Interface/VoteCandleIgnite", "files": ["SoundMaker/vote-candle/candle_ignite.wav"],
   "bus": "bus:/Sfx", "volume": 0, "pitch_random": 0.6, "volume_random": 1.5}
 ]}
```

An existing event keeps its GUID, effects and references: only the instruments of its first track are replaced.
Several `files` = one random take per play (multi instrument). `pitch_random` (semitones) / `volume_random` (dB)
add FMOD random modulators, so a sound heard often never repeats exactly. Then every bank is built and checked
against the project (`tools/fmod/README.md`).
