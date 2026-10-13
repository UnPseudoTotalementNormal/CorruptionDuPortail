---
name: maquettes
description: Have Poyo pick a game screen on mockups before coding it — an HTML page of numbered variants drawn as real 16:9 game screens (real art, real menu background, interactive when the screen is), opened on his phone through "Aperçu de l'appli" or in a browser, with 👍/👎 votes and remarks sent back to Claude, one capture per variant, then the choice recorded. Use for any new screen or visible UI change of the game ("fais-moi des maquettes", "maquette", "montre-moi avant de coder", "mockups", "propose plusieurs designs"), and before coding any new interface (the ui-slop rule says to show mockups first). Adapted from the Clawd app's mockups skill.
---

# Maquettes (Corruption du Portail)

Poyo chooses on evidence: a new screen is coded only after he picked it on mockups. Visuals stay
**placeholder and design-owned** (no invented palette: reuse the game's art, fonts and tokens).

Everything lives in `tools/maquettes/`: one folder per subject (`personnalisation/`…), one page per round
(`v1.html`, `v2.html`…; a round of edits = a new page, the old one stays). Shared: `common/mock.css`
(page chrome, 16:9 `.screen` drawn at 1600 × 900 in `.stage`), `common/mock.js` (scaling, `?only=`, votes,
remarks, sending; **load it last**), `common/menu_bg.jpg` (main menu background), book textures
(`parchment`, `leather`, `waxseal`), `fonts/` (IM FELL English, the role book's font). Reference page:
`personnalisation/v1.html` (interactive layered cat, vignettes, menu mock).

## 1. Understand, look

- Read the current screen's code (controller, UXML/USS or Canvas prefab) and `project-context.md`'s UI
  gotchas; read the memory for choices already made and tastes.
- Look at the real thing: autoplay captures (`AutoplayRuns/**.png`), the scene's sprites, the art files.
- **Read `~/.claude/ui-slop.md` first** (his UI slop list, kept up to date at his request): no decorative pills/chips, no "a · b" metadata subtitles; diegetic,
  not web/SaaS (the grimoire won over a web look). Player text in French, no em dash (« — »).

## 2. The page

- `<body data-subject="<folder>">`, a header (what changes, what is missing or provisional), one **section**
  per question to settle (the screen, the way in, the words…), 2 to 5 **really different** variants each:
  `<div class="variant" data-id="E1">` with `.vhead` (`.vid` + `.vname`), `.vdesc` (what changes and why, one
  or two sentences), then `<div class="screen"><div class="stage">…</div></div>`.
- Plan for growth: if the screen lists content that will grow (cosmetics, roles…), show it holding many
  more items (fill with the real items, say how it scales), not only today's handful.
- Same moment and same data on every screen of a section; real texts, real art. If the screen reacts
  (choices, tabs), make the mockup react too.
- Game art in mockups: render it from the source (`.blend` in headless Blender, see below) rather than
  drawing placeholders; crop and save as `.webp` to keep the repo light.
- Bottom: `<textarea id="global">` (a useful open question) and `<button id="send">`.

## 3. Check, capture

- Server: `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5198/`; if it does not answer, start
  `pythonw tools/maquettes/serve.py` detached (port 5198, Clawd's mockups use 5199).
- One capture per screen in `<subject>/captures/v<N>/<id>.jpg` (headless Edge, own profile):
  ```bash
  E="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
  "$E" --headless=new --disable-gpu --user-data-dir="$TEMP/mock-shot-cdp" --hide-scrollbars \
    --window-size=1600,900 --virtual-time-budget=3000 \
    --screenshot="$(cygpath -w $PWD/E1.png)" "http://127.0.0.1:5198/<subject>/v1.html?only=E1"
  ```
  then convert to a 1200 px JPEG (PIL). Read them (fix what overflows), put them in the answer
  (`![E1](C:\…\E1.jpg)`, one per line): Poyo often follows from his phone.

## 4. Show it, then wait for his answers ON THE SITE

- On his phone: in the session, ⋮ › « Aperçu de l'appli » › port 5198 (the root lists every round, newest
  first). On the PC: `http://127.0.0.1:5198/`.
- **The site is the decision channel.** Do NOT ask the same choices again with AskUserQuestion (he answers
  on the page; a duplicate question wastes his time). End the turn with the captures + « dis-moi quand tu as
  envoyé tes retours ». Questions that are not about the screens (network, export, scope) go in the page
  too: add a section with text variants (one `.variant` per answer) or name them in the `#global` prompt.
- He votes, writes, taps « Envoyer mes retours à Claude »: it lands in `<subject>/retours.txt` (dated).
  **Before anything else on his next message, read the end of that file**, even if he does not mention it.
- A remark like « combine E1 et E3 » = a new round (`v<N+1>.html`) built from those variants, not code.
  Code only when a round has a clear 👍 with no change asked.

## 5. Keep the choice

- Commit the page and its captures with the task (never a mockup-only commit): they are the spec's
  visual reference. Cite the chosen variants in the spec / memory (« maquettes personnalisation v1 : E2, B2 »).
- A new taste or a clear refusal goes to the memory (and to `~/.claude/ui-slop.md` if it is UI slop).

## Rendering game art with Blender (headless)

`"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b file.blend --python script.py`. Traps met:
drivers can force `hide_render` (remove the object's `hide_render` drivers before hiding); to render one
part alone but correctly occluded, set the other parts `is_holdout = True` (transparent film); textures
are resolved relative to the `.blend` (keep the Drive's folder layout).
