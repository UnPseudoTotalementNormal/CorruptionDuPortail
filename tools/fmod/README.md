# FMOD: project and banks

The game plays the **banks** (`Assets/FMODBanks/Desktop/*.bank`); **`FMODProject/Corrupted`** is where every sound is
made. The two must always be committed together: banks built from a project that is not in git cannot be edited
again, and the next build from the repo deletes what they held (it happened: menu music and 8 interface sounds,
2025-10 → 2026-02, recovered 2026-10-28 by `resync-2026-10-28.js`).

## Changing a sound

In FMOD Studio (2.03.x, open `FMODProject/Corrupted/Corrupted.fspro`) or by script:

1. Edit, save.
2. Build **all** banks (Studio: File → Build, or `tools/fmod/fmod.sh build`). Never build one bank alone: its event
   paths would be missing from `Master.strings.bank` (Unity then fails to refresh the banks in CI).
3. Commit `FMODProject/` **and** `Assets/FMODBanks/` in the same commit, with the new audio files of
   `FMODProject/Corrupted/Assets/`.

Checked automatically: `tools/fmod/fmod.sh check` locally, the `fmod_guard` hook on Claude's commits, and the CI job
`FMOD project in sync` on every push / PR to Dev that touches FMOD. They compare the events of the banks and of the
project (GUID and path) and fail on any difference.

`.cache/` (build cache) and `.user/` (window layout) are local, not in git.

## Tools

| Tool | What |
|---|---|
| `fmod.sh script <file.js>` | run a Studio script on the project (FMOD Studio closed) |
| `fmod.sh build` / `check` | build every bank, then compare / compare only |
| `banks.py list` | every event, bus, snapshot of the built banks with its GUID |
| `banks.py diff` | banks vs project, event by event |
| `banks.py render <event> <out.wav>` | play an event offline into a WAV, exactly as the game would (bit-exact repeatable) |
| `banks.py compare <old dir> <new dir>` | render every event from two sets of banks, compare loudness and waveform |
| `banks.py recover <event> <out.wav>` | the audio an event plays, to rebuild an event lost from the project |
| `banks.py setguid <event> <guid>` | give a project event back its original GUID (the game references events by GUID) |

`banks.py` uses the game's own FMOD runtime (`Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudio.dll`), so it
runs without FMOD Studio and in CI. `render`, `compare` and `recover` also need `ffmpeg`.
