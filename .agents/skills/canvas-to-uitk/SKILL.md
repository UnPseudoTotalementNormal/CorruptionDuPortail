---
name: canvas-to-uitk
description: "Convert ONE Unity uGUI Canvas screen (prefab + MonoBehaviour) into an equivalent UI Toolkit screen (UXML + USS + UIDocument controller) — close-to-faithful, then a hand-tune checklist for what can't map 1:1. Use when the user says 'convert Canvas to UITK', 'migrate this Canvas screen to UI Toolkit', 'canvas to uitk', 'passe ce Canvas en UI Toolkit', 'convertir Canvas vers UI Toolkit', 'reconvertir en UITK', or points at a Canvas prefab/MonoBehaviour and wants a UI Toolkit version. PREFER this over an ad-hoc manual rebuild."
---

# uGUI Canvas → UI Toolkit conversion (one screen)

**Goal:** Rebuild a single uGUI Canvas screen as a UITK screen (UXML structure + USS style + a rewritten `UIDocument` controller) that behaves the same, landing **close** to the original visually, and hand back a precise list of the ~20% that needs human tuning. Semi-automated, collaborative — NOT a push-button pixel-perfect port.

**Why this exists (project context):** the reverse of [[uitk-to-canvas]]. When a screen wants UITK strengths (data-binding, resolution-independent flex layout, one-file structure/style, USS transitions), this skill re-expresses a Canvas screen as UITK — structure/behaviour-preserving.

**Fidelity contract:** "close + hand-tune". LayoutGroup ↔ flexbox is lossy; never claim pixel parity. When something can't map cleanly, FLAG it — don't fake it.

⚠️ **Dev has ZERO UITK infra** (no `Assets/UI`, no PanelSettings/USS/token files) — see [[project_ui_toolkit_migration]]. First conversion on Dev needs a greenfield UITK bootstrap (PanelSettings + a root theme `.tss` + token `variables.uss`) OR a cherry-pick of the Wave-0 foundation from the `UI-Toolkit-try` branch. Confirm which before building. Rest of game stays Canvas — the two coexist fine.

---

## Inputs to gather first

1. The Canvas **prefab** (or scene subtree): the full `RectTransform` hierarchy + every `Image`, `TextMeshProUGUI`, `Button`, `LayoutGroup`, `LayoutElement`, `ContentSizeFitter`, `Mask`/`RectMask2D`, `ScrollRect`.
2. The `CanvasScaler` on the root Canvas — read its **UI Scale Mode** + **Reference Resolution** + **Match** (drives the PanelSettings scale mode / reference resolution) and the Canvas render mode + `sortingOrder`.
3. The controller `.cs` (the `MonoBehaviour`): every `[SerializeField]` element ref, `SetActive`, `CanvasGroup` toggle, color/sprite swap, `Button.onClick`/`EventTrigger`, DOTween tween, and dynamic `Instantiate` of row prefabs.
4. **Every clickable / interactive element** — not just `Button`. Grep the subtree + code for `EventTrigger`, `IPointerClickHandler`/`IPointerDownHandler`/`IDragHandler` (custom scripts), `Toggle`, `Slider`, `ScrollRect`, and any `Graphic` with `raycastTarget = true` used as a hit area. Each needs an explicit UITK callback — a lost click is a silent regression.
5. **Custom `MonoBehaviour`s on the subtree that add logic/visuals** — hover/press tint scripts, marquee/typewriter text, procedural fills, tween drivers, anything not a stock uGUI component. List each; its behaviour must be re-authored in the UITK controller (it does NOT come for free from UXML/USS).
6. ⚠️ **Runtime-spawned content** — internal OR external scripts that `Instantiate` prefabs/GameObjects into this Canvas (lists, grids, notifications, pooled items). **The editor prefab is an INCOMPLETE snapshot**: what you see at rest ≠ the live tree. Trace who spawns into it (grep for `Instantiate(...` targeting this canvas/container, object pools, other systems writing to it) and model each spawn as a UITK row-template clone. Miss this and the converted screen renders empty/half at runtime.
7. Any DOTween sequences driving entrance/exit/state anims — these become USS `transition` (approximate) or stay in C#.
8. Where the UITK output should live: the `.uxml` + `.uss` paths, the PanelSettings asset, and the scene GameObject that carries the `UIDocument`.

Use Unity MCP resources/tools to read the prefab/components; use `Read`/`Grep` for the `.cs`. **Grep the whole codebase, not just the controller** — spawners and click-logic often live in other scripts.

---

## Mapping tables (the reusable core)

### GameObject → UXML element
| uGUI | UITK |
|---|---|
| GameObject + `RectTransform` (no graphic) | `<VisualElement>` |
| `Image` (bg only) | `<VisualElement>` with `background-color` / `background-image` USS |
| `TextMeshProUGUI` | `<Label>` |
| `Button` + `Image` + child text | `<Button text="…">` (bg + label fold into the Button + USS) |
| `ScrollRect` + Viewport + Content | `<ScrollView>` |
| `Toggle` / `TMP_InputField` / `Slider` | `<Toggle>` / `<TextField>` / `<Slider>` |
| `Mask`/`RectMask2D` | `overflow: hidden` USS (not a node) |
| `LayoutElement`, `ContentSizeFitter` | fold into flex USS on the element (not nodes) |

### uGUI component/value → USS (the lossy part)
| uGUI | USS | Note |
|---|---|---|
| `HorizontalLayoutGroup` / `VerticalLayoutGroup` | `display: flex; flex-direction: row/column` | |
| LayoutGroup `childAlignment` | `justify-content` + `align-items` | approximate |
| LayoutGroup `spacing` | ⚠️ no `gap` in Unity USS → per-child `margin` or spacer | **flag** |
| LayoutGroup `padding` | `padding` | |
| `childForceExpand` / `LayoutElement.flexibleWidth/Height` | `flex-grow` / `flex-shrink` | |
| `LayoutElement.preferredWidth/Height` | `width`/`height` (or `min-`/`max-`) px | approximate |
| free `RectTransform` anchors/pivot/`sizeDelta` | `position: absolute` + `left/top/right/bottom` or `width/height` | anchor+pivot math is manual; a stretched anchor (min≠max) ≈ `left:0;right:0` — **flag** |
| `RectTransform` rotation/scale (Z-rot, non-uniform scale) | USS `rotate` / `scale` | pivot ↔ `transform-origin` — **flag** |
| `Image.color` = white + `sprite` | `background-image: url("…")` | plain textured element |
| `Image.color` = **tint** over a sprite (multiply) | ⚠️ USS `-unity-background-image-tint-color` (NOT `background-color` — that paints a flat fill *behind*, doesn't tint the sprite) | **flag — easy to get wrong** |
| `Image.color` flat (no sprite) | `background-color` | |
| `Image` HSV/hue done via **tint color** | bake the final RGBA into the tint/`background-color` | a hue offset is just the resulting color — resolve it, don't invent a filter |
| `RawImage` (raw texture) | `background-image: url("…")` | no tiling/UV-rect in USS — **flag** if `uvRect`≠default |
| **custom `Material` / shader** on `Image`/`Graphic` | ⚠️ NO per-element material in UITK. Options: bake to a static sprite, a USS gradient/`-unity-` prop if it's simple, or a Panel `render-texture`/custom pass | **flag — biggest gap; confirm with user** |
| `Image` type Sliced (9-slice) | `background-image` + `-unity-slice-*` | |
| `Image` type Tiled / Filled | ⚠️ no USS tile-repeat / radial-fill | **flag** |
| `Outline` / `Shadow` / rounded sprite | `border-width`+`border-color` / `text-shadow`/box shadow / `border-radius` | UITK does border+radius NATIVELY — a win |
| TMP `color`/`fontSize`/`fontStyle` | `color` / `font-size` / `-unity-font-style` | |
| TMP `alignment` / `enableWordWrapping` | `-unity-text-align` / `white-space` | |
| TMP gradient / outline / face-dilate | ⚠️ TMP material features — no USS equivalent | **flag** |
| TMP **SDF `TMP_FontAsset`** | UITK **`FontAsset`** / `-unity-font-definition` | different asset — **flag** |
| `CanvasGroup.alpha` | `opacity` | |
| `CanvasGroup.interactable` | `SetEnabled(false)` / `:disabled` | |
| `CanvasGroup.blocksRaycasts` | `pickingMode: Ignore/Position` | ⚠️ see world-input gotcha — do NOT blanket-map onto the root |
| TMP `characterSpacing` | `letter-spacing` | |
| **DOTween tween** (scale/move/fade/rotate) | ⚠️ USS `transition` + `translate`/`scale`/`rotate` (eased approximation only) or keep in C# | **flag** |
| `Scrollbar` + handle | `<ScrollView>` scroller + USS | approximate |

### Controller MonoBehaviour → `UIDocument` controller
| uGUI | UITK |
|---|---|
| `[SerializeField] T x;` per element | `[SerializeField] UIDocument` + `root.Q<T>("x")` (name each element in UXML) |
| `SetActive(b)` | `style.display = b ? Flex : None` (or `AddToClassList`/`RemoveFromClassList`) |
| `CanvasGroup.alpha/interactable` | `EnableInClassList`/opacity + `SetEnabled` / `pickingMode` |
| color/sprite swap for a state | `AddToClassList/RemoveFromClassList` (a state class in USS) |
| `Button.onClick` / `EventTrigger` | `RegisterCallback<ClickEvent>` / `clicked +=` |
| coroutine / `UniTask.Delay` | `schedule.Execute(...).ExecuteLater(ms)` or keep UniTask (this project = UniTask) |
| `Instantiate(rowPrefab, container)` | clone a row **VisualTreeAsset** (`rowUxml.Instantiate()`) + `Add`, or `new Label()`/`VisualElement()` |
| `Image.sprite = sprite` | `element.style.backgroundImage = new StyleBackground(sprite)` |

---

## Flow

### Step 1 — Parse
Read the Canvas prefab tree (each element + its components + resolved values), the `CanvasScaler` (scale mode + ref resolution + match), and the controller. Build an internal tree of (element, components, name). **Also build a second list the tree alone won't show**: (a) every interactive element + who handles its click, (b) every custom `MonoBehaviour` adding logic/visuals, (c) every runtime spawn site that injects children — grep the whole codebase for `Instantiate` into this canvas + pools + external writers. The static prefab is a partial snapshot; the live screen is prefab + spawns.

### Step 2 — Produce a conversion PLAN, then HALT for approval
Output, for the user to review before any mutation:
- **UXML tree**: the target element hierarchy (names mirror the GameObject names).
- **USS map**: per element, the resolved USS rules — tokenize hardcoded colors/sizes to `var(--…)` and flag them **provisional** (never enshrine an invented palette — design-owned, see [[project_ui_visuals_placeholder]]).
- **Controller rewrite**: the `UIDocument` + `Q<>()` fields, the state-toggle → class translations, the event wiring, the dynamic-`Instantiate` → clone-VisualTreeAsset plan.
- **Bootstrap decision**: greenfield UITK infra vs cherry-pick Wave-0 from `UI-Toolkit-try` (see [[project_ui_toolkit_migration]]).
- **⚠️ Flagged**: everything from the "flag" rows this screen actually uses — LayoutGroup spacing→gap, free-anchor→absolute math, DOTween→transition, TMP font→FontAsset, exotic layouts. Propose an approach for each.
Get the user's OK (and answers to the flags) before building.

### Step 3 — Bootstrap UITK infra (only if Dev has none)
- Create/confirm a `PanelSettings` asset: set its **Scale Mode** + **Reference Resolution** from the CanvasScaler (mismatch = everything wrong-size, the #1 "looks nothing alike" cause).
- Create/confirm the root theme `.tss` and a token `variables.uss` it `@import`s; put provisional tokens there.
- OR cherry-pick the Wave-0 foundation from `UI-Toolkit-try`.

### Step 4 — Author the UXML + USS
- Write the `.uxml` (structure, element `name`s mirror the source) and `.uss` (per-element rules, resolved from the token file). `<Style src>`-link the USS.
- Native `border`/`border-radius`/gradient/shadow now go straight into USS — no sprite needed (a genuine UITK win over the Canvas source).
- Back up any scene/prefab you mutate into repo-root `BackupToolkit/` (gitignored) — see [[feedback_toolkit_scene_backup]].

### Step 5 — Rewrite the controller
- Prefer `create_script` for the NEW file (see [[reference_unity_silent_compile_exclusion]]); keep the same namespace/asmdef — and add an explicit Domain asmdef ref if it moves ([[reference_domain_asmdef_autoref_tests]]).
- Translate fields (`Q<>()`), state toggles (class add/remove), events (`RegisterCallback`), and dynamic builds per the table. This project: async = **UniTask**, audio = FMOD, never `AudioSource`.
- After writing, poll `read_console` for compile errors before continuing.

### Step 6 — Wire + place the UIDocument
- Put a GameObject in the scene with the `UIDocument` component; assign the `PanelSettings` + the source `.uxml` (`visualTreeAsset`), then set the controller's `[SerializeField] UIDocument`. **Read each ref back** to confirm it resolved, not null — object refs silently go null otherwise ([[reference_unity_mcp_serialized_field_wiring]], [[feedback_serialized_field_rewiring]]).

### Step 7 — Verify (render-compare) + hand-tune checklist
- Capture the ORIGINAL Canvas: `manage_camera` / Game-view screenshot of the prefab instance.
- Capture the NEW UITK: `manage_ui render_ui` (throwaway scene with the UIDocument if needed; remove `display:none` state classes via `modify_visual_element`; render twice in play mode; delete the throwaway scene after).
- Put the two side by side, list concrete diffs (spacing, wrap, anchor drift, font), hand back a **hand-tune checklist** — the flagged items + parity gaps. Be honest about what's approximate.

---

## Gotchas & project specifics
- **State screen must not block world input**: do NOT map a Canvas `GraphicRaycaster`/`blocksRaycasts` veil onto the UITK **root** `pickingMode` — a Position root blocks the whole screen. Keep the StateUI root `pickingMode: Ignore` and put the world-mask on a **scrim child**. See [[reference_uitk_state_screen_blocks_world_input]] — this regressed in-game cards/vote + lobby tablet last time.
- **DOTween → USS transition is only an approximation**: easing curves don't map 1:1; the migration dropped some fullscreen Backgrounds and only approximated DOTween easing. Keep timing-critical or bespoke anims in C#; flag them.
- **Font**: the Canvas uses a TMP **SDF `TMP_FontAsset`**; UITK needs a UITK **`FontAsset`** / `-unity-font-definition`. Different asset — create/wire the UITK one, don't point USS at the SDF asset.
- **Scale**: PanelSettings scale mode must match the CanvasScaler. #1 cause of "wrong size everywhere".
- **No `gap` in Unity USS**: LayoutGroup `spacing` → per-child `margin` or spacer elements — flag it.
- **The devil is in the per-element detail — inspect EVERY component, don't eyeball**:
  - **`CanvasGroup`**: read all four fields, not just `alpha` → `opacity`. `interactable` → `SetEnabled`, `blocksRaycasts` → `pickingMode` (but never blanket-map onto the root, see world-input gotcha), `ignoreParentGroups` has no USS analog — flag.
  - **`Image.color`**: is it a flat fill (→ `background-color`) or a **tint over a sprite** (→ `-unity-background-image-tint-color`)? Getting these two backwards is the classic silent visual bug. A "hue"/HSV shift is just the resulting RGBA — resolve the final color, don't try to reproduce a filter.
  - **Custom `Material`/shader** on any `Image`/`Graphic`/`RawImage`: **the** hard gap. UITK has no per-element material. Bake to a sprite, approximate with USS, or a panel render pass — always confirm the approach with the user.
  - **`Mask` vs `RectMask2D`**: both → `overflow: hidden`, but a soft/alpha `Mask` (sprite-shaped) has no USS equivalent (only rect clip) — flag.
  - **Layout**: capture `childControlWidth/Height`, `childForceExpand`, `childScaleWidth`, reverse-arrangement, `ContentSizeFitter` fit modes — each maps to a distinct flex prop; don't collapse them.
  - **Anchors + pivot**: a stretched anchor (min≠max) is `left/right:0` (fill), a point anchor is `position:absolute` + offset; pivot ↔ `transform-origin`. Anchor→flex is the lossiest step — flag any non-trivial anchoring.
  - **RectTransform rotation/scale**: Z-rotation and non-uniform scale → USS `rotate`/`scale` about `transform-origin`.
  - **TMP material features** (gradient, outline, underlay/shadow, face dilate): no USS equivalent — flag, don't fake.
- **Clickables beyond `Button`**: every `EventTrigger`, `IPointer*`/`IDrag*` custom handler, and raycast-target `Graphic` used as a hit area needs its own UITK `RegisterCallback`. Enumerate them; a dropped click reads as "the button does nothing" — a silent regression.
- **Custom logic scripts don't survive the visual port**: hover/press tints, typewriter/marquee text, procedural bars, tween drivers — none come from UXML/USS. Re-author each in the controller (UniTask/FMOD project rules apply).
- **The prefab is a PARTIAL snapshot — model the runtime tree, not the editor tree**: if internal or external scripts `Instantiate` into this Canvas (lists, grids, toasts, pooled cells), the at-rest editor view is incomplete. Grep the codebase for every spawn site targeting this screen and reproduce each as a UITK template-clone (`VisualTreeAsset.Instantiate()` + `Add`). If you only convert what's visible in the editor, the live UITK screen renders empty/half. **Render-compare (Step 7) in PLAY mode with the spawns populated**, not on the bare prefab.
- **Tokens are provisional**: green accent + current look are placeholder ([[project_ui_visuals_placeholder]]); tokenize as swappable, never invent a palette.
- **Backup before scene/prefab mutation**; **verify every wired ref**; **compile-check after each script change**. Non-negotiable.
- Output is asset/text (UXML/USS/`.cs` diff cleanly; the UIDocument placement is GUID-wired) — budget for the verify loop.

## When to refuse / escalate
- Heavy DOTween choreography, complex free-anchored (non-LayoutGroup) subtrees, or bespoke uGUI controls: convert the structure, then flag the behavior for manual authoring rather than guessing.
- If the screen is world-space or input-critical, confirm the interaction model (pickingMode, PanelSettings, EventSystem coexistence) with the user before wiring — UITK and uGUI input coexistence is the sharp edge here.
