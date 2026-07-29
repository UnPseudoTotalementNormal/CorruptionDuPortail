# Investigation: Anonymous message menu (sacoche + fin de nuit)

## Hand-off Brief

1. **What it is.** The "menu des messages" is the **anonymous-message system**: players write anonymous notes, which are batched and then revealed to everyone during the awakening recap, and archived for later browsing via the 3D sacoche. (Confirmed by code + scene wiring.)
2. **Where the case stands.** Full mechanical model mapped — write path, reveal path, archive path, and the two duplicated display components that make the sacoche panel and the night-recap look like "the same menu". Concluded (exploration case), High confidence.
3. **What's needed next.** Decide rebuild scope + target tech (Canvas vs UITK) and whether design changes touch mechanics (anonymity, timing) or just presentation. Then `gds-quick-dev` / `gds-create-story`.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | N/A (exploration, pre-rebuild) |
| Date opened      | 2026-07-16 |
| Status           | Concluded (mental model sufficient for rebuild) |
| System           | Unity 6000.2.6f2, NGO, FMOD, DOTween, UniTask; `GameScene.unity` |
| Evidence sources | Source code (`Assets/Scripts`), scene wiring (`GameScene.unity`) |

## Problem Statement

Poyo : « le menu des messages lorsqu'on clique sur la sacoche ? et aussi le même menu s'affiche lors de la fin d'une nuit pour tout le monde quelques secondes. Je veux qu'on le refasse complètement avec quelques modif de design. Pour l'instant cherche juste comment il fonctionne. »

Refined by evidence: the "sacoche menu" and the "night-end menu" are **two different components** that share the same message prefab and the same text formatting — hence they *look* identical. They are not one menu.

## System Map — the four moving parts

| Part | File | Role |
| ---- | ---- | ---- |
| **MessageManager** (data/net) | `Assets/Scripts/MessageSystem/MessageManager.cs` | Server-authoritative store. Two `NetworkList<MessageInfo>`: `messagesToReveal` (pending, current day) + `revealedMessages` (archive). Singleton (recorded §4 survivor). |
| **SendMessagePanel** (write) | `Assets/Scripts/MessageSystem/SendMessagePanel.cs` | uGUI panel to compose + send one anonymous message. |
| **AwakeningRecapMessages** (live reveal) | `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapMessages.cs` | The "fin de nuit pour tout le monde" — animates the day's messages one-by-one, then server moves them to the archive. |
| **AnonymousRevealedMessagesComponent** (archive view) | `Assets/Scripts/UI/Misc/AnonymousRevealedMessageRecap.cs` | The "clic sur la sacoche" panel — persistent scrollable list of all `revealedMessages`, grouped by day. |

`MessageInfo` struct (`MessageManager.cs:65`): `{ ulong senderClientId, FixedString512Bytes message (≤512 bytes), int day }`, `INetworkSerializable`.

## Scene wiring (GameScene.unity) — Confirmed Findings

### Finding 1 — The 3D sacoche opens the ARCHIVE (read), not the write panel

**Evidence:** `GameScene.unity:7192` `Bag3D` has `Board3DButton` (`GameScene.unity:7223-7227`) with `targetButton: {fileID: 1642275988}`. That fileID is `RevealedMessageButton` (`GameScene.unity:14272`), whose `onButtonClickedUnityEvent` → `PanelComponent.TryOpenPanel` on **RevealedMessagePanel** (`GameScene.unity:14338-14340`, target `1629860980`).

**Detail:** `Bag3D` (layer 7, under `AnonymousMessageStation`) is a 3D-interactable that forwards to an invisible logic `CustomButton` (the project's Board3DButton pattern). Clicking the bag opens **RevealedMessagePanel** = `PanelComponent` (`GameScene.unity:13881`, canvasGroup fade 0.5s) hosting the `AnonymousRevealedMessagesComponent` list. So the sacoche = **the message archive**.

### Finding 2 — Writing is a separate 2D HUD button

**Evidence:** `MessageButton` (`GameScene.unity:7330`, a uGUI `CustomButton`) → `SendMessagePanel.TryOpenPanel` (`GameScene.unity:7396-7398`). FMOD open sound `event:/Interface/Messages General + Anomaly/Menu 2` (`GameScene.unity:7378`).

**Detail:** Distinct GameObject from the bag, different HUD anchor. `TryOpenPanel` refuses if `messageLeft.Value <= 0` (`SendMessagePanel.cs:88`).

### Finding 3 — The night recap and the archive share prefab + formatting → "le même menu"

**Evidence:** Both spawn `anonymousMessagePrefab` into a layout and use identical text: header `"Messages du jour {day}:"` (underlined) + numbered `N: "content"`. See `AwakeningRecapMessages.cs:78,87` and `AnonymousRevealedMessageRecap.cs:42,44`.

**Detail:** Duplicated `SpawnNewMessageText` in both files (`AwakeningRecapMessages.cs:98`, `AnonymousRevealedMessageRecap.cs:49`). This duplication is why the two menus are visually the same and is the prime consolidation target for the rebuild.

## Data flow (end to end) — Deduced Conclusions

**Based on:** Findings 1–3 + the RPC/reveal code.

1. **Write** — `SendMessagePanel.TrySendMessageToServer` (`SendMessagePanel.cs:40`): guards `messageLeft > 0`, `!hasSentMessageThisTurn`, byte length ≤ 512 → `messageManager.SendMessageRpc(...)` (server) appends to `messagesToReveal` tagged with `Loop.currentDay` (`MessageManager.cs:48-51`). Then `OnMessageSentRpc` decrements `messageLeft` and sets `hasSentMessageThisTurn` (`SendMessagePanel.cs:66-72`). One message per turn, limited stock.
2. **Reveal (fin de nuit, everyone)** — `AwakeningRecapMessages.ShowEvent` (`:63`) copies `messagesToReveal` into a local `Stack`, then **server-only** calls `MessageManager.RevealAllMessage()` (`:141`) which pushes every pending message into `revealedMessages` and clears `messagesToReveal` (`MessageManager.cs:53-61`). Client animates the reveal: intro count line, then each message fades in and dwells `Clamp(base 2s + 0.05s/char, 3s..10s)` (`:40-44`). Total duration pre-computed by `EvaluateDuration` (`:46`) so the recap state knows how long to hold.
3. **Archive (sacoche)** — `revealedMessages.OnListChanged` (`AnonymousRevealedMessageRecap.cs:19`) → `RebuildRevealedMessagesUI` wipes and re-spawns the whole list, grouped by `day`. This panel is opened on demand by the bag and shows the running history.

**Conclusion:** `messagesToReveal` = "sent but not yet shown" (this day); `revealedMessages` = "already shown" history. The night recap is the transition between the two; the sacoche browses the history.

## Source Code Trace

| Element | Detail |
| ------- | ------ |
| Write entry | `SendMessagePanel.cs:40` `TrySendMessageToServer` (opened by `MessageButton`, HUD) |
| Reveal entry | `AwakeningRecapMessages.cs:63` `ShowEvent` (awakening recap event component) |
| Archive entry | `AnonymousRevealedMessageRecap.cs:17` `Start` subscribe / `:27` rebuild (opened by `Bag3D`→`RevealedMessagePanel`) |
| Net store | `MessageManager.cs` — `messagesToReveal`, `revealedMessages`, `SendMessageRpc`, `RevealAllMessage` |
| Data | `MessageManager.cs:65` `MessageInfo` |
| Scene hosts | `AnonymousMessageStation` (`:5466`, world) → children `Bag3D` (`:7192`); `RevealedMessagePanel` (`:13844`); `SendMessagePanel` (`:3732`); `MessageButton` (`:7330`); `RevealedMessageButton` (`:14272`); `MessageManager` (`:9158`) |

## Side Findings (matter for the rebuild)

- **`senderClientId` is stored but never displayed.** Both views show only the message text — full anonymity. The sender id rides the wire (`MessageInfo`) but no UI reads it. If a design change wants "reveal author" / partial deanonymization, the data is already there; if strict anonymity matters, note it's currently exposed on every client's `revealedMessages` replica.
- **Duplicated display logic** (Finding 3) — two `SpawnNewMessageText` + two copies of the day-grouping/format. Rebuild should unify into one presenter/prefab.
- **NGO NetworkList late-joiner duplicate bug** applies to `revealedMessages` — per memory `[[reference_ngo_networklist_sync_duplicate]]`, a late joiner can get a same-tick-added entry twice. `MessageInfo` is `IEquatable`; there is **no dedup** in the message path (unlike CharacterManager's `[CHARLIST]` guard). A rebuilt archive should dedup or the fix should be ported.
- **All Canvas/uGUI + DOTween.** Project has UITK infra (RoleCard, InfoTable via RT→RawImage). A rebuild is a candidate for UITK, but the recap timing/fade is DOTween-driven; USS transitions only approximate it (see memory `[[reference_uitk_state_screen_blocks_world_input]]`).
- **`AwakeningRecapMessages` still reads the `MessageManager.instance` singleton** directly (`:50,134,141`) — intentional §4 survivor, not a bug, but relevant if the rebuild moves it off the singleton.
- **Composition-root resolution asymmetry:** `SendMessagePanel` resolves `MessageManager` via `CompositionRoot` (`:36`), while the two display leaves still use the bare singleton — a rebuild is a chance to unify.

## Conclusion

**Confidence:** High — Confirmed by source + scene wiring; deterministic, no missing evidence.

The anonymous-message feature is three UI surfaces over one server-authoritative store. The user's "same menu in two places" is real and explained: the **sacoche opens the archive** (`RevealedMessagePanel` / `AnonymousRevealedMessagesComponent`), and the **night recap** (`AwakeningRecapMessages`) is a separate animated reveal — both render the same prefab with the same day-grouped numbered format, so they look identical. Writing is a third, separate HUD button (`SendMessagePanel`).

## Recommended Next Steps

Investigation stops at the model. For the rebuild, the decisions to settle before coding:
1. **Scope** — which surfaces change: archive panel, night recap, write panel, or all three?
2. **Tech** — stay Canvas or migrate to UITK?
3. **Mechanics vs presentation** — do the "modifs de design" touch anonymity/timing/stock (mechanics, design-owned) or only look/layout?
4. **Consolidation** — unify the duplicated display into one presenter; decide dedup for the late-joiner NetworkList bug.

Then hand to `gds-quick-dev` (small) or `gds-create-story` (tracked). Design-touching changes → confirm with Poyo first (design-owned).

## Follow-up: 2026-07-16 — Rebuild decisions (Poyo)

### Decisions locked
- **Scope:** unify **Archive (sacoche)** + **Recap de nuit** into **one shared surface**. Poyo: "les deux doivent être la même chose (c'est actuellement techniquement le cas)" — formalize today's accidental visual overlap (same prefab/format, Finding 3) into a single real component. Write panel (`SendMessagePanel`) NOT in scope for now.
- **Tech:** migrate to **UITK** (reuse RoleCard/InfoTable infra). Watch: recap timing/fade is DOTween → USS approximates poorly; drive reveal dwell via `experimental.animation`/UniTask, not USS transitions.
- **Design:** will touch **design + new features** — details TBD, Poyo will specify ("on rajoute des trucs, je t'en parle après"). Design-touching (anonymity/timing/stock) = design-owned → confirm before implementing.

### Implication for the build
One unified UITK component serving two modes over the same data + presenter:
- **Reveal mode** — night, everyone, timed one-by-one over `messagesToReveal`, then server `RevealAllMessage`.
- **Archive mode** — on-demand via sacoche, full `revealedMessages` history, day-grouped.
Kills the duplicated `SpawnNewMessageText`/format. Port/add dedup for the NGO NetworkList late-joiner bug.

### Status
Investigation **Concluded**. Awaiting Poyo's design + new-feature spec before authoring a story/spec. No code yet.

## Follow-up: 2026-07-16 #2 — Design direction (Sally + Poyo, provisionally validated)

Content confirmed: the unified surface is a **per-turn journal**, one entry per turn, each showing:
- a per-turn stat line, and
- that turn's anonymous messages.

**Per-turn stat line** (natural-language, accord singulier/pluriel):
- `{n}/{total} corrompu(s)` — number in **gold**, word `corrompu(s)` in **red** (anomaly sense).
- targeting: `Personne n'a ciblé le Robot` / `{n} joueur a ciblé le Robot` / `{n} joueurs ont ciblé le Robot` — number **gold**, `Robot` in **orange**.

**Visual direction (provisionally validated "pour l'instant"), lobby palette lane:**
- Bg near-black `rgb(10,7,8)`; primary text warm off-white `rgb(240,231,221)`; muted `rgb(164,147,138)`; hairlines `rgb(42,33,31)`/`rgb(58,44,41)`.
- Accent green `rgb(95,217,135)` (from LobbyRoles). Gold numbers ≈ `rgb(238,201,122)`. Red `rgb(219,74,86)`. Orange `rgb(226,150,58)`.
- **Diegetic, NOT dashboard:** continuous flow, NO rounded bordered cards. Turns separated by a plain hairline rule with the centered turn label (`──── TOUR 3 ────`). Ornaments (diamonds) tried then removed by Poyo — keep separators plain.
- Header = centered serif (font-voice) frontispiece title, upright (not italic), "Messages anonymes" (in-world rename still open: Le Courrier / La Sacoche / Rumeurs / none).
- Messages = slightly indented quoted lines with a muted-green envelope glyph.
- **"Write-on" feel:** the newest turn's stat + messages fade/rise in staggered, as if written live at start of day; older turns dimmed by opacity to convey history depth; panel opens already scrolled to bottom.
- **Values are provisional / design-owned** — tokenize during build (LobbyRoles is literal today), never enshrine. Poyo will add more features/feel later ("on rajoute des trucs, je t'en parle après").

Palette lane resolved: **green/faction lobby** (NOT the gold RoleCard lane the first mock used).

Interactive mockup iterations produced in-session (visualize widget), latest = `recap_par_tour_valide_mockup`. Not a code artifact.
