---
name: autoplay
description: Play Corruption du Portail automatically with bots (host + simulated players or up to 7 real network clients, real GameScene, real power / vote / picker / network code paths) to check a precise goal, with screenshots, recordings and exported state at the right moments, then answer with evidence; and extend the autoplay framework (new lever, event, check, scenario) when a goal is not covered yet. Use when the user asks to test / check / verify / playtest / reproduce something in-game automatically ("check que tous les pouvoirs…", "vérifie que le flou…", "lance une partie auto", "fais jouer des bots", "autoplay", "teste en jeu", "teste en réseau", "est-ce que ce feedback s'affiche quand…", "simule une partie"), or to add / improve an autoplay feature ("ajoute à l'autoplay", "l'autoplay ne couvre pas…", "nouveau scénario", "extend autoplay"). PREFER this over manual play mode, MCP play-mode driving or ad-hoc scripts for any in-game behaviour or visual check.
---

# Autoplay — goal → bots play → evidence → answer

Framework: package `Packages/com.unpseudo.autoplay` (generic: runner, journal, captures + state export, dev-build
bootstrap, window guard, `Tools~/`) + the game adapter `Assets/Scripts/Autoplay/` (`CdpAutoplayGame`,
`AutoplayDriver` bot brain, `AutoplaySelectionAutopilot`). R&D + findings:
`_bmad-output/implementation-artifacts/investigations/autoplay-automated-games-rnd.md`.

- **Every lever, event, capture, check and scenario that exists:** `tools/autoplay/REFERENCE.md`. Look there first.
- **Adding to the framework:** `Packages/com.unpseudo.autoplay/EXTENDING.md` (generic method, rules, definition of
  done) + `tools/autoplay/REFERENCE.md` § Extending in this game (where it goes here, backlog recipes).
- Package README (layout, outputs, hard-won lessons): read once.

## 1. Turn the request into a goal you can check

Write down, before running anything: **what must be observed, when, on whose screen, and what counts as pass/fail**.
Map it to what the run produces:
- game logic / flow / errors → `report.json` (outcome, phase trace, counters, errors) + `events.ndjson`
- exact values at a moment → the capture's state file (`NNN-label.json`, game state under `"game"`, probes)
- visuals → the capture PNGs at that moment (bursts give several timings of one moment)

Moments captured out of the box: every phase change, every power verdict (burst 0.15/0.5/1.2 s), and in
`visual-picker` mode every picker opening (burst 0/0.1/0.25/0.5/1 s + hover). Need another moment or value? Add a
`capture.Request(...)` / `RequestBurst(...)` call or an `AddProbe(...)` in the driver — small, then rebuild.

**Force the situation from the start.** A random game does not guarantee the case under test: before launching, ask
"what must happen for this run to prove something, and what guarantees it?" If nothing does, add the scenario lever
first (`-autoplay-vote-focus <role text>` = every bot votes that role, `-autoplay-clients/players/bots`, seed, or a
new forced-composition option), then run. Afterwards, check in the logs that the situation really happened (e.g.
`portal.click` logged by a client) — otherwise report "not covered", never "OK".

## 2. Environment (once per session)

- By default drive **your own headless editor on your checkout** (batchmode → modal dialogs auto-cancelled, nothing
  on screen). Use the user's open editor only when they ask for it; when unclear, ask. Recipe + traps:
  `tools/HEADLESS_UNITY.md`.
- Everything long runs **in the background** (`run_in_background` / Monitor), never in a foreground loop.
- **Watch the watchdog.** Every run is under `AutoplayWatchdog` (per step / phase budgets, see `REFERENCE.md` § Watchdog):
  arm a Monitor on `AutoplayRuns/alerts.log` (`tail -f … | grep --line-buffered -E "WATCHDOG|RUN.FAIL|DIED|ABORT|TIMEOUT|BUSY"`)
  for any long run or campaign. On each alert: read the line (what it waits on) + the folder's `watchdog.json`, look at
  the capture if needed, then write `extend <s>` (legit) or `abort <reason>` (stuck) into `<folder>/watchdog-control.txt`.
  Never wait out a global timeout on a stuck run.
- Stopping a campaign on Windows: `TaskStop` leaves the child bash / python / powershell / game processes alive: kill
  them by command line too, and check nothing is left before launching again (launchers queue, but orphans keep going).
- Use `tools/autoplay/unityctl.sh` (wrapper of the package CLI): `compile`, `editmode`, `build`, `play-build`,
  `last-run`. Never recompile while a PlayMode run is in flight.

## 3. Pick the run mode

| Goal | Mode |
|---|---|
| Whole game / flow / errors / visuals | **windowed dev build**: `tools/autoplay/unityctl.sh build` (only if code changed since the last build) then `play-build <seed> <port>` |
| Picker visuals (blur veil, lifted cards, hover) | same, with `AUTOPLAY_VISUAL_PICKER=1` |
| Real network: replication / desync between host and clients | `tools/autoplay/unityctl.sh play-net <clients> <seed>` (host + N real clients, bots fill to 8) → `compare_runs.py` verdict |
| Quick logic check of the first night only | `tools/autoplay/unityctl.sh playmode AutoplaySoloHostTests` (batchmode editor cannot finish a game: end of frame never comes) |
| Does the UI really let a player do it? (hidden / covered button, screen never shown, element out of reach) | add `-autoplay-real-input` (every seat clicks through the real EventSystem with a virtual mouse; `input.miss` names what took the click) — slower, for thorough checks; `-autoplay-lobby-ui`, `-autoplay-real-input-tour`, `-autoplay-menu-ui` for the lobby tablet, in-game interfaces and main menu |

Coverage = several seeds (roles are drawn from the designer's classic preset). Report which powers/roles a run covered;
if the goal needs a specific power, loop seeds until it appears (events `power.start`), or add a forced-composition
option before claiming coverage.

**Changing the bot brain (new role, state, action)?** Validate it with `play-net` too, not only `play-build`: host +
simulated bots runs everything on the server, which hides client-side bot bugs. On a real client, never gate an
action on a field only the server writes (GameState objects are cloned per process; e.g. `TakeDownThePortalState.
shouldActivate` is server-only, the client only gets the Mage id by RPC) — react to what the server actually sends the
client, like the UI does. (Memory: `reference_autoplay_client_vs_server_state`.)

Players are launched **without focus**, muted, never cursor-locked (`launch-background.ps1` + `AutoplayWindowGuard`):
the user keeps working. Never minimize a player (black captures). Ports 7850–7899 only.

### Scenario levers (player args, `-autoplay-<key>`)

Complete list with every package option: `tools/autoplay/REFERENCE.md`. Most used:
`force-roles A,B` (role-name fragments guaranteed in the composition) · `role-holder host|client|bot` (who must hold
them; mismatch fails fast and the scenario runner retries the next seed) · `vote-focus <role text>` · `max-days N` ·
`netsim delay,jitter,loss` (Multiplayer Tools Network Simulator; put it in `clientArgs`) · `quit-at <phase text>`
(a client leaves mid-game; put it in `client1Args`) · `target-focus <client|host|bot|role>` (+ `target-focus-power`)
· `chat` · `build-version` / `stall-load` (+ host `expect-clients`) · `visual-picker` · `clients/players/bots` · `real-input`
(+ `lobby-ui`, `real-input-tour`, `menu-ui`, `real-input-mask <name>` breakage test) · `power-use-probability` /
`vote-probability`. Real input never touches the user's own mouse or keyboard: the player's real devices are disabled
and the user's cursor is never captured; never drive the real cursor from a script.

### Declarative scenarios — prefer them for anything worth re-running

Write `tools/autoplay/scenarios/<name>.json` (goal, mode, levers, expectations — format in
`Packages/com.unpseudo.autoplay/Tools~/run_scenario.py`) and run
`python -X utf8 Packages/com.unpseudo.autoplay/Tools~/run_scenario.py tools/autoplay/scenarios/<name>.json`.
A passing scenario stays as a regression test; `tools/autoplay/campaign.sh` replays them all + random games.
Coverage of targeted powers: `python -X utf8 tools/autoplay/sweep_powers.py` (one forced-role visual run per role →
`coverage.md`, OK / FAIL / NOT COVERED per power).

### Animations

To check that an animation plays right, record it: `-autoplay-record "power.start:1.8"` (trigger on an event BEFORE the
animation, not on its own start, or the first frames are lost) → `rec-NNN-*/` frames + `tracks.csv`. Look at ONE contact
sheet (`python -X utf8 Packages/com.unpseudo.autoplay/Tools~/contact_sheet.py <rec-dir> --every 2 --track <name>`), and
assert on the numbers with an `animation` check (thresholds measured on a reference run, never invented). Reference:
`tools/autoplay/scenarios/picker-animation.json`. A late-started recording or a run stopped mid-burst is an artefact,
not a game bug — check before concluding.

### Speed
Measured costs: ~45-60 s of start-up per run (player boot + GameScene + clients joining), so prefer fewer, richer runs.
Levers that keep the test's value: `role-holder` SEATS the forced role (no re-roll of whole games); clients capped at
30 fps (`-ClientFps`); `-autoplay-fast-phases "Intro|Chaining"` on network / desync runs (nobody acts there; keep recaps
at normal speed when the goal samples them); 2 days for a power / role sweep; 2 lanes in parallel
(`AUTOPLAY_MAX_PARALLEL=2`, `run_scenario.py --port 7870` / `--port 7880`) once the machine is known to cope (watch for
`DIED`). Do not raise the global time scale for desync hunts (latency weighs more than in a real game) and never run
clients in batchmode (end of frame never comes).

`-autoplay-fast-fakes` (fake roles sleep after ~1 s: night time 77 s → 12 s on 3 days, seed 777) and
`-autoplay-fast-phases "Recap|Intro|Chaining" -autoplay-fast-timescale 12` cut a game ~3× (118 s → 39.5 s). Use them by
default for flow / coverage runs; leave them OFF when the goal is the real timing of fake roles or of those phases.

### Tests réalisés

Tenir ce tableau à jour : chaque exécution d'un scénario met à jour sa ligne (date + résultat), chaque nouveau
scénario ajoute la sienne. Ce qui est vérifié en détail : `tools/autoplay/REFERENCE.md` § Scenarios.

| Test | Ce qu'il prouve | Mode | Dernier résultat |
|---|---|---|---|
| `mage-portal-client` | un Mage tenu par un vrai client abat le portail | réseau, 7 clients | PASS 2026-10-04 |
| `picker-visual` | chaque ouverture du sélecteur : voile flou + cartes valides levées | build | PASS 2026-10-04 |
| `picker-animation` | flou 0→1 en ~0,3 s, cartes +2 en y stabilisées à ~0,45 s | build (enregistrement) | PASS 2026-10-04 |
| `client-leaves-at-vote` | un client qui part au vote est enchaîné, la partie continue | réseau, 3 clients | PASS 2026-10-04 |
| `lag-150ms` | partie complète sous 150 ms de latence | réseau, 3 clients | PASS 2026-10-04 |
| `net-sync-3clients` | zéro désync sur chaque composant de l'état public | réseau, 3 clients | PASS 2026-10-05 |
| `client-owner-local-powers` | les révélations du Repenti / de l'Orpheline atteignent le client qui les tient | réseau, 7 clients | PASS 2026-10-05 |
| `orpheline-contact-chosen` | Manque d'affection sur un élu : ligne de contact + rôle révélé | réseau, 7 clients | PASS 2026-10-05 |
| `orpheline-contact-anomaly` | Manque d'affection sur une anomalie : ligne de contact, pas de révélation | réseau, 7 clients | PASS 2026-10-05 |
| `chat-private` | les messages privés atteignent chaque membre réel, jamais un non-membre | réseau, 7 clients | PASS 2026-10-05 |
| `join-version-mismatch` | une autre version du jeu est refusée avec le message de version | réseau, 3 clients | PASS 2026-10-05 |
| `join-stuck-load-kick` | un chargement bloqué est éjecté à 90 s, le lobby démarre sans lui | réseau, 3 clients | PASS 2026-10-05 |
| `join-slow-load-honest` | un chargement lent (30 s) n'est pas éjecté, le client joue | réseau, 3 clients | PASS 2026-10-05 |
| `join-spawn-during-load` | un joueur assis pendant un chargement > 10 s est vu par tous (propriétaire + liste complète), 0 désync | réseau, 3 clients | PASS 2026-10-05 (FAIL 2/2 avant le correctif) |
| `net-sync-7clients-chat` | table pleine de vrais joueurs (hôte + 7 clients) qui discutent en privé : 0 désync sur 8 process, aucune fuite | réseau, 7 clients | PASS 2026-10-06 |
| `heavy-loss` | partie complète à 250 ms, gigue 80 ms, 5 % de pertes, avec chat | réseau, 3 clients | PASS 2026-10-06 |
| `rejoin-at-vote` | déconnexion en plein vote du jour 1 puis retour : le vote se résout, siège intact | réseau, 3 clients | PASS 2026-10-06 |
| `rejoin-at-night` | déconnexion la nuit (pouvoirs en cours) puis retour : siège intact, listes de pouvoirs complètes | réseau, 3 clients | PASS 2026-10-06 (FAIL 1/2 avant le correctif `CharacterManager`) |
| `rejoin-under-lag` | retour par le menu avec 150 ms, gigue 40 ms, 2 % de pertes | réseau, 3 clients | PASS 2026-10-06 |
| `mass-rejoin` | les 3 clients tombent en même temps et reviennent tous | réseau, 3 clients | PASS 2026-10-06 |
| `rejoin-after-expiry` | retour après la grâce : refus « La partie a déjà commencé. », session oubliée, la partie continue | réseau, 3 clients | PASS 2026-10-06 |
| `full-game-victory` | partie réseau jouée jusqu'à la victoire (sans limite de jours), 3 clients + chat | réseau, 3 clients | PASS 2026-10-06 |
| `full-game-ending` | puis 10 s sur l'écran de fin : mêmes vainqueurs partout, plateau = vainqueurs (`analyze_ending.py`) | réseau, 3 clients | PASS 2026-10-06 (seeds 302-304) |
| `last-anomaly-leaves` | la dernière anomalie quitte : grâce expirée → victoire des élus, la partie RESTE sur l'écran de fin | réseau, 3 clients | PASS 2026-10-06 (FAIL avant le correctif N2 : retour au lobby) |
| `last-anomaly-leaves-recap` | même chose, grâce expirée PENDANT le récap du vote (course N2 forcée) | réseau, 3 clients | PASS 2026-10-06 |
| `rejoin-while-chained` | un enchaîné qui se déconnecte reprend son siège (toujours enchaîné, revote) | réseau, 3 clients | PASS 2026-10-07 ×2 (après le correctif D1 ; refus avant) |
| `rejoin-at-chaining` | déconnexion au début de l'enchaînement puis retour | réseau, 3 clients | PASS 2026-10-06 (FAIL avant N4) |
| `idle-table` | personne n'agit 4 jours : chaque phase finit sur son minuteur | réseau, 2 clients | PASS 2026-10-06 |
| `full-table-lag-victory` | hôte + 7 clients, 100 ms / 1 % de pertes, partie complète + écran de fin | réseau, 7 clients | PASS 2026-10-06 |
| `replay-net` | « Terminer la partie » puis 2e partie dans les mêmes process | réseau, 3 clients | PASS 2026-10-06 (FAIL avant N5) |
| `real-input-full-game` | partie complète par vraies entrées, clic réel sur « Terminer la partie », 2e partie | réseau, 3 clients | PASS 2026-10-06 |
| `host-leaves-mid-game` | l'hôte quitte en plein vote : clients au menu sans avis d'erreur | réseau, 3 clients | PASS 2026-10-06 (FAIL avant N7) |
| `host-crash-mid-game` | l'hôte crashe : « Connexion à l'hôte perdue » sur chaque client | réseau, 3 clients | PASS 2026-10-06 |
| `late-join-refused` | un nouveau venu en pleine partie est refusé « La partie a déjà commencé. » | réseau, 3 clients | PASS 2026-10-06 |
| balayage d'enchaînement (`sweep_chain_roles.py`) | chaque rôle, tenu par un vrai client (ou l'hôte : `--holder host`), enchaîné au 1er vote | réseau, 3 clients | 14/14 client + 14/14 hôte 2026-10-07 |
| balayages par phase (`sweep_phases.py` + `scenarios/templates/`) | départ / rejoin / crash hôte / départ hôte à chacune des 7 phases | réseau, 3 clients | 4 × 7/7 2026-10-07 (après N8, N9) |
| `copies-uges-client` | Ugës (vrai client) vole Soin Baveux : rien la nuit du vol, une copie par nuit ensuite, copie dépensée disparue partout, pairs d'accord | réseau, 2 clients | PASS 2026-10-07 |
| `copies-uges-real-input` | même chose par vraies entrées : Ugës clique ses copies dans la barre de pouvoirs (nuits 2, 3, 4), aucun clic raté | réseau, 2 clients | PASS 2026-10-07 |
| balayage des copies (`sweep_copies.py`) | Ugës vole chacun des 11 pouvoirs actifs d'élu, Luma copie chaque élu factice, l'Incomplet en Ugës / Luma, Ugës hôte, faux Ugës (couche non instantanée), rejoin d'Ugës, 8 chaînes de copieurs dans les deux ordres | réseau, 2 clients | 33/33 cas 2026-10-07 (3 échecs d'outil corrigés puis rejoués, 1 non couvert : Ugës enchaîné par Abyss la nuit 1, rejoué avec une autre graine) |
| balayage des pouvoirs (`sweep_powers.py`) | chaque pouvoir ciblé est utilisé et résolu | build | 15/15 OK 2026-10-04 |
| campagne (`campaign.sh`) | tous les scénarios + parties aléatoires | mixte | 45/48 2026-10-07 (3 échecs = outil : T8, T9 corrigés, `real-input-lobby` instable) |
| `real-input-actions` | pouvoirs, cartes, vote (réticule), sommeil : vrais clics avec leur effet, sur chaque écran | réseau, 3 clients | PASS 2026-10-05 |
| `real-input-mask` | un bouton de sommeil masqué fait échouer le scénario (`input.miss hit=AutoplayMask`) | build | PASS 2026-10-05 |
| `real-input-tour` | infobulle, pause + curseur audio, tablette + chat, roue d'émotes par vraies entrées | réseau, 3 clients | PASS 2026-10-05 |
| `real-input-lobby` | lobby à la souris : preset, molette + Imposé « + », « Prêt » partout, départ par `TryAutoStart` | réseau, 3 clients | INSTABLE 2026-10-07 (2/5 ; build Dev non modifié 1/2 : pas une régression, N11) |
| `real-input-menu` | premier écran capturé ; la notification de refus passe au-dessus de l'écran de connexion et se ferme au clic | réseau, 3 clients | PASS 2026-10-05 |

### Not covered? Extend, do not hand it back

When the goal needs something autoplay cannot do yet (bots never chat, nothing stalls a join, targets are random…),
the default is to **add the lever / event / check**, then prove the goal with a scenario: follow
`Packages/com.unpseudo.autoplay/EXTENDING.md` (definition of done: compile, scenario PASS on `play-net` if a client is
involved, shown able to fail, `REFERENCE.md` updated). Start from the backlog recipes in `tools/autoplay/REFERENCE.md`
when the gap is listed there. Hand a point to human playtest only when the extension is out of scope for the task,
and then say which extension would cover it (and add it to the backlog table).

## 4. Read the evidence — cheaply

1. `tools/autoplay/unityctl.sh last-run` (outcome, trace, counters, deduplicated errors).
2. Goal-specific analysis over the state files (e.g. `python -X utf8 tools/autoplay/analyze_picker.py <run>`); write a
   small analyzer for a new kind of goal rather than reading hundreds of files.
3. Look at **a few targeted PNGs** only (the ones an analysis flags, plus one passing example) — never all of them.

## 5. Answer

- Verdict per goal (pass / fail / not covered), with the run folder, the key capture paths and numbers. A "not
  covered" names the extension that would cover it.
- Separate: **game bugs** (with root cause if proven — instrument with a tagged log before theorising),
  **tool problems** (fix them), and **design observations** (overlaps, layout — report only, design is owned by the
  game designer).
- Never commit `AutoplayRuns/` or editor import noise; stage explicit files only, and only when the user says so.
