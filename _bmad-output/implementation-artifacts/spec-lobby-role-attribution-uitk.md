---
title: 'Lobby role-attribution rebuilt in UI Toolkit + max/forced role model (RT→RawImage tablet app)'
type: 'feature'
created: '2026-07-15'
status: 'approved'
approved: '2026-07-15'
baseline_commit: 'cab2aef8'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-infotable-uitk-rt.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-menu-on-tablet-app.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-gamesettings-refonte-networked-backbone.md'
  - '{project-root}/_bmad-output/implementation-artifacts/ui-toolkit-style-guardrails.md'
design_reference: 'VISUAL/layout reference (reproduce the LOOK & interaction, NOT the code — the proto JS is throwaway): {project-root}/_bmad-output/implementation-artifacts/proto/role-select-proto.html  ·  hosted: https://claude.ai/code/artifact/91ee0ded-eb55-4799-9832-1a8392aff168'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The tablet Lobby app (`GameSettingsPanel.prefab`, a world-space uGUI `SmartphoneApp`) presents role attribution as a flat scroll list — one **slider** per role for its count, plus a `canBeFake` checkbox. A slider is the wrong instrument for a small integer, the screen is text-only (no role recognition), the "Autres options…" tab is empty, there is no live composition feedback, and there is no fast path for a new host to reach a balanced game. Poyo wants the **whole tablet Lobby app rebuilt in UI Toolkit** (the InfoTable treatment), and — in the same pass — the per-role data model changed from `count + canBeFake(bool)` to **`max + forced(int)`**.

**Two coupled changes:**
- **(A) Presentation — UITK rebuild.** Rebuild the app content in UITK, rendered by the proven **Path B** bridge (screen-space panel → `RenderTexture` → uGUI `RawImage`) inside the existing tablet `ScreenCanvas`, exactly like `InfoTableRtPresenter`. Carousel, stencil mask, `PhoneCamera`, `SmartphoneApp` container reused unchanged — only the app *content* becomes UITK. Card grid grouped by faction (5:7 cards, ratio `0.7159` from `Card.prefab`), **two steppers per card**, live composition tally, a **preset system** (classique one-click + player-count-filtered list — the Discord task *« Sélections de rôle en lobby – recommandation »*), a **"Ta partie"** tab (the SAME card grid, filtered to roles with `max > 0` — not a distinct list), and a **gated "Démarrer"** hosted on the tablet.
- **(B) Mechanic — `count + canBeFake` → `max + forced`.** Each role gets **two integers**: `max` (pool cap — random draw fills up to this) and `forced` (guaranteed minimum — this many real players are guaranteed this role). Constraint `forced ≤ max`. This *subsumes* the old `canBeFake` bool: a role's **fakeable capacity is derived** = `max − forced` (the copies not locked to real players). `forced = 0` ≡ old `canBeFake = true` (whole pool fakeable); `forced = max` ≡ old `canBeFake = false` (mandatory, 0 fakeable). Total fakes stay the surplus `Σmax − players`, drawn from the `max − forced` capacity. The mandatory floor becomes `Σforced`.

Build & validate the mechanic change headless first (golden-master re-pin), then build the UITK app in a Play harness (demo data source), then port into `GameScene`, replacing the uGUI panel content.

## Locked decisions (this session)
1. **"Démarrer" lives on the tablet** (moved from the HUD; the modular lobby makes this a reparent — `LobbyStartButton` hosts on the tablet app). The authoritative gate stays in `LobbyState`.
2. **Card tap → reuse the existing screen-space `RoleCard` overlay** (`RoleCardController.Open(role)`). No tablet-embedded detail.
3. **"Autres options…" tab = empty for now** (keep the tab, no content).
4. **Model = `max + forced`** (see B); `forced` replaces `canBeFake`; `count = max`, `forced = min`, `forced ≤ max`.

**Design answers locked (Poyo, party-mode round 1):**
5. **`forced` is per-role only — FOR NOW.** The gate stays scalar (`Σforced ≤ players ≤ Σmax`); no per-faction/camp floor. (May be revisited later — do not build a faction-floor DTO now.)
6. **Fake selection = random.** When the fake budget (`Σmax − players`) is smaller than the total fakeable capacity (`Σ(max − forced)`), which roles become decoys is drawn **at random** from the fakeable subset — this is already `RoleDistributor.AvailableFake`'s behaviour. No weighting.
7. **No global fake cap; a role appearing ONLY as a fake is intended** (a role present in nobody's real hand is core to the bluff — "c'est le principe du random").
8. **No camp-ratio guardrail / no extra validity rules for now** ("on verra à la fin"). Only the scalar gate.
9. **Start gate is re-validated server-side.** The tablet Start button is a client-side UITK control: tap → `ServerRpc` (via `GetSafeRpcTarget`) → the server **re-checks** `Σforced ≤ players ≤ Σmax` before transitioning. The client-side disabled state is UX only; never trust client validity (the client's NetworkList can be a tick behind).
10. **Failure is readable.** When Start is gated off, the UI shows the reason AND points to what to fix — e.g. "6 rôles garantis pour 5 joueurs" highlighting the roles carrying `forced` (Samus's UX point). A bare greyed button is a rage-trap.

## Boundaries & Constraints

**Always:**
- **Server authority preserved.** Views read via `GameSettingsManager` and write via its request API (host writes directly; non-host is a read-only mirror — `_allowClientEditing` stays `false`). Replication stays auto-synced NetworkList state (bot-safe by construction, no manual `ClientRpc` target).
- **Mechanic change is behaviour-*redefining*, golden-master intentionally re-pinned.** The distribution now reserves `forced` reals per role before the existing surplus-driven fake/real draw. `RoleAssignmentGoldenMasterTests` / `RolePoolOrderingTests` are updated to the new golden (with `forced = 0` everywhere the new path must reproduce the OLD result byte-for-byte — that equivalence is a required test). The pure `RoleDistributor` stays decision-only (NFR4): no NGO, no `RoleDataObject`, no `Random`.
- **`forced` subsumes `canBeFake`.** Fakeable capacity per role = `max − forced` (the fake loop draws from roles with residual pool after forced reals). Do NOT keep a separate `canBeFake` field.
- **Reuse the InfoTable RT bridge verbatim.** New presenter mirrors `InfoTableRtPresenter`: clone `PanelSettings` (never mutate a shared asset), runtime RT (~1440×912, match tablet aspect), `panelSettings.targetTexture = RT`, `RawImage.texture = RT`, `SetScreenToPanelSpaceFunction` via **PhoneCamera**, **no Y-flip**, gate on `_ownerApp.IsOpen` (NaN off-center → no phantom clicks).
- **GameScene `EventSystem` needs `PanelInputConfiguration`** covering the new PanelSettings (RT panels are off-screen and must be told to redirect screen→panel; RoleCard overlay did not need it).
- **Follow `RoleCardController` UITK conventions:** `[RequireComponent(UIDocument)]`, guarded `TryInitialize()` in OnEnable+Start, `Q<>` by name, BEM classes, dynamic children in C#, `var(--cdp-*)` tokens.
- **Faction grouping is data-driven** (`FactionType {anomaly, chosen, marginal, unknown}`): render every non-`unknown` faction that has roles; labels/colours/icons from the wired `FactionDatabase` SO. Never hardcode 2 sections or invent colours (`marginal` is a real faction).
- **Role list source = `RoleAttributionState.roleAttributionDictionary`** keys (each `RoleDataObject → .role`), the same enumeration the current tab uses. Per-role `max`/`forced` via the manager.
- **Two steppers per card, constraint `forced ≤ max`.** Lowering `max` below `forced` clamps `forced` down. One visible number per stepper.
- **Palette/sizes PLACEHOLDER, design-owned.** Add `--cdp-*` tokens, keep provisional. Faction colours flow from `FactionDatabase`, not new USS tokens.
- **Before mutating any scene/prefab**, copy the original into repo-root `BackupToolkit/` (gitignored).

**Ask First:**
- Retiring vs. disabling the old uGUI widgets (`RoleAttributionSettingObject`/`RoleAttributionSettingTab`) and the HUD Start button once the tablet app ships.
- Any `RoleDistributor` behaviour beyond the reserve-forced-then-existing-draw (e.g. changing the fake/real ordering) — default = minimal: reserve forced reals, then the current fake(surplus)-then-real loop on the residual pool.
- The `MaxRoleCount = 15` clamp vs. the `[Range(0,10)]` mismatch (pre-existing; align while here?).
- Enshrining any colour/size as final.

**Never:**
- Do not change the random-draw core of `RoleDistributor` beyond adding the forced-reservation pass (the fake-then-real depleting-pool draw stays; only a reservation is prepended).
- Do not mutate a ScriptableObject at runtime (the refonte killed the `role = …` write — keep it dead).
- Do not mutate the shared `PS_ScreenOverlay` `targetTexture` (hijacks RoleCard) — new dedicated `PanelSettings` asset (mirror `PS_InfoTable_RT`).
- Native world-space UITK (Path A). Root `pickingMode = Position` on a fullscreen element (project trap — blocks world input; pick per-element).
- `AudioSource` (FMOD), `System.Threading.Tasks.Task` (UniTask), owner-write NetworkVariables.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Build grid | Lobby entered, manager ready, role pool from `RoleAttributionState` | One faction section per non-`unknown` faction present; 5:7 cards; `max` read from manager; card dimmed at `max=0`, lit + faction glow at `max≥1`; gold "★ forcé N" tag when `forced>0` | Manager not ready → deferred build (`UniTask.WaitUntil`, cancellation-guarded), no throw |
| Max stepper | Host taps Max `±` | `RequestSetMax(id, n)` (clamped `0..MaxRoleCount`); replicates; refresh on `OnSettingsChanged` | Non-host inert (read-only mirror) |
| Forced stepper | Host taps Forcé `±` | `RequestSetForced(id, n)` clamped `0..max`; `+` disabled at `forced==max` | Non-host inert |
| Lower Max below Forced | Max stepper `−` when `forced==max` | `max` drops AND `forced` clamps to the new `max` (single coherent state) | N/A |
| Card / `i` tap | Host taps card face (not a stepper) | `RoleCardController.Open(role)` overlay | Stepper taps `stopPropagation` — never open detail |
| Live tally | Any change | Recompute: players · `Σforced` · `Σmax` · per-faction (by `max`); notes show surplus (`Σmax−players`) / shortfall | `Σforced>players` or `Σmax<players` flagged red |
| Preset classique | Host taps "Preset classique" | Apply the `isClassic` preset for the connected-player count (sets `max` + `forced` per entry) via the manager; toast | No classic for N → button disabled/hidden |
| Other presets | Host opens "Autres presets…" | Popover lists presets where `playerCount == connected`, "Recommandé" on classic; **hidden when only the classic exists** | Empty → opener not shown |
| Start gating | `Σforced`, `Σmax` vs players | Start enabled iff `Σforced ≤ players ≤ Σmax`; else disabled + reason ("pool trop petit" / "trop de rôles forcés") | Mirrors the updated `LobbyState.OnStartGameButtonPressed` guards exactly |
| Distribution (server) | Game start with per-role `max`/`forced` | Reserve `forced[i]` reals per role; then fakes=`Σmax−players` drawn from residual (`max−forced`) fakeable subset; then remaining reals=`players−Σforced` from residual | Golden-pinned; `forced=0` everywhere ⇒ byte-identical to the OLD distribution |
| Player count changes | Join/leave in lobby | Tally `players` updates; presets re-filter; gating re-evaluates | Read-only source; never editable on tablet |
| Ta partie tab | Any selection | The SAME card grid as Attribution (identical cards + steppers), filtered to roles with `max>0` — a view filter, not a distinct layout; tab badge = distinct roles in pool | Empty → empty-state copy |
| Click while app off-center | Pointer where the slid-off RawImage would map | `ScreenToPanel` → NaN (gated on `IsOpen` + rect) → no phantom clicks | N/A |
| Late joiner mid-lobby | Client connects after edits | Snapshot replication → grid shows canonical `max`/`forced` | N/A |

</frozen-after-approval>

## Mechanic change — `max + forced` (Part B, do this FIRST)

**Data model:**
- `RoleAttributionSetting` (authoring SO, `RoleAttributionState.cs`): `roleToAttribute:int [Range]` → keep as **`max`**; `canBeFake:bool` → **`forced:int`** (guaranteed minimum). Migrate authored assets (see Ask-First on the range clamp).
- `RoleSettingEntry` (replicated DTO, `GameSettingsManager.cs`): `{roleId, count, canBeFake}` → `{roleId, max, forced}` (all unmanaged → NetworkList constraint still satisfied; update `NetworkSerialize`/`Equals`/`GetHashCode`).
- Seed: `SeedFromAuthoredDefaults` copies `max`/`forced` from the authored dict.

**`GameSettingsManager` API:**
- Read: `GetMax(RoleID)`, `GetForced(RoleID)`, `GetTotalMax()` (Σmax), `GetTotalForced()` (Σforced — the new mandatory floor, replaces `GetMandatoryRoleCount`).
- Write (host-auth, clamped): `RequestSetMax(id, n)` (`0..MaxRoleCount`, and re-clamp that role's `forced ≤ new max`), `RequestSetForced(id, n)` (`0..GetMax(id)`).
- `RequestApplyPreset(RolePreset)` — host-only, zero all then set `max`+`forced` per entry (single replicated batch).

**`RoleDistributor` (pure):** signature gains `forced` per role. Algorithm:
1. Reserve: for each role in frozen order, assign `forced[i]` reals (append to `realIndices`, `remaining[i] -= forced[i]`). Guard `Σforced ≤ realCount`.
2. Then the EXISTING two-loop draw on `remaining`: fake loop draws `fakeCount = Σmax − players` from `remaining>0` (the residual = `max−forced` fakeable subset), then real loop draws the leftover `realCount − Σforced`.
- **Equivalence test (required):** with `forced=0` for every role the output is byte-identical to the pre-change distribution under the same seed/counts.

**`RoleAttributionState.OnStartStateServer`:** read `max`/`forced` from the manager (fallback to authored for the headless harness); pass `forced` list into `Distribute`; `_totalRolesToAttribute` = `GetTotalMax()`.

**`LobbyState.OnStartGameButtonPressed`:** gate becomes `GetTotalForced() ≤ players ≤ GetTotalMax()` (was `mandatory ≤ players ≤ total`). `LobbyStateStartGuardTests` updated.

## Code Map

**Reuse / read:**
- `Assets/Scripts/UI/InfoTable/InfoTableRtPresenter.cs` — THE RT→RawImage bridge to replicate. `InfoTableUitkController.cs` — dynamic-grid + `FactionDatabase` precedent.
- `Assets/Scripts/UI/RoleCard/RoleCardController.cs` — UITK convention template + the detail overlay reused on card tap (`Open(Role)`); its `Bind`/`Build*` methods are the extraction source for the reusable card face.
- `Assets/Scripts/Smartphone/{SmartphoneApp,SmartphoneController}.cs` + `Apps/Lobby/{LobbyAppPresenter,LobbyStartButton}.cs` — carousel + Lobby app driver + Start (Start moves onto the tablet).
- `Assets/Scripts/Characters/{Role,RoleDataObject,FactionDatabase,PortraitTable,RoleID}.cs`; `Assets/Scripts/Domain/FactionType.cs`.
- `Assets/UI/{Styles/variables.uss,Styles/theme.tss,PanelSettings/PS_InfoTable_RT.asset}`.

**Mutate (Part B — mechanic):**
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` — `RoleAttributionSetting` fields (`max`/`forced`); `OnStartStateServer` passes `forced`.
- `Assets/Scripts/GameLogic/GameSettings/GameSettingsManager.cs` — `RoleSettingEntry` DTO + seed + read/write API (max/forced) + preset apply.
- `Assets/Scripts/Domain/RoleDistributor.cs` — forced-reservation pass.
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — gate on `Σforced`/`Σmax`.
- `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs`, `Tests/Editor/RoleDistributorTests.cs`, `Tests/PlayMode/.../LobbyStateStartGuardTests.cs`, `GameSettingsManagerTests.cs` — re-pin + add the `forced=0` equivalence + forced-reservation cases.
- Authored `RoleAttributionState` `.asset` — migrate `canBeFake`→`forced` values (design-owned: which roles get a forced minimum).

**Mutate (Part A — UITK):**
- `Assets/Prefabs/GameSettings/GameSettingsPanel.prefab` — replace uGUI content with RawImage + UITK document stack (mirror InfoTable). Stays a `SmartphoneApp`. Host the tablet `LobbyStartButton`.
- `Assets/Scenes/GameScene.unity` — wire document/presenter/data-source refs; verify `EventSystem` `PanelInputConfiguration` covers the new PanelSettings.

**New (Part A — confirm paths on build):**
- `Assets/UI/Screens/LobbyRoles/LobbyRoles.uxml` + `.uss`; `RoleGridCard.uxml` (+ reusable builder factored from RoleCard).
- `Assets/Scripts/UI/LobbyRoles/{LobbyRolesUitkController,LobbyRolesRtPresenter,ILobbyRolesDataSource,GameLobbyRolesDataSource,DemoLobbyRolesDataSource}.cs`.
- `Assets/UI/PanelSettings/PS_LobbyRoles_RT.asset` (mirror `PS_InfoTable_RT`; never the shared overlay).
- Presets (design-owned content): `Assets/Scripts/GameLogic/GameSettings/{RolePreset,RolePresetDatabase}.cs` (SOs) + authored `.asset`s under `Assets/GameData/Presets/`.

## Data binding
Each card binds one `RoleDataObject`: name→`role.roleName`; faction→`role.factionType`→section+tint/icon via `FactionDatabase.Get`; portrait→`PortraitTable.Get(role.rolePortrait)`; `max`→`GetMax(roleID)` / `RequestSetMax`; `forced`→`GetForced(roleID)` / `RequestSetForced`; detail(tap)→`RoleCardController.Open(role)`. Tally: `players` = connected-player count (read-only); `Σmax`, `Σforced`, per-faction Σmax; surplus = `max(0, Σmax − players)`.

## Preset system (Discord task « recommandation »)
- `RolePreset` SO: `displayName; description; int playerCount; bool isClassic; List<RolePresetEntry>` where `RolePresetEntry = { RoleID roleId; int max; int forced }`.
- `RolePresetDatabase` SO: `List<RolePreset>`; `ForPlayerCount(n)`, `Classic(n)`.
- Apply via `GameSettingsManager.RequestApplyPreset` (host-only). UI: "Preset classique" → `Classic(players)`; "Autres presets…" → `ForPlayerCount(players)`, hidden when ≤1 entry.
- Authoring = design-owned (Poyo); ship mechanism + a couple placeholders.

## Tasks & Acceptance (commit sequence — refined by party-mode review)

The mechanic (C1–C3) lands **fully green before a single line of UXML**. Do this on a branch in the **MAIN checkout** (not a worktree — Unity MCP can't reach a worktree, and this stage lives on `run_tests`). Prefer `create_script` for every new `.cs` (silent-exclusion trap).

**C0 — golden baseline (NO logic change).** Regenerate/verify `RoleAssignmentGoldenMasterTests` from current `Dev` HEAD, green, committed **alone**. This is the honest baseline: any later golden movement = a real behaviour change, not a re-pin. Confirm coverage breadth first (players × pool compositions × seeds — a one-config golden is decorative; widen if thin).

**C1 — DTO wire change (the boss).** `RoleAttributionSetting` (`roleToAttribute`→`max`, `canBeFake:bool`→`forced:int`) + replicated `RoleSettingEntry {roleId, max, forced}` (`NetworkSerialize`/`Equals`/`GetHashCode` — `Equals` MUST include `forced`, else phantom no-op sync). **Migrate authored `.asset`s by ADDING fields + a migration pass (`count→max`, `canBeFake=true⇒forced=0`, `canBeFake=false⇒forced=max`), verify, then remove the old fields — never silent-rename** (`feedback_serialized_field_rewiring`). Re-run the `[CHARLIST]` dedup guard. Tests (write red first): serialize→deserialize→field-equality round-trip; the migration mapping table; manager seed reads `max`/`forced`. **Include the end-to-end DTO round-trip in this commit** (authoring SO → `GameSettingsManager` NetworkList → a host-side read) — do not defer wire validation to the UI waves.

**C2 — distributor + gate (pure POCO).** `RoleDistributor` gains the `forced` list + a **reserve-forced-reals-first** pass before the existing fake-then-real depleting draw. **The `forced=0` path MUST early-out before any `IRandomProvider` call** so the RNG consumption order is untouched (else byte-identical is a lie). `LobbyState.OnStartGameButtonPressed` gate → `GetTotalForced() ≤ players ≤ GetTotalMax()`. Tests: (a) **`forced=0` ⇒ byte-identical to the C0 golden** (necessary, not sufficient); (b) **new goldens with `forced>0` spread across roles** — read them by hand before pinning; (c) boundary `forced==max`; (d) `Σforced==players` (fully constrained → deterministic regardless of seed); (e) **property tests**: ∀ valid config `sum(reals)==players`, `count(fakes)==Σmax−players`, **`reals_i ≥ forced_i` per role** (the silent one); (f) **gate↔distributor contract**: every state the gate ACCEPTS produces a valid distribution with no exception (highest-ROI test); (g) clamp `forced ≤ max` on invalid SO input — decide clamp-vs-throw explicitly and pin it (SOs are designer-editable → invalid config is a production input); (h) iteration order is frozen-pool-order, not `Dictionary` order (`RolePoolOrderingTests` covers the new pass).

**C3 — replication (PlayMode, isolated).** One 2-NetworkManager test: host sets `max`/`forced`, a late-joiner client-replica sees the same. **Assert from the HOST** (`IsOwner` unreliable on loopback replicas). Keep it ALONE in its file (port-7777 flake); do not block C1/C2 on it, but do not skip it.

**C4 — UITK scaffolding + harness (recipe conditions up front).** `BackupToolkit/` copy of `GameSettingsPanel.prefab` + `GameScene.unity`. `PS_LobbyRoles_RT.asset` (**cloned**, never the shared overlay); `LobbyRoles.uxml`/`.uss` skeleton; controller stub (guarded `TryInitialize`, root `pickingMode=Ignore` + world-mask via a scrim child); `ILobbyRolesDataSource` + `DemoLobbyRolesDataSource` (**typed on the REAL DTO**, not a fiction). **Acceptance = a click on a stepper registers THROUGH the RT** (PanelInputConfiguration + cloned PanelSettings + ScreenToPanel proven), not just that it renders.

**C5 — card grid + tally (read-only, on the stub).** Reusable `RoleGridCard` face (portrait/name/faction tint, 5:7, dimmed↔lit, gold forced tag) factored from RoleCard; faction-grouped grid; live tally (players · Σforced · Σmax · per-faction); card tap → `RoleCard.Open`.

**C6 — steppers → stub (local, no network).** Max + Forcé steppers mutate the `DemoLobbyRolesDataSource`; `forced ≤ max` clamp incl. lower-max-clamps-forced; visual feedback. Isolates "stepper logic" from "replication".

**C7 — steppers → backbone (replicated).** Wire the data source to the live `GameSettingsManager` (`RequestSetMax`/`RequestSetForced`, host-only, non-host mirror; refresh on `OnSettingsChanged`). The seam stays a data port + intents — the gate rule stays in the server POCO, never in the adapter.

**C8 — tabs, presets, Start on the tablet.** "Ta partie" tab = same grid filtered `max>0` (a view filter) + badge; empty "Autres options". `RolePreset`/`RolePresetDatabase` + `RequestApplyPreset` + placeholders; preset bar (classic + filtered popover, hidden when ≤1). `LobbyStartButton` on the tablet: disabled + **readable reason pointing to the offending forced roles**; tap → `ServerRpc` → **server re-validates the gate** before transition.

**C9 — port to GameScene.** Swap the uGUI content in `GameSettingsPanel.prefab` for the UITK stack; wire refs; confirm the `EventSystem` `PanelInputConfiguration` covers `PS_LobbyRoles_RT`. Disable (Ask First before delete) the old slider path + the HUD Start button. `run_tests` green; SceneWiringGuard green. Real 2-build client playtest = Poyo.

## Open / design-owned decisions (remaining — the rest resolved this session, see Locked decisions)
- **Preset roster** — actual presets per player count (`max`+`forced` values). Guidance (Samus): "classique" optimizes a first-time host's game — a working camp ratio, pillars at `forced=max` (fixed) + bluff on peripherals via `forced<max`, 1–2 decoys, one classic per player-count tier.
- **Forced authoring** — which roles get a forced minimum by default (the `canBeFake→forced` migration values).
- **Balance rules beyond the scalar gate** — deferred ("on verra à la fin"); camp-ratio guardrail explicitly declined for now.
- **`MaxRoleCount` 15 vs `[Range(0,10)]`** — align while here?
- Real card art / faction icons (via `PortraitTable` / `FactionDatabase`).

## Risks & gotchas
- **`PanelInputConfiguration` on the EventSystem** is mandatory for RT-panel input (InfoTable lesson) — omit = dead clicks.
- **Clone PanelSettings**, never mutate shared `PS_ScreenOverlay` (hijacks RoleCard).
- **No root `pickingMode=Position`** on a fullscreen element (blocks world input) — pick per-element.
- **UITK renders in Play only** — diagnose via visual-tree `resolvedStyle`, not USS theory; MCP screenshots show uGUI not UITK-runtime.
- **`Label` used as a grid cell** needs `margin:0` (default `.unity-label` margin floats it).
- **Golden-master is being redefined on purpose** — the `forced=0` equivalence test is the guard that the redefinition didn't break the old behaviour.
- **No Unity MCP in a worktree** — build there = Edit/Write only, verify when Unity opens the branch.
- **Serialized-field rewiring** on the prefab goes through MCP re-linking (append-don't-rename) or refs break silently.
- **This spec `.md` must be committed** with the work (git clean/switch wipes untracked skill artifacts).

## Design reference
**Reproduce the VISUAL & interaction of this proto, NOT its code** (the proto JS is a quick throwaway — likely rough; only the look, layout, states, and interaction flow are the contract). Palette is design-owned / provisional, not visual-final.
- In-repo (open it locally): `_bmad-output/implementation-artifacts/proto/role-select-proto.html`
- Hosted (interactive): https://claude.ai/code/artifact/91ee0ded-eb55-4799-9832-1a8392aff168

Covers: the Max/Forcé two-stepper card, faction-grouped 5:7 grid, dimmed↔lit + gold forced tag, live tally, preset bar (classic + filtered popover), "Ta partie" = same grid filtered `max>0`, gated Démarrer.

## Review Findings — post-merge code review (PR #83, 2026-07-15)

### Decision needed
- [ ] [Review][Decision] Migration safety — authored `RoleAttributionState` assets: `canBeFake=false + roleToAttribute>0` (mandatory roles) silently migrate to `max=N, forced=0` (fully fakeable), dropping the Σforced floor. Migration keeps `[FormerlySerializedAs("roleToAttribute")]→max` but hard-defaults `forced=0`; nothing reconstructs `forced` from the old bool. Correctness rests on the unverifiable inline claim "all authored assets had roleToAttribute=0". [RoleAttributionState.cs:498-512] (blind+auditor)
- [ ] [Review][Decision] `forced ≤ max` at the seed/authoring boundary — clamp vs throw (spec C2(g) explicitly defers this to a human and says "pin it"). `SeedFromAuthoredDefaults` copies `forced` verbatim; `RoleAttributionSetting` exposes raw `[Range(0,10)] forced`; only runtime setters clamp. An authored `max=1, forced=3` makes the start window `Σforced ≤ players ≤ Σmax` empty (game can't start), and at the distributor `remaining[i]` stays `max` so only `max` reals are placed while the gate promised `forced`. [GameSettingsManager.cs:105-110, RoleAttributionState.cs:505-512, RoleDistributor.cs:44-46] (blind+edge+auditor)
- [ ] [Review][Decision] Missing spec scope — cut permanently or follow-up tasks? (a) "Ta partie" tab absent (only 2 tabs built, index 1 skipped, `TabBadgeClass` declared-unused); (b) gold "★ forcé N" card-face tag missing (forced only visible inside the stepper); (c) `RequestApplyPreset(RolePreset)` single replicated batch not implemented — `ApplyPreset` loops N× `RequestSetRoleCount`+`RequestSetForced` (N replication ticks). [LobbyRolesUitkController.cs BuildTabs/BuildCard, GameLobbyRolesDataSource.cs:1267-1284] (auditor)
- [ ] [Review][Decision] Lobby-roles grid client path — is the grid host-only by design? `GetPlayerCount` non-server branch reads NGO `ConnectedClients.Count`, which throws `NotServerException` off-server → every `Rebuild` on a non-host tablet throws (grid never paints). Also non-host start tap is silently inert (no `ServerRpc`/`GetSafeRpcTarget`; `RequestStart` early-returns unless `IsServer`). Severity is HIGH if clients ever get this grid, cosmetic if host-only. Fix (unambiguous regardless): guard `ConnectedClients` under `IsServer`, use a replicated count on the client branch. [GameLobbyRolesDataSource.cs:1180, RequestStart ~1213-1226] (blind+auditor)

### Patch
- [ ] [Review][Patch] `MatchesPreset` compares UNCLAMPED authored `entry.max`/`entry.forced` against server-clamped `GetRoleCount`/`GetForced` → an out-of-range preset (`max=20`) is never detected active; badge stuck on "○ Personnalisé" right after applying. `DemoLobbyRolesDataSource.MatchesPreset` already clamps — align the Game impl. [GameLobbyRolesDataSource.cs:1242-1254] (edge)
- [ ] [Review][Patch] Unknown/out-of-`{anomaly,chosen,marginal}` `factionType` is summed into `totalMax`/`totalForced` and the Démarrer gate but never rendered (no card, no per-faction tally). Host sees an invisible role block/enable start; visible cards no longer sum to the displayed totals. Exclude unlisted factions from the tally/gate too, or render a catch-all section. [LobbyRolesUitkController.cs:1611 (FactionOrder) + 1703-1711 (Rebuild sums)] (blind+edge)
- [ ] [Review][Patch] Dead code cleanup — `TabOptions=2` leaves tab index 1 unused (renumber 0/1); `lobby-roles__card*` constants + matching USS rules (2380-2408) never emitted (`BuildCard` uses `RoleCardElement`); stale `canBeFake` shim (`RoleAttributionSetting.CanBeFake`, `GameSettingsManager.GetCanBeFake`) with comments referencing the removed distributor bool path. [LobbyRolesUitkController.cs:1606/1577, GameSettingsManager.cs:144-151] (blind+auditor)

### Deferred
- [x] [Review][Defer] C2 test top-up — no property tests (`reals_i ≥ forced_i` per role, `sum(reals)==players`, `count(fakes)==Σmax−players`), no gate↔distributor contract test, and golden masters only exercise `forced=0`/`forced=max` (never a partial spread across roles). Only 3 fixed stub-RNG cases shipped. Deferred: test-authoring follow-up, no behavior regression (goldens byte-identical). [RoleDistributorTests.cs:666-702, RoleAssignmentGoldenMasterTests.cs:797-809] (auditor)
