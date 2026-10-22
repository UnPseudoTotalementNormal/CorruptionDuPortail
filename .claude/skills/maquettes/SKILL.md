---
name: maquettes
description: Have Poyo pick a game screen on mockups before coding it — an HTML page of numbered variants drawn as real 16:9 game screens (real art, real menu background, interactive when the screen is), opened on his phone through "Aperçu de l'appli" or in a browser, with 👍/👎 votes and remarks sent back to Claude, one capture per variant, then the choice recorded. Use for any new screen or visible UI change of the game ("fais-moi des maquettes", "maquette", "montre-moi avant de coder", "mockups", "propose plusieurs designs"), and before coding any new interface (the ui-slop rule says to show mockups first).
metadata:
  template: maquettes
  template-version: 1.6.0
---

# Maquettes (Corruption du Portail)

Poyo chooses on evidence: a new screen is coded only after he picked it on mockups.
Everything lives in `tools/maquettes/`: one folder per subject (`tools/maquettes/<subject>/`), one page
per round (`v1.html`, `v2.html`…; a round of edits = a new page, the old one stays). One subject = one
precise question (`roles-book`, `cat-cosmetics`), not a whole part of the game: the contents page stays
readable and two sessions never write in the same folder. Shared:
`common/mock.css` (page chrome, frames), `common/mock.js` (scaling, `?only=`, votes, sending; **load it
last**), `serve.py` (port 5198). Made from the `maquettes` template of the skill marketplace
(`~/Projects/skill-marketplace`, other variants in `templates/maquettes/variantes.md`).

## 1. Understand, look

- Read the current screen's code, `CLAUDE.md` and the memory: choices already made, tastes.
- **Read `~/.claude/ui-slop.md` first** (his UI slop list, kept up to date at his request).
- Look at the real thing, not a memory of it. Before any variant, the page's header says three things:
  - **Reference**: how two or three games (apps, sites) that already do the thing go about it, seen for
    real (his video or captures, the game opened on the PC or the phone, a search), gestures and timings
    included, sources as links. If he cites one, that one first.
  - **Today**: what the game does now, read in the code, and what is missing.
  - **The scene**: one concrete moment of a game where the feature matters (who, what, why now), the
    same on every frame.
- Then list the questions to settle. Keep only those whose answer changes what will be coded and that
  he would not settle with his eyes closed; look also for those that do not show on screen (rules,
  sound, what counts or not, what happens on failure). What has only one sensible answer, or a run of
  screens with no alternative, fits in one line of the header (« Sans question : … »), not in a section
  to vote on. A wording question alone does not make a section.
<!-- local:regarder -->
- The screen's controller, UXML/USS or Canvas prefab, and `project-context.md`'s UI gotchas.
- Autoplay captures (`AutoplayRuns/**.png`), the scene's sprites, the art files.
<!-- /local -->
<!-- local:gouts -->
- Visuals stay **placeholder and design-owned**: no invented palette, reuse the game's art, fonts and
  tokens. Diegetic, not web/SaaS (the grimoire won over a web look).
- Player text in French, no em dash (« — »).
<!-- /local -->

## 2. The page

- `<body data-subject="<subject folder>">`, a header (what changes, what is missing or provisional), one
  **section** per question to settle (the screen, the way in, the words…), 2 to 5 **really different**
  variants each, not shades: `<div class="variant" data-id="E1">` with `.vhead` (`.vid` + `.vname`),
  `.vdesc` (what changes and why, one or two sentences), then the `.screen` frame
  (`<div class="screen"><div class="stage">…</div></div>`, drawn at 1600 × 900, variants one under the
  other). Reference page: `personnalisation/v1.html` (interactive layered cat, vignettes, menu mock).
- Take the round's number right before writing (`ls <subject>/v*.html`, the first free one): never
  write over an existing `v<N>`, another session may be working on it.
- The kit's classes are reserved (`page`, `row`, `stack`, `stage`, `variant`, `vhead`, `vid`, `vname`,
  `vdesc`, `phone`, `screen`, `card`, `toggle`, `vote`, `send`…: see `common/mock.css`): the page's own
  classes carry a prefix of their own (`rb-page`, not `page`).
- A question about a rule or a behaviour (when something happens, what if…) is shown too: one concrete
  scene per variant (what happens, before/after, on the real screen), not a sentence to interpret.
- Same moment and same data on every frame of a section; real texts, real art, nothing « Lorem ». The
  colors, fonts and images are the game's: no invented palette.
- Plan for growth: a screen listing content that will grow (cosmetics, roles…) shows it full (real
  items, say how it scales), not only today's handful.
- If the screen reacts (choices, tabs), the mockup reacts. If it moves, the mockup moves: a short looping
  scenario with a **key moments** bar at the top (one button per step, « ▶ En boucle ») that freezes
  every frame at the same instant, and `?at=<ms>` for the captures: comparable at a glance. A feature
  that unfolds over time (a transition, a reveal, sending) is never shown frozen, and a « Ralenti » helps
  judge a short movement.
- **A gesture is played.** When the question is a gesture (hold, drag, pinch, double tap), the frame
  answers it on the page (pointer events, with the finger on the phone as with the mouse): he tries it
  instead of judging a drawing of the gesture.
- **Where does it go? Let him place it.** When the question is a place (an object on the table, a button on
  the screen), don't draw five guesses: give him the object to drag on the real screen. `<img class="place"
  data-name="bougie" data-x="391" data-y="757" data-scale="1">` inside a `.stage` (x, y = its anchor in stage
  pixels, bottom centre by default) is dragged with the finger or the mouse; `data-scale` adds a size slider
  under the frame (`data-scale-min` / `-max`) with « Remettre ». The kit keeps it per page and sends it with
  the remarks (« P1 placement bougie : x=… y=… taille=… (data-info) »). Each change fires `mockplace` (bubbling,
  `detail {variant, name, x, y, scale}`): the page listens to it to size the object with the perspective, mirror
  it in another frame, and write what the code needs into `el.dataset.info` (e.g. the world position under it).
  An object from the game is rendered from the game with a transparent background (`compte-a-rebours/v3.html`:
  the real candle, the seated view's camera fitted on the capture, its place mirrored on the night's top view).
- Bottom: `<textarea id="global">` (a useful open question) and `<button id="send">`.
- `?only=E1` shows that frame alone (captures).

## 3. Reread, check, capture

- **Reread the page critically before showing it**, and redo what does not pass:
  - the header has its reference seen for real, the today and the scene;
  - each section asks a question that matters, with at least two really different answers (never a
    single proposal « to approve »);
  - what moves moves, what is touched can be touched;
  - compared with a page he liked (this project's last kept round, or another project's from the
    marketplace: the template's `variantes.md`), it is neither less polished nor less alive.

- Server: `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5198/`; if it does not answer, start
  `pythonw tools/maquettes/serve.py` detached (Clawd's mockups use 5199).
- Look at the whole page, not only the frames: `?only=` captures have neither the header nor the
  sections, a style clash hides there. One capture without `?only=` at the width he reads it. Phone:
  headless Edge does not go below 492 px (narrower, the capture is cropped and shows a false overflow),
  so the page in a 412 px iframe, from a local file (`$TEMP/tel.html`: `<body style=margin:0><iframe
  src="http://127.0.0.1:5198/<subject>/v<N>.html" width=412 height=900 style=border:0>`), captured
  with `--window-size=412,900`. Fix what overflows.
- One capture per variant in `<subject>/captures/v<N>/<id>.jpg` (headless Edge, own profile):
  ```bash
  E="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
  "$E" --headless=new --disable-gpu --user-data-dir="$TEMP/mock-shot" --hide-scrollbars \
    --force-device-scale-factor=1 --window-size=1600,900 --virtual-time-budget=3000 \
    --screenshot="$(cygpath -w $PWD/E1.png)" "http://127.0.0.1:5198/<subject>/v1.html?only=E1"
  ```
  then convert to a 1200 px JPEG (PIL) to keep the repo light. Read them (fix what overflows), put them
  in the answer (`![E1](C:\…\E1.jpg)`, one per line): Poyo often follows from his phone.

## 4. Show it, then wait for his answers ON THE SITE

- On his phone: in the Clawd Code session, ⋮ › « Aperçu de l'appli » › port 5198. The contents page shows
  big what waits for his vote (a subject's last round, no feedback yet), then the rest by day, as
  thumbnails. On the PC: `http://127.0.0.1:5198/`.
- **The site is the decision channel.** Do NOT ask the same choices again with AskUserQuestion. End the
  turn with the captures + « dis-moi quand tu as envoyé tes retours ». Questions that are not about the
  screens (network, export, scope) go in the page too: a section with text variants, or the `#global`
  prompt.
- He votes, writes, taps « Envoyer mes retours à Claude »: it lands in `<subject>/retours.txt` (dated).
  **Before anything else on his next message, read the end of that file**, even if he does not mention it.
- « Combine E1 et E3 » = a new round (`v<N+1>.html`) built from those variants, not code. Code only when
  a round has a clear 👍 with no change asked. A 👍 whose remark says « mais », « plutôt »,
  « j'imaginais » or brings another idea is a change asked: a new round. « X par défaut, Y à un tap » is a
  clear choice.

## 5. Keep the choice

`retours.txt` is a draft, out of git (`tools/maquettes/.gitignore`): the choice is kept as follows.
<!-- local:garder -->
- Commit the page and its captures with the task (never a mockup-only commit): they are the spec's
  visual reference. Cite the chosen variants in the spec / memory (« maquettes personnalisation v1 : E2, B2 »).
<!-- /local -->
- A new taste or a clear refusal goes to the memory (and to `~/.claude/ui-slop.md` if it is UI slop).
<!-- local:propre -->

## Rendering game art with Blender (headless)

Game art in mockups is rendered from the source (`.blend`) rather than drawn as placeholders; crop and
save as `.webp` to keep the repo light. Shared art lives in `common/` (`menu_bg.jpg`, book textures
`parchment`, `leather`, `waxseal`) and `fonts/` (IM FELL English, the role book's font).

`"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b file.blend --python script.py`. Traps met:
drivers can force `hide_render` (remove the object's `hide_render` drivers before hiding); to render one
part alone but correctly occluded, set the other parts `is_holdout = True` (transparent film); textures
are resolved relative to the `.blend` (keep the Drive's folder layout).

## Rendering a game object from Unity (headless) for a mockup

To place a real 3D object of the game on captures (the vote candle, T19): render it from your own headless editor
(`tools/HEADLESS_UNITY.md`), same viewpoint as the capture, alone, with a transparent background.
- Fit the capture's camera first (seated vote view: eye ≈ (0, −4.1, −15.4), pitch 33°, vertical field 40°, fitted on
  the table corners) so the object lands where the game would draw it.
- Put the object's parts on a **free layer** (25) and cull to it. An outline (hover, vote…) comes from the
  renderers' rendering layer bits, not the GameObject layer: `gameObject.SetOutline(OutlineState.Hover)` to show it.
- Transparency: render twice, on black then on white (post-processing on, it is the game's look), alpha =
  1 − (white − black) / (white₀ − black₀) with the backgrounds' own values (tonemapping keeps white below 1), colour =
  (black − black₀(1 − alpha)) / alpha. Additive parts (a flame) come out right this way; add their glow in the page
  (CSS radial gradients, `mix-blend-mode: screen`).
- Note in the page script the image's scale against the game's (focal ratio) and the pixel under the object's foot: the
  page sizes it with the depth where it is dropped.
<!-- /local -->
