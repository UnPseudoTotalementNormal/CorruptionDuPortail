# Spike B — Main Menu + Lobby pilot in UI Toolkit (issue #63)

**Throwaway, self-contained** (own asmdef, no `Game` dependency, **no live Facepunch / Unity Lobbies**).
The lobby list is **mock data**, so it runs in any scene without Steam, auth, or the network stack. Delete
the folder when you've made the call. Runs on the project's current **Unity 6.2**.

## What it is
A clickable UITK rebuild of the existing flow — `UI.MainMenu`, `UI.Lobby.LobbySelectionPanel`,
`UI.LobbyUI.LobbyEntryUI` — so you can feel the AI-authored UXML/USS workflow vs the current Canvas one.

Screens: **Main menu** (Host / Join / Settings / Quit) → **Host** (lobby name ≥ 3 chars enables the button,
optional password) → **Browser** (data-driven `ListView`: Nom · Privé · Langue · Joueurs, refresh,
select-to-highlight, Connect) → **password modal** + **loading overlay** (fake async via the UITK scheduler).

The browser is the headline: the lobby table is the same shape as `InfoTable` / `TableSystem`, the subset
#63 flags as the highest-value place to adopt UITK.

## Setup (≈3 min, throwaway scene)
1. `Assets > Create > UI Toolkit > Panel Settings` — leave **Render Mode = Screen Space Overlay** (default).
   (This is also the "coexists with uGUI via sort order" check from #63: give it a Sort Order above/below
   your Canvas to layer them.)
2. New empty GameObject → add **UI Document** (`Add Component > UI Toolkit > UI Document`). Assign
   **Source Asset** = `MenuPilot.uxml`, **Panel Settings** = the one from step 1.
3. Add **`MenuPilotController`** to the same GameObject. (Optional: drag `MenuPilot.uss` into its
   *Style Sheet* field if the panel renders unstyled.)
4. Press **Play** and click through it. Then open `MenuPilot.uxml` / `MenuPilot.uss` in **UI Builder** and
   restyle live — that live-edit loop is the thing to evaluate.

## What to judge (the actual point of the spike)
- **Look**: is the AI-authored result already nicer than the current Canvas menu?
- **Iteration speed**: editing USS (here, or with me) vs wiring RectTransforms/anchors/prefabs by hand.
- **The table**: `ListView` virtualization + `:hover`/selection states vs instantiating `LobbyEntryUI`
  prefabs and hand-syncing column widths to headers (`LobbySelectionPanel.CreateEntry`).

Jot the verdict on issue #63, Spike B checklist.

## Honest scope
- No backend: "Lancer le salon" / "Connecter" just show the loading overlay and log. Wiring to the real
  `LobbyManager` / `NetworkManager` is mechanical and deliberately out of scope.
- Says nothing about world-space (that's Spike A) or the DOTween/animation re-authoring cost (tracked in #63).
- I authored this without a running editor (no Unity in the remote env), so **check the Console for compile
  errors first**; the visual polish is meant to be tuned in UI Builder.
