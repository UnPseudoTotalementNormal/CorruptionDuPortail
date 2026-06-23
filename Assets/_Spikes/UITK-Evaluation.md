# uGUI → UI Toolkit — Evaluation memo (issue #63)

> Decision record for "should we convert most/all UI to UI Toolkit?". Lives next to the throwaway spikes
> it references. Delete with them once the call is made. Branch: `claude/zen-darwin-dclrls`.

## Recommendation (BLUF)
**Don't convert wholesale. Go hybrid and phased.**

- **Phase 1 — now, low risk, real gain:** migrate the *pre-game, screen-space* flow to UITK —
  **Login, Main Menu, Lobby browser**. AI-authored UXML/USS is a genuine multiplier here and there is no
  world interaction to fight.
  - ⚠️ **Scope correction (verified 2026-06):** GameSettings and InfoTable are **diegetic** — they live on
    the in-game 3D phone/tablet (`Smartphone/Apps/Lobby/…`; `RoleAttributionSettingTab` "Phone layer"), so
    they are **world-space**, not flat. Their presentation under `Assets/UI Toolkit/Screens/` is reusable,
    but their wiring is the world-space path → **gated on Spike A**, not part of this low-risk phase.
    Almost all other in-game UI (Vote, Chat, Notes, recaps) is diegetic/world-space too.
- **Phase 2 — later, conditional:** the *world-space / diegetic* gameplay UI (cards, PowerBar, VoteCanvas,
  Smartphone, tooltips) stays on uGUI **until Spike A passes on the Unity version you actually ship**.
  Treat it as a separate decision, not part of Phase 1.
- **Never big-bang.** uGUI and UITK coexist via PanelSettings sort order. And the UI decision must **not**
  drive the 6.2 → 6.4 engine upgrade — that is its own project.

"AI makes beautiful UI" is true but orthogonal to the tech: the lever for beauty is design + iteration,
which works in uGUI too. What UITK actually buys is **maintainability + iteration speed on document UI**.

## What we found (audit, current `Dev`)
- ~8,800 LOC of UI C# (`Scripts/UI/` ≈ 4,510 + annex systems Smartphone/Note/Chat/Tooltip/Focus/Arrow/Board-UI ≈ 4,320).
- 37 Canvas prefabs / 73 total prefabs.
- 139 DOTween animation calls across 36 files, tightly woven into the UI.
- **0** UITK at runtime today (all `.uxml`/`.uss` are third-party/editor tooling).
- Core gameplay UI is **diegetic / world-space**, driven by a first-person reticle
  (`ReticleInteractor` → `EventSystem.RaycastAll` → `GraphicRaycaster`).
- uGUI-specific deps: UISoftMask, TMP sprite assets, DOTween-UI module.

## The two spikes (how to read them)
Run both in the editor on the current 6.2 — no engine upgrade needed to get the answers.

- **Spike A — `Assets/_Spikes/UITKWorldSpace/`** (commit `7999d62`). Tests whether the screen-centre reticle
  can hover/confirm a **world-space UITK panel** via `RuntimePanelUtils.CameraTransformWorldToPanel` →
  `IPanel.Pick`. Decisive test = the hover highlight tracks the reticle exactly.
  - ✅ highlight is accurate → Phase 2 (world-space migration) is technically viable.
  - ❌ offset / mirrored / misses → keep gameplay UI on uGUI, revisit at 6.7 LTS.
- **Spike B — `Assets/_Spikes/MenuPilot/`** (commit `6bf31d2`). A clickable UITK rebuild of Main Menu + Lobby
  (mock data, no backend). Judge: look vs the current Canvas menu, USS iteration speed, and the data-driven
  `ListView` table vs hand-synced `LobbyEntryUI` prefabs.
  - ✅ nicer + faster to iterate → green-light Phase 1.

Record a one-line verdict + screenshot on each issue #63 checklist item.

## World-space UITK — status & timeline (verified, Jun 2026)
- Introduced 6.2 ("runtime UI in 3D space with sizing/transforms/interaction"), expanded in 6.3.
- **Not declared production-ready** in Unity's Nov 2025 roadmap; bigger world-space work (scene-view
  authoring, advanced animation) targeted for **6.7 LTS**.
- **Reticle-relevant caveat:** world-space raycast → panel pick is the still-rough part — open bug on 6.3
  (Jan 2026): inverted/unscaled coords, pivot-dependent picking. Hence Spike A: validate, don't assume.

## What a migration actually costs (beyond writing markup)
AI lowers the *markup* cost, not these:
- **DOTween ×139** → re-authored as USS transitions / `experimental.animation`. Re-tuning *feel*, not typing.
- **Reticle interaction** rebuilt: UITK panels are outside the EventSystem/GraphicRaycaster pipeline, so the
  existing reticle can't see them — they need the separate pick bridge (the Spike A path).
- **Re-validating networked, server-authoritative UI flows** — testing burden, techno-independent.
- **Engine 6.2 → 6.4 upgrade** is its own project (NGO/FMOD/Facepunch/URP/DOTween re-validation).
- Saving in the other direction: **UISoftMask becomes unnecessary** (UITK masking is native).

## Open decisions (yours)
1. Green-light **Phase 1** (screen-space subset) now? (Spike B informs this.)
2. Target Unity version + timing for any **Phase 2** world-space migration. (Spike A + the 6.7 timeline inform this.)

## Sources
- World Space UI — Unity Manual (6000.4): https://docs.unity3d.com/6000.4/Documentation/Manual/ui-systems/world-space-ui.html
- UI Toolkit dev status, Nov 2025: https://discussions.unity.com/t/ui-toolkit-development-status-and-next-milestones-november-2025/1698009
- WorldSpace raycasting not working as expected (Jan 2026): https://discussions.unity.com/t/worldspace-ui-document-raycasting-not-working-as-expected/1703035
