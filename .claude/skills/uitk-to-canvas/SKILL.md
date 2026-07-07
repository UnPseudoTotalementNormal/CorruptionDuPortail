---
name: uitk-to-canvas
description: "Convert ONE Unity UI Toolkit screen (UXML + USS + UIDocument controller) into an equivalent uGUI Canvas prefab + MonoBehaviour — close-to-faithful, then a hand-tune checklist for what can't map 1:1. Use when the user says 'convert UITK to Canvas', 'migrate this UI Toolkit screen to Canvas', 'uitk to canvas', 'passe cette UI Toolkit en Canvas', 'convertir UI Toolkit vers Canvas', 'reconvertir en Canvas', or points at a .uxml/.uss and wants a Canvas version. PREFER this over an ad-hoc manual rebuild."
---

# UI Toolkit → uGUI Canvas conversion (one screen)

**Goal:** Rebuild a single UITK screen as a uGUI Canvas prefab + a rewritten MonoBehaviour that behaves the same, landing **close** to the original visually, and hand back a precise list of the ~20% that needs human tuning. This is a semi-automated, collaborative conversion — NOT a push-button pixel-perfect port.

**Why this exists (project context):** UITK hit limitations (world-space, blur RT binding, gradients/shadows/borders, input), the game is mostly uGUI Canvas, and the team edits Canvas faster. This skill re-unifies a screen onto Canvas.

**Fidelity contract:** "close + hand-tune". Flexbox ↔ LayoutGroup is lossy; never claim pixel parity. When something can't map cleanly, FLAG it — don't fake it.

---

## Inputs to gather first

1. The screen's `*.uxml` (structure) and `*.uss` (style).
2. Any USS the UXML `<Style src>` links, PLUS the PanelSettings theme `.tss` and the token file(s) it `@import`s (e.g. `Assets/UI/Styles/variables.uss`) — resolve `var(--…)` to real values.
3. The `PanelSettings` asset the screen renders through — read its **Scale Mode** + **Reference Resolution** (drives the CanvasScaler) and render mode.
4. The controller `.cs` (the `UIDocument` consumer): every `Q<>()`, class toggle, `RegisterCallback`, and dynamic element build.
5. Where the Canvas output should live (target scene + a prefab path).

Use Unity MCP resources/tools to read assets; use `Read`/`Grep` for the text files.

---

## Mapping tables (the reusable core)

### UXML element → GameObject
| UITK | uGUI |
|---|---|
| `VisualElement` | GameObject + `RectTransform` (add `Image` only if it has a background) |
| `Label` | `TextMeshProUGUI` |
| `Button` | `Button` + `Image` (bg) + child `TextMeshProUGUI` |
| `ScrollView` | `ScrollRect` + `Viewport` (`RectMask2D` or `Mask`+`Image`) + `Content` |
| `Toggle` / `TextField` / `Slider` | uGUI `Toggle` / `TMP_InputField` / `Slider` |
| `<Style>` / theme | not a node — folded into component values |

### USS property → uGUI (the lossy part)
| USS | uGUI | Note |
|---|---|---|
| `flex-direction: row/column` | `HorizontalLayoutGroup` / `VerticalLayoutGroup` | |
| `justify-content` | LayoutGroup `childAlignment` + padding/spacing | approximate |
| `align-items` | `childAlignment` + `childControlWidth/Height`, `childForceExpand` | approximate |
| `flex-grow` / `flex-shrink` | `LayoutElement.flexibleWidth/Height` | |
| `width`/`height` px | `LayoutElement.preferredWidth/Height` (in a group) or `sizeDelta` (free) | |
| `padding` | LayoutGroup `padding` | |
| **`margin`** | ⚠️ no per-child margin in uGUI → `LayoutElement`/spacing/spacer child | **flag** |
| `background-color` | `Image.color` (+ default sprite) | |
| **`border` / `border-radius`** | ⚠️ `Outline` (border only) / a 9-slice rounded **sprite** | **flag — needs an asset** |
| `background-image` (sprite) | `Image.sprite` | |
| `color` / `font-size` / `-unity-font-style` | TMP `color` / `fontSize` / `fontStyle` | |
| `-unity-text-align` / `white-space` | TMP `alignment` / `enableWordWrapping` | |
| `-unity-font-definition` (ttf) | TMP needs a **TMP_FontAsset** (SDF), not the raw ttf | **flag** |
| `opacity` | `CanvasGroup.alpha` | |
| `position: absolute` | anchored `RectTransform`, OUTSIDE any LayoutGroup | |
| `letter-spacing` | TMP `characterSpacing` | |
| **`transition` / `translate` / `scale` / `rotate`** | ⚠️ re-author in **DOTween** in C# | **flag** |
| slim scrollbar USS | `Scrollbar` + handle sprite/color | approximate |

### Controller `UIDocument` → Canvas MonoBehaviour
| UITK | uGUI |
|---|---|
| `[SerializeField] UIDocument` + `rootVisualElement.Q<T>("x")` | `[SerializeField] T x;` per element, wired in the prefab |
| `AddToClassList/RemoveFromClassList` (a state) | `SetActive`, `CanvasGroup.alpha/interactable`, or swap color/sprite |
| `EnableInClassList(cls, cond)` | branch that applies the two visual states directly |
| `RegisterCallback<PointerDownEvent>` / click | `Button.onClick` or an `EventTrigger` |
| `schedule.Execute(...).ExecuteLater(ms)` | coroutine / `UniTask.Delay` (this project = UniTask) |
| dynamic `new Label()/VisualElement()` + `Add` | `Instantiate` a small uGUI row **prefab** into a container |
| `style.backgroundImage = sprite` | `Image.sprite = sprite` |

---

## Flow

### Step 1 — Parse
Read the uxml, the linked uss + theme tokens (resolve every `var(--…)`), the PanelSettings (scale mode + ref resolution), and the controller. Build an internal tree of (element, resolved styles, name).

### Step 2 — Produce a conversion PLAN, then HALT for approval
Output, for the user to review before any mutation:
- **Structure tree**: the target GameObject hierarchy (names mirror the UXML `name`s / classes).
- **Style map**: per node, the uGUI components + values.
- **Controller rewrite**: the new fields, the state-toggle translations, the event wiring, the dynamic-build → prefab plan.
- **⚠️ Flagged (needs human/judgment)**: everything from the "flag" rows above that this screen actually uses — margins, borders/radius, gradients, transitions, fonts, exotic flex. Propose an approach for each (e.g. "border-radius 18px → I'll use a generic rounded 9-slice sprite unless you have one").
Get the user's OK (and answers to the flags) before building.

### Step 3 — Build the Canvas prefab (Unity MCP)
- **Back up the target scene first** (see [[feedback_toolkit_scene_backup]] — copy into repo-root `BackupToolkit/`, which is gitignored).
- Create a root `Canvas` (or reuse one) with `CanvasScaler` set to the PanelSettings scale mode + reference resolution, and a `GraphicRaycaster`. For a modal overlay, mirror the UITK z-order with `Canvas.sortingOrder`.
- Build the hierarchy top-down with `manage_gameobject` (create, parent) and `manage_components` (add + set_property). Add LayoutGroups/LayoutElement/ContentSizeFitter per the style map.
- Save it as a **prefab** (`manage_asset`) so it's reusable and diff-stable.
- For dynamically-cloned rows, build the row as its own small prefab.

### Step 4 — Rewrite the controller
- Prefer `manage_script`/`create_script` for a NEW file (see [[reference_unity_silent_compile_exclusion]]); keep the same namespace/asmdef.
- Translate fields, state toggles, events, and dynamic builds per the table. This project: async = **UniTask**, audio = FMOD, never `AudioSource`.
- After writing, poll `read_console` for compile errors before continuing.

### Step 5 — Wire references
- Set every `[SerializeField]` on the prefab/scene instance via `manage_components set_property`, then **read the component back** to confirm each ref resolved (not null). Object-ref and List wiring silently goes null otherwise — see [[reference_unity_mcp_serialized_field_wiring]] and [[feedback_serialized_field_rewiring]].

### Step 6 — Verify (render-compare) + hand-tune checklist
- Capture the ORIGINAL UITK: `manage_ui render_ui` (create a throwaway scene with the UIDocument if needed; remove hidden/collapsed state classes via `modify_visual_element`; render twice in play mode; delete the throwaway scene after).
- Capture the NEW Canvas: `manage_camera` screenshot (or a Game-view capture) of the prefab instance.
- Put the two side by side, list concrete diffs (spacing, wrap, borders, color), and hand back a **hand-tune checklist** — the flagged items + any parity gaps. Be honest about what's approximate.

---

## Gotchas & project specifics
- **Font**: UITK often uses the raw `LiberationSans.ttf`; TMP needs the **SDF `TMP_FontAsset`** (`Assets/TextMesh Pro/...`). Different asset — wire the TMP one, don't point TMP at the ttf.
- **Scale**: read the PanelSettings scale mode; a mismatched CanvasScaler makes everything the wrong size. This is the #1 cause of "looks nothing alike".
- **No native border/radius/gradient in uGUI**: needs sprites. Offer a generic rounded 9-slice; the final art is design-owned — don't enshrine invented assets.
- **Blur/frost backdrop**: if the UITK screen faded a `CanvasGroup` veil (e.g. FrostCanvas), the Canvas version reuses the SAME veil — no change needed there.
- **Backup before scene mutation**; **verify every wired ref**; **compile-check after each script change**. These three are non-negotiable.
- Output is scene/prefab (GUID-wired) — slower and more fragile than text. Budget for the verify loop.

## When to refuse / escalate
- Heavy custom USS animation, complex nested `flex-grow` mixes, or bespoke UITK controls: convert the structure, then flag the behavior for manual authoring rather than guessing.
- If the screen is world-space or input-critical, confirm the interaction model (raycaster, event system) with the user before wiring.
