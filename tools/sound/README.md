# Sound maker

Free sound effects found on the internet, shaped and leveled to sit with the project's sounds, credited. Workflow:
the `sound-maker` skill. Tool: `sound.py` (`python sound.py -h`). Needs `ffmpeg` / `ffprobe` and Python 3 (`sheet`
needs Pillow).

## Files

| Path | What | Git |
|---|---|---|
| `sound.json` | project name, credits file, secret command for a Freesound API key | yes |
| `loudness.json` | categories, the project's measured loudness, the target of each category | yes |
| `engine.py` (optional) | how this project plays a sound and takes a new one (`engine.example.py`) | yes |
| `work/<subject>/` | downloads (`raw/`), `candidates.json`, `sources.json`, drafts | ignored |
| `recipes/<subject>.json` | how each finished sound was made, + `<subject>.sources.json` | yes |
| credits file (`sound.json` → `credits`) and its `.md` page | every outside sound, its license, where it is used | yes |

## Licenses

Allowed: **CC0** and **CC BY** (credit the author), or a license that allows the project's use without credit
(Kenney CC0, Sonniss GDC bundles, Pixabay Content License). Never **NonCommercial** for anything that may be
sold, never Sampling+, never a sound whose license is unclear. `fetch` refuses NC and Sampling+ by itself; for
`add`, check the license page.

## Recipe

```json
{
 "out": "Assets/Audio/SoundMaker/door",
 "sounds": [
  {"name": "door_open", "category": "ui", "event": "<name of the sound in the engine>", "mono": true,
   "layers": [
    {"src": "../work/door/raw/fs123456.ogg", "start": 0.1, "end": 0.9, "fade_out": 0.2, "highpass": 80},
    {"src": "../work/door/raw/fs654321.ogg", "pitch": -5, "gain": -6, "delay": 0.05, "lowpass": 4000,
     "eq": [[300, -3, 1.2]]}
   ]}
 ]
}
```

Per layer: `start` / `end` (s, cut), `reverse`, `pitch` (semitones, speed follows), `highpass` / `lowpass` (Hz),
`eq` (`[freq, gain dB, Q]`…), `fade_in` / `fade_out` (s), `gain` (dB), `delay` (s). Layers are summed.
Per sound: `category` (target from `loudness.json`), `target_max_m` to override it (a level picked by ear; `check`
honours it), `event` (its name in the engine, for `check` and the credits), `mono`, `reverb` (`{"delays":
"40|73|117", "decays": "0.35|0.25|0.15"}`, a small room; an engine's reverb send is better), `trim_lead: false` to
keep leading silence. Every sound is normalized: loudest 400 ms (`max_m`, LUFS) = its target, true peak under
-1 dBTP.

## Loudness

`loudness.json` holds what the project already sounds like (`sound.py reference`: with `engine.py`, every sound
rendered as played; otherwise `--files` on the project's audio files) and a target per category. Set the targets
by hand from the reference (median per category is the default) and say why in `targets_why`. Placeholders go in
`exclude`. A short click is measured padded with silence: the 400 ms window needs it.

## This project: FMOD (`engine.py`)

`engine.py` makes `sound.py` speak FMOD: `reference` and `check` render events from the built banks with the game's
own FMOD runtime (`tools/fmod/banks.py`), and `sound.py fmod <spec>` puts finished sounds into events. Finished
sounds go to `FMODProject/Corrupted/Assets/SoundMaker/<subject>/`, credits to `FMODProject/credits.json` →
`CREDITS.md`, specs next to their recipe as `recipes/<subject>.engine.json`. Measured here: clicks ≈ -29, table
objects -24, game moments ≈ -19.5, powers ≈ -14, ambience ≈ -23, music ≈ -22. Code may scale a sound further
(`GameAudioManager.PlayOneShot(event, volume)`): the candle ignites at 0.8.

### Spec (`sound.py fmod`)

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
