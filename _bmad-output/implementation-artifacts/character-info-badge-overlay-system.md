# Story 1.3: Modular info-badge overlay system (anchor + offset) for characters and cards

Status: blocked (depends on GameInfoRevealer rewrite)

> **⚠️ BLOCKING DEPENDENCY:** `GameInfoRevealer` is being **completely rewritten**. This story must be implemented **after** that rewrite lands. The rewrite will also carry the **heal info** (the missing reveal piece for `isHealed`). Do NOT add `isHealedRevealed` in this story — consume whatever the rewritten reveal API exposes for heal. Re-validate all `GameInfoRevealer` references below against the new API before starting (the field names / `GetCharacterInfo` signature / `onCharacterInfoRevealedChanged` may change).

**Reference:** Builds on Story 1.1 (Role bar sorted by faction + awakening) and Story 1.2 (`visual-grouping-characters-bar.md`). Same `CharactersBar` / `CharactersBarObject` surface.

## Story

As a player,
I want small gameplay-info icons (e.g. a purple "healed" ring, a red "corrupted" ring) to appear at precise, configurable spots on each character in the CharactersBar **and** on the cards shown during a power's role/character selection,
so that I can read at a glance the information I have learned about each player, in both the persistent bar and the in-power picker.

## Context

The `CharactersBar` already sorts and visually groups characters by faction (Stories 1.1–1.2). Each `CharactersBarObject` today renders exactly **one hardcoded overlay**: `corruptedOverlayImage`, faded in/out from `UpdateCharacter()` based on `GameInfoRevealer` reveal state. This is not modular — every new piece of info (healed, protected, marked, etc.) would mean another hardcoded `Image` field + bespoke fade code, and none of it is reusable on the role-picker cards.

The mockups (provided by Poyo) show:
- **Mockup A (character bar):** colored rings drawn *on top of* portraits — a red ring = "corrupted and I have the info", a purple ring = "healed and I have the info", positioned at different spots (bottom-left, bottom-center, etc.).
- **Mockup B (role picker):** the same kind of badges must also be placeable on the larger cards during a power's role-selection flow (`CardPickerManager.ShowRolePicker` / `ShowCharacterPicker`).

The user's explicit requirement: badges must be placed via an **anchor + offset** scheme (not offset alone), so the *same* badge definition lands correctly on two differently-sized/shaped surfaces (small bar icon vs. large card). "Propre et facilement modulable" — clean and easily extensible: adding a new badge type should be data-driven, not a code change per badge.

## Acceptance Criteria

1. **Modular badge definitions (data-driven):** A new badge type can be added without modifying `CharactersBarObject` or card code — only by adding a data entry (ScriptableObject under `Assets/ScriptableObjects/` and/or a sprite mapping). Adding "healed" must not require touching the corruption logic.
2. **Anchor + offset placement:** Each badge is positioned by an **anchor** (a normalized anchor point / named slot on the host surface — e.g. bottom-left, center, top-right) **plus** a pixel/units offset from that anchor. The same badge definition placed on a small bar icon and on a large picker card lands at the visually-equivalent spot on each.
3. **Two host surfaces — ROLE views only:** Badges render correctly on (a) `CharactersBarObject` in the persistent bar (which displays **roles** — by design in this game the "CharactersBar" shows roles, not characters), and (b) the **role-picker** cards spawned by `CardPickerManager.ShowRolePicker`. Badges must **NOT** appear on the **character-picker** cards (`ShowCharacterPicker`) — they only show when the player is looking at roles, never at characters. The placement system is shared, not duplicated per surface.
4. **Driven by revealed info:** Which badges show for a given character is derived from `GameInfoRevealer` / `CharacterInfoReveal` for the local (or simulated) observer — a badge appears only when the local observer actually has that info (mirrors today's `corruptedOverlayImage` gating: `isCorrupted.Value && forceCorruptOnRoleRevealed > RevealLevel.False`).
5. **Corruption parity (regression):** The existing corrupted overlay behavior is preserved or migrated into the new system with identical visible result (red indicator fades in when corruption is known, out otherwise). No double-rendering of corruption (old hardcoded overlay + new badge at once).
6. **Healed badge (first new badge):** A "healed" badge (purple ring per mockup) is wired as the first *new* badge produced by the system, proving extensibility end-to-end. The heal **state** already exists (`Character.isHealed` NetworkVariable, set server-side by `HealPlayerServerRpc`). The **heal reveal info will be provided by the rewritten `GameInfoRevealer`** (see blocking dependency) — this story does NOT add the reveal field; it consumes whatever the new API exposes and shows the badge when the character is healed AND the local observer has the heal info (mirroring the corruption gating in the new API).
7. **Dynamic updates:** Badges add/remove/refresh live when reveal state changes (`onCharacterInfoRevealedChanged`) and when corruption changes (`isCorrupted.OnValueChanged`), without rebuilding the whole bar. Picker cards show the correct badges at spawn time.
8. **Clean teardown:** Badges are destroyed/pooled with their host (bar rebuild via `ResetCharactersBar`, card teardown in `CardPickerManager.CancelPicker`) — no leaked GameObjects, no dangling event subscriptions (follow the existing subscribe/unsubscribe discipline in `CharactersBarObject`).
9. **Inspector-tunable & scalable:** Anchor slots, offsets, and per-badge sprite/color are configurable from the inspector / SO without recompiling. The taxonomy will grow to **~10+ badge types**, so the design must scale cleanly (enum id + per-badge SO registry; no per-badge hardcoding). Naming follows project conventions (`DataObject` suffix for data SOs).
10. **No regression:** Hover lift (`hoverVisual` move toward camera) still works. **Decision (from design):** the badge surface is parented as a **child of `hoverVisual`** so badges ride with the lifted portrait. Verify this does not reintroduce hover spam (badges must not be raycast targets that move the cursor's hit; set their `Image.raycastTarget = false`). 57/57 EditMode tests still pass; new logic has unit coverage.

## Tasks / Subtasks

- [ ] **Task 1 — Design the badge placement model (AC: 1, 2, 9)**
  - [ ] Define an anchor representation: a normalized anchor (e.g. `Vector2` in 0..1 like Unity `RectTransform.anchorMin/Max`, or an enum of named slots mapping to normalized points) + an offset `Vector2`. Prefer normalized anchor so it scales across surface sizes.
  - [ ] Create a `BadgeDataObject : ScriptableObject` (suffix `DataObject`, under `Assets/ScriptableObjects/`) holding: badge id/enum, sprite, optional tint `Color`, default anchor, default offset, optional size. Keep it pure data (no `Instantiate`, no scene side-effects — see project-context anti-patterns).
  - [ ] Decide badge id source: extend an enum (e.g. `CharacterBadgeID`) rather than magic strings.
- [ ] **Task 2 — Build the reusable badge host component (AC: 2, 3, 7, 8)**
  - [ ] Create a `CharacterBadgeOverlay` MonoBehaviour that owns a `RectTransform` "badge surface" and instantiates badge `Image`s from a small badge prefab, positioning each via anchor + offset. One instance lives on the bar-icon prefab and one on the card surface.
  - [ ] Public API like `SetBadges(IEnumerable<ActiveBadge>)` / `AddBadge` / `ClearBadges`; internally pool or destroy cleanly.
  - [ ] Anchor resolution converts (normalized anchor + offset) → `anchoredPosition` so a single `BadgeDataObject` renders at the equivalent spot on both small and large surfaces.
- [ ] **Task 3 — Map revealed info → active badges (AC: 4, 5, 6)**
  - [ ] Create a resolver that, given a `Character` + the observer's `CharacterInfoReveal` (via `GameInfoRevealer.GetCharacterInfo`), returns the list of badges to show. Corruption rule must match current gating exactly.
  - [ ] **Do NOT add a heal reveal field** — the rewritten `GameInfoRevealer` provides heal info. Read the new reveal API and map the heal info to the healed badge, gated the same way corruption is in the new API (character `isHealed` true AND local observer has the heal info).
  - [ ] Re-check corruption gating against the new API too — `forceCorruptOnRoleRevealed` / `isCorruptRevealed` field names may have changed in the rewrite.
- [ ] **Task 4 — Integrate into `CharactersBarObject` (AC: 4, 5, 7, 8, 10)**
  - [ ] Add a `CharacterBadgeOverlay` to `CharacterBarObject.prefab`; reference it from `CharactersBarObject`.
  - [ ] In `UpdateCharacter()`, replace the bespoke `corruptedOverlayImage` fade by calling the resolver + `SetBadges(...)` (or migrate so corruption flows through the badge system). Avoid rendering corruption twice.
  - [ ] Keep existing subscribe/unsubscribe to `onRoleUpdated`, `isCorrupted.OnValueChanged`, and `gameInfoRevealer.onCharacterInfoRevealedChanged`. **Add** `isHealed.OnValueChanged` (symmetric sub/unsub) so the healed badge refreshes live.
  - [ ] Parent the badge surface under `hoverVisual` (design decision: badges ride the hover lift). Set badge `Image.raycastTarget = false` to avoid hover-spam from a moving collider.
- [ ] **Task 5 — Integrate into ROLE-picker cards only (AC: 3, 4, 7)**
  - [ ] Add the badge surface to the card prefab (referenced via `CardVisualComponents`) and populate it **only** for cards spawned in `CardPickerManager.ShowRolePicker`, using the same resolver.
  - [ ] **Do NOT** populate badges in `ShowCharacterPicker` — by design, badges show only when looking at roles, never at characters.
  - [ ] Ensure badges are torn down with the card in `CancelPicker`.
- [ ] **Task 6 — Authoring data + scene wiring (AC: 1, 6, 9)**
  - [ ] Create `BadgeDataObject` assets for corruption (red ring) and healed (purple ring); import/assign the ring sprites under `Assets/Art/Sprites/...`.
  - [ ] Wire the badge registry/list where the resolver reads it (central UI config or `CharactersBar` inspector — match how `factionIcons`/`factionTextColors` are already wired).
  - [ ] Assign references in `GameScene` and on the prefabs.
- [ ] **Task 7 — Tests & verification (AC: 5, 6, 10)**
  - [ ] EditMode unit tests for the resolver (corruption known/unknown → badge present/absent; observer vs. simulated observer ≥100) and for anchor→anchoredPosition math on two surface sizes.
  - [ ] `mcp__UnityMCP__read_console` clean compile after each change; `mcp__UnityMCP__run_tests` EditMode green (≥ 57/57).

## Dev Notes

### Current state of files being modified (read before editing)

- **`Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs`** — today:
  - Single hardcoded overlay: `[SerializeField] private Image corruptedOverlayImage;` faded in `UpdateCharacter()`:
    `bool _isCorrupted = playerCharacter.isCorrupted.Value && _forceCorruptOnRoleRevealed > RevealLevel.False;` then `corruptedOverlayImage.DOFade(_isCorrupted ? 0.65f : 0, 0.35f);`
  - Subscriptions established in `Start()` / `SetCharacter()` / `OnDestroy()`: `onRoleUpdated`, `isCorrupted.OnValueChanged`, `gameInfoRevealer.onCharacterInfoRevealedChanged`. **Preserve this discipline** — any new event sub must be unsubscribed symmetrically.
  - Hover system moves `hoverVisual` (separate from root so the static raycast target never moves). Badge surface placement must not reintroduce the "moving collider" hover spam the comment warns about.
- **`Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs`** — builds the bar in `ResetCharactersBar()`: clears children, sorts (`SortCharacters`), groups (`GroupConsecutiveByFaction`), instantiates one `factionGroupPrefab` per faction and `characterBarObjectPrefab` per character. Inspector spacing + `factionIcons`/`factionTextColors` `SerializedDictionary` are the established pattern for per-faction data; mirror it for badge data if the registry lives here.
- **`Assets/Scripts/GameLogic/GameInfoRevealer.cs`** — ⚠️ **BEING REWRITTEN — the description below is the OLD API; re-verify everything against the new version before coding.** Source of truth for "what does the local observer know". OLD API: `GetCharacterInfo(clientId, observerId)` returned `CharacterInfoReveal { isRoleRevealed, isCorruptRevealed, forceCorruptOnRoleRevealed }`. The **rewrite adds heal info** — use that for the healed badge. Observer id `>= 100` = simulated bot (separate brain) — the resolver should go through whatever public accessor the new API offers (the old one handled the ≥100 split internally), not raw dictionaries. `onCharacterInfoRevealedChanged` (or its rewritten equivalent) fires only for the local observer.
- **`Assets/Scripts/Characters/Character.cs`** — `isHealed` (`NetworkVariable<bool>`, line ~25) already exists, set server-side by `HealPlayerServerRpc` (~line 175). Mirror the `isCorrupted` display path for healed. The heal state is server-authoritative — do not add new state, only consume + add the reveal field.
- **`Assets/Scripts/UI/BoardUI/CardPickerManager.cs`** — spawns cards via `BoardManager.instance.AddNewCard(...)`, sets `CardRoleVisualUpdater` / `CardRoleAnimation`, lays them in an arc; tears down in `CancelPicker`. `HidePlayerIdentity(card)` hides pseudo/me-icon. Badge population hooks in after card creation in `ShowRolePicker`/`ShowCharacterPicker`; teardown hooks in `CancelPicker`.
- **`Assets/Scripts/Board/CardComponents/CardVisualComponents.cs`** — central `[SerializeField]` reference holder for card visuals (`cardImage`, `unknownFogOverlay`, `chainedOverlay`, faction logos…). Add the badge-surface reference here to follow the existing pattern rather than `GetComponentInChildren` at runtime.

### Anchor + offset — why, and suggested model

Offset-only fails because the bar icon and the picker card differ in size/aspect. A normalized anchor (0..1 over the host's `RectTransform`) + a `Vector2` offset gives "bottom-left ring, nudged 4px in" that resolves correctly on both. Suggested: `BadgeDataObject` stores `Vector2 anchor` (0..1) and `Vector2 offset`; `CharacterBadgeOverlay` computes `anchoredPosition = (anchor - pivot) * hostRect.size + offset` (or set the badge `RectTransform` anchorMin=anchorMax=anchor and `anchoredPosition = offset`). The latter is cleanest and auto-rescales. Allow per-host offset override only if a badge needs surface-specific tuning.

### Existing reusable patterns to follow (don't reinvent)

- `MeIconCard` (`Assets/Scripts/UI/CardUI/MeIconCard.cs`) is already an "icon overlaid on a card" — review it for the established overlay/anchor approach before designing a new one.
- `SerializedDictionary<FactionType, Sprite>` (from `AYellowpaper.SerializedCollections`) is the project's way to map enum→asset in the inspector — use it for badge-id→sprite if not using per-badge SOs.
- DOTween fades (`DOFade`, `DOKill`) are the established show/hide; reuse the 0.35s fade feel of the current corrupted overlay.

### Testing standards

- EditMode tests live under the test asmdef (`defineConstraints: ["UNITY_INCLUDE_TESTS"]`). Keep new pure logic (resolver, anchor math) free of scene dependencies so it's unit-testable without PlayMode.
- Run `mcp__UnityMCP__run_tests` (EditMode) and confirm ≥ 57/57. Add cases per Task 7.

### Project Structure Notes

- New data SOs → `Assets/ScriptableObjects/`, suffix `DataObject`, pure data (project-context: SOs hold values + decision logic only, no `Instantiate`/`FindObjectOfType`).
- New runtime scripts → alongside the bar UI (`Assets/Scripts/Board/UI/CharacterBar/`) or a shared `Assets/Scripts/UI/` location if used by both bar and cards; keep within `Game.asmdef` (no editor-only refs).
- Sprites → `Assets/Art/Sprites/UI/`.

### Project Context Rules

- **Async = UniTask only** (`UniTask`/`UniTaskVoid`) — never `System.Threading.Tasks.Task`, never `IEnumerator` coroutines. `UpdateCharacter()` is already `async UniTaskVoid`.
- **Audio = FMOD** via `AudioSystem`/`GameAudioManager` — if a badge plays a sound, never `AudioSource`.
- **Server authority strict** — this story is presentation-only; do **not** add new authoritative game state. Reveal data already comes from server via `GameInfoRevealer` RPCs. If a "healed" *state* must be introduced, that is server-authoritative and is a design/architecture question (see Questions), not something to fabricate here.
- **`GetSafeRpcTarget(clientId)`** + **`IsLocalOrSimulated(clientId)`** — only relevant if you touch RPC/identity; reuse `GetCharacterInfo`'s existing ≥100 simulated handling instead of re-checking identity yourself.
- **`[FormerlySerializedAs]`** when renaming any serialized field, or prefab/scene values silently wipe. Relevant if you rename `corruptedOverlayImage`.
- **ScriptableObject runtime mutation persists to disk in Editor** — never mutate a `BadgeDataObject` at runtime; read from it only.
- Default visibility `internal`/`private`; promote to `public` only across asmdef boundaries.
- After every code change: poll `mcp__UnityMCP__read_console` for compile errors before assuming it works.

### References

- [Source: Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs] — current corrupted overlay + event discipline + hover visual.
- [Source: Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs] — bar build/rebuild, per-faction inspector data pattern.
- [Source: Assets/Scripts/GameLogic/GameInfoRevealer.cs#CharacterInfoReveal] — reveal fields + observer/simulated split.
- [Source: Assets/Scripts/UI/BoardUI/CardPickerManager.cs] — card spawn/teardown for the picker surface.
- [Source: Assets/Scripts/Board/CardComponents/CardVisualComponents.cs] — central card visual reference holder.
- [Source: Assets/Scripts/UI/CardUI/MeIconCard.cs] — existing card-overlay icon pattern.
- [Source: _bmad-output/implementation-artifacts/visual-grouping-characters-bar.md] — prior story on this surface.
- [Source: _bmad-output/project-context.md] — Unity/NGO/UniTask/SO/asmdef rules cited above.

## Resolved Decisions (from Poyo)

1. **Heal info:** `Character.isHealed` state exists; the **heal reveal info comes with the upcoming `GameInfoRevealer` rewrite**. This story is therefore **blocked until that rewrite lands** and must consume the new reveal API (do not add a reveal field here).
2. **Badge taxonomy:** ~10+ badge types planned (full list TBD) → design must be enum + SO-driven and scale cleanly. No per-badge hardcoding.
3. **Role-only visibility:** Badges show only when looking at **roles** — the `CharactersBar` (which displays roles) and the **role picker**. Never on the **character picker** / character views.
4. **Hover:** Badges are **children of `hoverVisual`** → they ride the hover lift. Set `raycastTarget = false` to avoid hover spam.

## Dependencies

- **Blocks on:** Story 1.4 — `modular-information-system-rewrite.md` — complete rewrite of `Assets/Scripts/GameLogic/GameInfoRevealer.cs` (exposes corruption + `InfoKey.WasHealed` heal reveal via the new `IGameInfoRevealer` API). Re-validate every `GameInfoRevealer` / `CharacterInfoReveal` reference in this story against the new API before implementation.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
