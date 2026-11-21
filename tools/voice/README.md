# Voice bench (T22): look inside the voice, stage by stage

Real Steam Voice, no second PC, no human talking. A French TTS voice is played into the virtual
"Steam Streaming Microphone", the real Steam client records it, compresses and decompresses it exactly
like the game (`ISteamUser` via `steam_api64.dll`, app 480), then the game's own `VoiceLeveler` runs on it
in the headless editor. Each stage is a WAV; the scripts measure it.

Steps (Git Bash; ~15 s with the user's default microphone switched, so say it and switch it back):

1. `pwsh -File tools/voice/mic.ps1 get` (note the current default capture id), then
   `mic.ps1 set "{0.0.1.00000000}.{9cb683b8-be15-4c27-bd1c-49b2694bfbec}"` (Steam Streaming Microphone on Poyo's PC).
2. In parallel: `pwsh -File tools/voice/speak.ps1 -Seconds 9 -Volume 3` (3 = a normal voice, 8 = a hot microphone;
   100 saturates the virtual mic itself), `ffmpeg -f dshow -i audio="Microphone (Steam Streaming Microphone)" -t 10
   -ac 1 -ar 48000 src.wav` (the source), `python tools/voice/capture.py 10` (→ `%TEMP%/voice-bench/steam_decoded.wav`).
3. `mic.ps1 set <the id noted in 1>`.
4. Leveler: `unity command eval` reading the WAV, `new VoiceLeveler(48000).Process` in 60 ms packets, writing it back.
5. `python tools/voice/stats.py <wav…>` (peak, speech level, crest, clipped %, flat tops) and
   `python tools/voice/dist.py <name>` (run in the folder: wave-shape distortion per pitch period of `steam_<name>.wav`
   vs `steam_<name>_old.wav` / `_new.wav`, two leveler versions, same timeline).

Reading: Opus hides a clipped source (32 % clipped samples in, 4.7 % out): judge the source by its crest
factor (peak minus speech level), ~15-19 dB for a clean voice, ~3-5 dB for a saturated one. The second
Steam client in the Sandboxie box records nothing (`voice.mic … packets=0`): use the main Steam here.

First run (2026-11-21): Steam's codec adds nothing (levels and crest kept); the leveler brings a hot mic down
9 dB, output peak -10 dBFS, 0 % clipped, the limiter never reached; spectrograms show no added distortion.
