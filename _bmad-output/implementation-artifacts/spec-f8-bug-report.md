---
title: 'F8 bug report: in-game window, capture, marked moment, Discord alert'
type: 'feature'
created: '2026-10-20'
status: 'in-review'
context: ['{project-root}/_bmad-output/project-context.md', '{project-root}/_bmad-output/implementation-artifacts/spec-online-playtest-logs.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A tester who hits a bug describes it later on Discord, from memory; the exact moment, his screen and his game's logs are not tied to his words.

**Approach:** In playtest builds, F8 (or « Signaler un bug » in the pause menu) captures the screen, opens a small window « Qu'est-ce qui s'est passé ? », and on Envoyer marks the moment in the online logs, uploads the capture and a report document; a Cloud Function posts it to a hidden Discord channel « rapports-de-bug » with the player, game, moment and the `logs.py` command to read it. A crashed session recovered at the next launch posts a crash alert the same way.

## Boundaries & Constraints

**Always:**
- Mockups chosen (tools/maquettes/signaler-un-bug v1, 2026-10-20): window **B2** (black pause-menu panel, white text, white text box, grey buttons, capture above the text), confirmation **C1** (« Envoyé, merci », ~2 s, then gone), access **K2** (F8 everywhere + « Signaler un bug » line in the pause menu).
- Exists only in builds with `CDP_PLAYTEST_LOGS` (window, key, menu line, upload code all compiled out otherwise); needs the logs runtime (same Firebase project, anonymous account, remote switch: switch off = window says the report could not be sent, nothing leaves).
- The capture is taken at the key press, before the window shows (the window is never in it).
- The game is not paused (network game); the window never blocks the game loop.
- Player text: French, no em dash; window texts = labels only (no explanation lines).
- Discord bot token only server-side (Secret Manager); clients never talk to Discord.
- Report text logged in full (`GameLog.Mark("bug.report", …)`), playtest decision.

**Ask First:** creating the Discord channel (show name + permissions to Poyo first); any change to the pause menu beyond adding the line.

**Never:** wax seal or diegetic decoration (B2 chosen); a report path in public builds; client-side Discord webhooks.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Report | F8, types text, Envoyer | capture + report doc + `bug.report` mark + immediate log flush; C1 shown; Discord message with capture within ~10 s | N/A |
| Empty text | Envoyer with no text | sent anyway (capture + moment are the value) | N/A |
| Cancel / F8 again / Échap | window open | window closes, nothing sent | N/A |
| Offline / switch off | Envoyer | window shows « Pas envoyé » for ~3 s | report kept locally, sent at next launch if switch on |
| Spam | F8 within 20 s of a report | window opens, Envoyer waits until 20 s passed | N/A |
| Crash recovery | previous session crashed | Discord « crash » alert (player, version, last phase) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Diagnostics/Logs/PlaytestLogRuntime.cs` -- owns auth/config/upload; gains a report queue, immediate flush, crash alert.
- `Assets/Scripts/Diagnostics/Logs/FirestoreLogClient.cs` -- REST client; gains Storage upload + report create.
- `Assets/Scripts/UI/Misc/LeaveGameButton.cs`, GameScene `PausePanel/LeaveGameButton` -- style model for the menu line (cloned at runtime).
- `firebase/firestore.rules`, `firebase/firebase.json` -- add `reports/{id}` rules, Storage rules, functions.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Diagnostics/Logs/BugReportWindow.cs` (`#if CDP_PLAYTEST_LOGS`) -- code-built uGUI overlay on its own DontDestroyOnLoad canvas (sort order above every game canvas): B2 panel (title, capture preview, TMP input, Annuler / Envoyer), C1 note; F8 toggles; Échap closes; frees the cursor while open and restores its previous lock state.
- [x] `Assets/Scripts/Diagnostics/Logs/PauseMenuReportLine.cs` (`#if`) -- on scene load, finds `PausePanel/LeaveGameButton`, clones it as « Signaler un bug » (removes the leave behaviour, rewires the click to open the window); nothing if not found.
- [x] `PlaytestLogRuntime.cs` + `FirestoreLogClient.cs` -- `SubmitReport(text, jpg)`: mark + flush + upload pass now; JPEG (≤ 1280 px wide) to Storage `reports/{id}.jpg`, doc `reports/{id}` {uid, sid, gameId, player, seat, role, phase, day, text, t, capture, version, flavor, kind}; local queue `logs/reports/` for offline; crash recovery creates `kind: crash`.
- [x] `firebase/functions/` (Node 20, europe-west1) -- `onDocumentCreated("reports/{id}")`: posts an embed (player, local time Europe/Paris, version/flavor, phase/day/role, text, `python tools/logs/logs.py around <game> <HH:MM:SS> --window 90`) + capture to channel `DISCORD_REPORTS_CHANNEL_ID`, token from secret `DISCORD_BOT_TOKEN`.
- [x] `firebase/firestore.rules`, `firebase/storage.rules`, `firebase/firebase.json`, `tools/logs/check_rules.py` -- create-only report docs (field allow-list, text ≤ 2000) and JPEGs (≤ 2 MB, image/jpeg, path = own report), no client reads; checks added.
- [x] Discord -- create hidden text channel « rapports-de-bug » (deny @everyone view, allow Poyo + bot) after Poyo's OK.
- [x] `Assets/Scripts/Tests/Editor/PlaytestLogs/` -- tests for report doc fields / JPEG sizing / rate limit (pure parts).
- [x] `.claude/skills/playtest-logs/SKILL.md`, `tools/logs/README.md` -- reports: where they land, `logs.py` `reports` command (list recent reports with their command line).

**Acceptance Criteria:**
- Given a playtest build in a game, when the player presses F8, types and sends, then within ~10 s the Discord channel shows his text, capture and a command that, run as is, prints the ±90 s around the press with a `bug.report` mark.
- Given a build without the define, then F8 does nothing and the pause menu has no new line.

## Spec Change Log

### 2026-10-20 — adversarial review round 1, all findings patched

- Échap: read on the keyboard by the window (the game's escape stack is never raised), the text box no longer restores
  "" on Échap.
- Envoyer never traps the player: the report is queued at once, the window closes, a click-through note shows
  « Envoi… » then « Envoyé, merci » / « Pas envoyé »; the immediate send gives up after 8 s (the queue goes on later).
- Cursor and look: `AvatarCameraArbiter.SetOverlayOpen` (like the tablet: cursor free, look frozen) — closing re-applies
  what the current camera mode wants instead of a stale snapshot; the pause menu's alpha is restored in a `finally`.
- Crash alerts: id = the crashed session id (copies launched together collapse into one), none for autoplay sessions.
- Queue: each report on its own (attempt counter, try/catch, the just-sent one first, 3 failures in a row stop the pass),
  dropped after 10 attempts or 3 days.
- Security: rules require exact id formats (`sid`, `gameId`, report id), a session of the same account, typed and
  bounded `seat`/`flavor`/`day`/`t`; Storage is create-only (`resource == null`); the function builds the command only
  from valid ids, escapes markdown/links/mentions in player text; `logs.py` validates ids and survives a missing capture.
- Function: idempotent (`postingSince` claim, `postedAt`), retries on Discord 429/5xx for a day, records `postError`
  otherwise; ignores `tools/logs/check_rules.py` documents (player « RulesCheck »).
- Not changed (frozen spec): « Pas envoyé » wording when offline / switch off (the report stays queued).
- Known limit kept: typing can still reach raw `Keyboard.current` hotkeys (same as the chat box).
- As built: the pause-menu line lives in `BugReportWindow.cs` (no `PauseMenuReportLine.cs`), `SubmitReport` is
  `QueueReport` + `TrySendNow`, the function runs on Node 22; channel « rapports-de-bug » id 1562180825305448578.

## Design Notes

UI built in code (no prefab, no scene change) so a public build carries nothing and GameScene stays untouched; the menu line is a runtime clone of the existing leave button, so it inherits the menu's look. Typing in the box can still reach raw `Keyboard.current` hotkeys (emote wheel, voice); the chat box has the same pre-existing limit, noted not fixed here.

## Verification

**Commands:**
- `tools/autoplay/unityctl.sh compile` + player compile with `CDP_PLAYTEST_LOGS` (Relay, Steam) -- 0 errors.
- `tools/autoplay/unityctl.sh editmode PlaytestLogs` -- green.
- `firebase deploy --only firestore,storage,functions` then `python tools/logs/check_rules.py` -- all PASS.
- Dev build with the define, autoplay run with `-playtest-logs-upload` + a scripted report (`-playtest-bug-report-at <s>` test switch) -- Discord message received, command output shows the mark.
