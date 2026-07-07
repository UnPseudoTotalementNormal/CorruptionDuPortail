# Investigation: CharacterBar cards collapse to center instead of spaced groups

## Hand-off Brief

1. **What happened.** On some games the whole CharactersBar subtree (faction titles + character cards) piles up overlapping at the bar's center instead of laying out as spaced faction groups — the layout collapse is set at build time and persists the entire game.
2. **Where the case stands.** Deduced root cause: the bar is populated once at runtime (GameIntroductionState) into nested HorizontalLayoutGroups on a **world-space canvas**, with each leaf carrying its **own nested Canvas** (a LayoutGroup-rebuild boundary), and `ResetCharactersBar` never forces a layout rebuild — so on frames where Unity's auto rebuild is missed/mis-registered the groups stay at origin. Not yet reproduced deterministically.
3. **What's needed next.** Add a `LayoutRebuilder.ForceRebuildLayoutImmediate(charactersBarParent)` at the end of `ResetCharactersBar` (and give the leaf prefab a `LayoutElement` so measurement is Canvas-independent); a trivial, low-risk fix — route to `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-07                                                                 |
| Status           | Active                                                                     |
| System           | Unity 6000.2.6f2, in-editor / player; world-space board canvas             |
| Evidence sources | Source code, prefabs, git history, two user screenshots (bug vs. normal)   |

## Problem Statement

User: "pourquoi ils sont tous au même endroit là sur la character bar ? normalement ils sont espacés (comme sur l'image 2)". Image 1 (bug): the CHOSEN/ANOMALY/MARGINAL faction titles and the character cards under them all overlap in a narrow centered pile. Image 2 (normal): the same three faction groups are spread horizontally with clear spacing.

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| `CharactersBar.cs` | Available | Build/layout logic; sets HLG spacing, instantiates group + leaf objects |
| `FactionGroupPrefab.prefab` | Available | VerticalLayoutGroup(root)+ContentSizeFitter(Preferred) / CharactersContainer HorizontalLayoutGroup + ContentSizeFitter(Unconstrained) |
| `CharacterBarObject.prefab` | Available | Leaf 75×75, carries its own **Canvas** (nested), no LayoutElement |
| Git history | Available | Hover feature 99a6612 touched prefab+script but did NOT add/remove the Canvas |
| Two screenshots | Available | Bug vs. expected — both faction titles AND cards collapse in the bug shot |
| Live repro / Unity console at failure | Missing | Not captured; would confirm the missing-rebuild frame |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | Confirm `charactersBarParent` canvas is world-space (render mode 2) | Medium | Open | Scene has 3 world-space canvases; the bar lives on the board (world-space) — not byte-confirmed in scene YAML yet |
| 2 | Reproduce by toggling the bar's GameObject active during build | Medium | Open | Would prove the missed auto-rebuild path |
| 3 | Check whether GameIntroductionState activates the bar / its canvas the same frame as `ResetCharactersBar` | Medium | Open | Instantiating into an inactive/just-activated subtree is a classic missed-rebuild trigger |

## Timeline of Events

| Time | Event | Source | Confidence |
| ---- | ----- | ------ | ---------- |
| Game start | `GameIntroductionState.OnEnter` calls `ResetCharactersBar(GetCharacters())` — bar built **once** | `GameIntroductionState.cs:65` | Confirmed |
| Same frame | Groups + leaves instantiated into HLGs; no forced rebuild issued | `CharactersBar.cs:129-240` | Confirmed |
| Render | If auto layout rebuild missed/mis-registered → subtree stays at origin, collapsed; persists (no later rebuild) | Deduced | Deduced |
| Awakening (screenshot) | User observes the collapsed bar; state never rebuilds it | `LobbyState.cs:81` only *destroys* it | Confirmed |

## Confirmed Findings

### Finding 1: The bar is built exactly once, at game intro, and never rebuilt in-game

**Evidence:** `Assets/Scripts/GameLogic/GameStates/GameIntroductionState.cs:65` is the only `ResetCharactersBar` caller; the only other lifecycle call is `DestroyCharactersBar` at `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:81`.

**Detail:** Whatever layout state results from the single build persists for the whole match. This explains why the collapse is stable within a game (not a per-frame flicker) yet varies between games.

### Finding 2: Nested HorizontalLayoutGroups drive all spacing, with no forced rebuild

**Evidence:** `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs:136-139` (parent HLG spacing = interGroupSpacing), `:199-202` (per-group CharactersContainer HLG spacing = intraGroupSpacing), `:220` (leaves instantiated into the container). Grep for `ForceRebuildLayout`/`LayoutRebuilder` across `Assets/Scripts` returns only ChatPanel and TooltipManager — **nothing in the CharacterBar path**.

**Detail:** Both spacing levels (group-to-group and card-to-card) depend on Unity's automatic layout rebuild firing correctly after runtime instantiation. In the bug screenshot BOTH levels collapse (titles overlap AND cards overlap) → the failure is a whole-subtree rebuild miss, not a single container.

### Finding 3: Every leaf card carries its own nested Canvas

**Evidence:** `Assets/Prefabs/CharacterBarObject.prefab` root (`m_Name: CharacterBarObject`, 75×75) contains a `Canvas` component (prefab line ~259); `CharactersBarObject.cs:25,94,107` uses it (`canvasObject.sortingOrder += 1` on hover). No `LayoutElement` on the root.

**Detail:** A nested Canvas is a rendering + layout-rebuild boundary in Unity UGUI. A LayoutGroup whose children each own a Canvas is a well-known source of "children not laid out / measured" glitches, because the layout rebuild registration keys off the nearest Canvas.

### Finding 4: The Canvas is not a new regression from the hover feature

**Evidence:** `git show 99a6612 -- Assets/Prefabs/CharacterBarObject.prefab` shows 0 added and 0 removed `Canvas:` components; it added `VisualWrapper`/`hoverVisual` and removed the old `hoverScale` approach.

**Detail:** Rules out "the hover commit introduced the nested Canvas." The collapse is a **latent timing bug** in how the bar is built, not a fresh structural change — consistent with it surfacing intermittently rather than every game.

## Deduced Conclusions

### Deduction 1: Intermittent whole-subtree layout collapse = missed auto layout rebuild at build time

**Based on:** Findings 1–3.

**Reasoning:** Leaf widths are fixed (75, ContentSizeFitter on the container is Unconstrained and ChildControlWidth=0), so child *sizes* are valid the instant they're instantiated — the collapse is not a sizing/zero-width problem. What varies frame-to-frame is whether Unity's `CanvasUpdateRegistry` layout pass actually runs for this subtree before it renders. With (a) a one-shot runtime populate during a state transition, (b) a world-space canvas, and (c) a nested Canvas on every leaf acting as a rebuild boundary, the automatic rebuild can be skipped or mis-registered, leaving every RectTransform at its local origin. The parent bar HLG then has nothing laid out → the whole assembly sits as one overlapping pile centered in the bar (childAlignment/anchoring puts the un-laid-out stack mid-bar).

**Conclusion:** The root cause is a missing explicit layout rebuild after `ResetCharactersBar` populates the bar, made fragile by the nested-Canvas-per-leaf structure and the world-space canvas. Because the bar is built only once, a single missed rebuild persists all game.

## Hypothesized Paths

### Hypothesis 1: The subtree (or its canvas) is inactive/just-activated when `ResetCharactersBar` runs

**Status:** Open

**Theory:** GameIntroductionState instantiates the bar children while the bar's GameObject or its canvas is inactive or being activated the same frame; objects instantiated under an inactive parent don't get an auto layout pass, and activation doesn't always re-dirty a LayoutGroup that was populated while inactive.

**Supporting indicators:** Build happens inside a state transition (`GameIntroductionState`); one-shot; persists.

**Would confirm:** Log `charactersBarParent.gameObject.activeInHierarchy` and canvas `isActiveAndEnabled` at the top of `ResetCharactersBar`, plus `charactersBarParent.GetComponent<RectTransform>().rect` of a leaf one frame later.

**Would refute:** Bar is active and a rebuild is registered yet layout still collapses → points to the nested-Canvas registration path instead.

### Hypothesis 2: Nested-Canvas rebuild boundary swallows the LayoutGroup rebuild

**Status:** Open

**Theory:** Each leaf's Canvas causes the auto rebuild to register on the leaf canvas rather than propagate to the parent HLGs, so the groups are never positioned on the flaky frames.

**Supporting indicators:** Finding 3; both HLG levels fail together.

**Would confirm:** Temporarily disable/remove the leaf Canvas (or add a `LayoutElement` to the leaf) and observe the collapse disappear across many game starts.

**Would refute:** Collapse still occurs with the leaf Canvas removed.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| Live repro on a collapsed frame | Confirms which frame/rebuild is missed | Add `[BARLAYOUT]` logs in `ResetCharactersBar` (active state, leaf `anchoredPosition` next frame), play until it collapses |
| World-space confirmation of `charactersBarParent`'s canvas | Confirms the world-space aggravating factor | Inspect the bar's canvas render mode in GameScene / MCP `manage_gameobject` |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs:129` `ResetCharactersBar` — builds subtree, issues no forced layout rebuild |
| Trigger       | `Assets/Scripts/GameLogic/GameStates/GameIntroductionState.cs:65` (one-shot at game start) |
| Condition     | Auto layout rebuild for the runtime-populated nested HLGs missed/mis-registered on a world-space canvas with nested-Canvas leaves → RectTransforms stay at origin, whole bar overlaps centered |
| Related files | `Assets/Prefabs/CharacterBar/FactionGroupPrefab.prefab`, `Assets/Prefabs/CharacterBarObject.prefab`, `Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs` |

## Conclusion

**Confidence:** Medium (Deduced; deterministic repro not yet captured, and the two contributing mechanisms — missed rebuild vs. nested-Canvas boundary — aren't yet separated).

The evidence shows the CharactersBar is built once at game intro into two levels of `HorizontalLayoutGroup` with **no explicit layout rebuild**, on a world-space canvas, with each leaf carrying its own nested `Canvas`. Leaf sizes are always valid, so the collapse is a **missed/mis-registered automatic layout rebuild**, not a zero-size problem — the whole subtree stays at its origin and renders as an overlapping centered pile. Because the bar is never rebuilt in-game, one bad frame persists the entire match, which matches the intermittent-between-games / stable-within-a-game symptom. The nested Canvas is pre-existing (not the hover commit), i.e. a latent timing bug.

## Recommended Next Steps

### Fix direction

Primary (mechanism: force the rebuild): at the end of `ResetCharactersBar`, after all groups/leaves are instantiated, call `UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(charactersBarParent as RectTransform)`. Because layout must resolve bottom-up through the nested containers, rebuild the leaf containers first (or simply call it on the parent, which recurses). If the subtree may be inactive at build time, defer one frame (`await UniTask.Yield()` / `UniTask.NextFrame()`) before the forced rebuild.

Secondary (mechanism: make measurement Canvas-independent): add a `LayoutElement` (preferredWidth/Height = 75) to the `CharacterBarObject` prefab root so the HLGs measure it without depending on the nested Canvas's rebuild path.

Investigation stops at diagnosis; these are the fix directions, not applied.

### Diagnostic

Add `[BARLAYOUT]` logs in `ResetCharactersBar`: log `charactersBarParent.gameObject.activeInHierarchy` and the canvas enabled/render-mode at entry, and one frame later log the `anchoredPosition` of the first leaf in each group. A collapsed run shows all leaves at ~(0,0). This separates Hypothesis 1 (inactive-at-build) from Hypothesis 2 (nested-Canvas registration).

## Reproduction Plan

Start games repeatedly from lobby → GameIntroduction with ≥3 factions across ≥2 awakening layers (to force multiple groups). Expected good: three spaced faction groups. Bug: titles + cards overlap centered. If flaky, artificially delay/deactivate the bar's canvas for one frame around the `ResetCharactersBar` call to reproduce on demand, then verify the forced-rebuild fix removes it across many starts.

## Side Findings

- `CharactersContainer` HLG has `ChildControlWidth=0` + `ChildForceExpandWidth=1` and its ContentSizeFitter is Unconstrained (`FactionGroupPrefab.prefab`); relies entirely on the fixed 75px leaf width. Works, but brittle if leaf size ever changes — a `LayoutElement` on the leaf would harden it. (Deduced from prefab YAML.)
