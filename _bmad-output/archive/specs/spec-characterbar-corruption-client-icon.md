---
title: 'CharactersBar corruption: overlay → client-side stack icon'
type: 'feature'
created: '2026-07-21'
status: 'done'
context: []
baseline_commit: 'fb3090f1'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Corruption knowledge (Glooby's `PCorruptionKnowledge` and Robot's `PPersonalBeacons`, both granting `forceCorruptOnRoleRevealed`) is surfaced on the CharactersBar as a full-thumbnail fading overlay `Image`, inconsistent with the new stacked private-icon system (`PlayerIconManager` + `CharacterBarIconStack`).

**Approach:** Delete the overlay. Add a generic **client-side icon channel** (`LocalBarIconRegistry` — per-`NetworkManager` `For(nm)`, no networking) that `CharacterBarIconStack` merges with the existing server slice. A dedicated client driver reads the *same* corruption gate (local reveal knowledge + live `isCorrupted`) and writes/clears a `"corruption"` icon — reusing the overlay's own sprite — into that channel. Reveal decisions and gameplay stay untouched: this is a pure render relocation.

## Boundaries & Constraints

**Always:**
- Corruption is shown iff `GetCharacterInfo(markedId).forceCorruptOnRoleRevealed > RevealLevel.False` (default observer = local viewer) **AND** `markedCharacter.isCorrupted.Value` — identical to the old overlay gate. Reactive to both `isCorrupted.OnValueChanged` and `onCharacterInfoRevealedChanged`.
- `LocalBarIconRegistry` is client-only, resolved via `For(nm)` born-clean (static `Dictionary<NetworkManager,…>`, domain-reload reset, unregister-by-value) — mirror `AvatarManager`/`PlayerIconManager` registry hygiene. No `static instance`, no `NetworkVariable`, no RPC.
- Stack order: client-channel icons FIRST, then server power-slice icons → `[corrupt][power…]`. The corruption icon rides the same collapse/fan-out layout as power icons.
- Reuse the sprite currently on the prefab's `corruptedOverlayImage` as the corruption icon.
- `CorruptionKnowledgeDecision`, `PersonalBeaconsDecision`, `RevealInfo`/`RevealInfoExecutor`, and `GameInfoRevealer` are unchanged.

**Ask First:**
- If the corruption sprite cannot be recovered from the prefab's `corruptedOverlayImage`.
- Any change that would alter WHO sees corruption or WHEN (must stay behavior-preserving vs. the overlay).

**Never:**
- No new server state / `NetworkVariable` / RPC for corruption display; do not touch the `PlayerIconManager` server path.
- Do not show corruption to viewers lacking the knowledge flag (no leak).
- No `AudioSource`; any async is `UniTask`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Known + corrupted | `forceCorrupt>False`, `isCorrupted=true` | corruption icon present, FIRST in target's stack | N/A |
| Healed mid-game | `isCorrupted` true→false, viewer knows | icon removed live (no full-bar rebuild) | N/A |
| Corrupted mid-game | `isCorrupted` false→true (corrupt/chain), viewer knows | icon appears live | N/A |
| No knowledge | `forceCorrupt=False`, `isCorrupted=true` | no corruption icon | N/A |
| Mixed with powers | corruption + N power icons | order `[corrupt][power…]`; fan-out includes it | N/A |
| 2 NetworkManagers | host & client hold different local knowledge | each stack reads its OWN nm registry; no cross-peer leak | N/A |
| Rematch / roster rebuild | new game session | driver re-subscribes; registry fresh; no stale icon | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/GameLogic/LocalBarIconRegistry.cs` — NEW. Client per-nm keyed-sprite registry + `onLocalIconsChanged`.
- `Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs` — merge registry sprites (first) + server slice; subscribe registry event.
- `Assets/Scripts/Board/UI/CharacterBar/CorruptionIconDriver.cs` — NEW. Client producer: roster + `isCorrupted` + reveal → write/clear `"corruption"` icon.
- `Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs` — remove overlay field/fade + `isCorrupted` subscription (corruption concern leaves the view).
- character-bar-object prefab + `GameScene` — disable/remove overlay `Image`; add & wire `CorruptionIconDriver` (sprite + `CharacterManager`).
- `Assets/Scripts/GameLogic/PlayerIconManager.cs`, `Assets/Scripts/Avatars/AvatarManager.cs` — `For(nm)` reference patterns.
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` — `GetCharacterInfo(id)` gate source (read-only).

## Tasks & Acceptance

**Execution:**
- [x] `LocalBarIconRegistry.cs` (new) — `For(nm)` GetOrCreate + `ResetStaticsForDomainReloadDisabled`; `SetIcon(markedId,key,sprite)` (replace-by-key), `ClearIcon(markedId,key)`, ordered `GetIconsFor(markedId)`, `onLocalIconsChanged`. Mirror `AvatarManager` hygiene. (No unregister-by-value: born lazily with no spawn hook, cleared on domain-reload.)
- [x] `CharacterBarIconStack.cs` — `ResolveRenderableSprites` prepends `LocalBarIconRegistry.For(nm).GetIconsFor(markedId)` before the server power sprites; subscribes/unsubscribes `onLocalIconsChanged` alongside `PlayerIconManager` (new `TryResolveLocalRegistry` in the watch loop) and rebuilds on it. Registry resolved via the existing `ResolveNetworkManager()`.
- [x] `CorruptionIconDriver.cs` (new) — subscribes `CharacterManager.onCharactersListUpdated`, each `Character.isCorrupted.OnValueChanged`, and `revealer.onCharacterInfoRevealedChanged`; `RecomputeAll()` sets/clears the `"corruption"` icon per character via the local-viewer gate; serialized corruption `Sprite` + `CharacterManager`; resolves revealer/registry from `characterManager.NetworkManager` (per-peer, lazy/spawn-order tolerant). Idempotent re-subscribe; teardown unsubscribes + clears icons.
- [x] `CharactersBarObject.cs` — deleted `corruptedOverlayImage` field, its `DOFade` logic, `OnCorruptedChanged`, and the `isCorrupted` subscribe/unsubscribe. Portrait + revealer wiring kept (revealer sub left intact — low-risk, may back the portrait reveal).
- [x] character-bar-object prefab + `GameScene` — removed the `CorruptOverlay` GameObject (prefab); added `CorruptionIconDriver` on `CharactersBar` and wired `CharacterManager` + the recovered `Symbole_Corruption` sprite (GUID `25eb…`). Backed up prefab + scene; wired via MCP; re-read confirms both refs non-null.
- [x] `LocalBarIconRegistryTests.cs` (new, EditMode) — 12 tests: set/clear/get ordering, replace-by-key, null-clears, per-player isolation, event fire/no-fire discipline, `For(null)`/same-nm/two-nm isolation (inactive-GO NetworkManager keys).

**Acceptance Criteria:**
- Given a viewer with corruption knowledge and a corrupted target, when the bar renders, then the target's stack shows the corruption icon first and no full-thumbnail overlay remains.
- Given corruption flips (corrupt/heal/chain) mid-game, when `isCorrupted` changes, then the icon appears/disappears live without rebuilding the whole bar.
- Given a viewer without the knowledge flag, when a target is corrupted, then no corruption icon shows.
- Given host+client in the 2-NM fixture with differing local knowledge, then each peer's stack reflects only its own knowledge (registry `For(nm)` isolation).
- Given the EditMode suite, when run, then `LocalBarIconRegistry` tests pass and `VisionPowerTests`/`CorruptionTests` stay green (reveal flow untouched).

## Spec Change Log

- **Iter 1 (review, patches only — no spec change).** Three adversarial reviewers converged on one real regression + hardening; all fixed in code (no loopback):
  - **[patch] Per-frame feedback loop** — `CorruptionIconDriver` read the roster via `GetCharacters()` (default `triggerUpdate:true`), which re-raises `onCharactersListUpdated` end-of-frame; the driver subscribes to it → self-sustaining per-frame pump churning every roster subscriber. Fixed: `GetCharacters(false)` at both read sites.
  - **[patch] Reveal-only subscribe race** — the revealer was subscribed only lazily inside `RecomputeAll`; once the per-frame pump was removed, a late-resolving revealer + a reveal-only knowledge change could be missed forever (silent, host-only-invisible). Fixed: one-shot `ResolveDependenciesAsync` UniTask (generation-stamped, cancel-on-destroy) resolves + subscribes the revealer deterministically.
  - **[patch] Teardown clear robustness** — `ClearAllIcons` resolved the NM via the live `characterManager.NetworkManager`, null at teardown → orphaned channel entries. Fixed: cache `_resolvedNetworkManager`; moved the clear from `OnDisable` to `OnDestroy` so a transient disable never wipes icons other views (NoteRibbon) still read. Added `RebindWatched` dedup.
  - **[patch] Doc** — corrected the test-count note (12, not 14).
  - **[reject]** orphan `corruptedOverlayImage: {fileID: 0}` on `NoteCharacterObject.prefab` (already null, Unity strips it); `s_byNetworkManager` build-time growth (matches sanctioned AvatarManager/PlayerIconManager pattern).

## Design Notes

- **Two-source stack.** Server slice = authoritative private markers (`PlayerIconManager`, kept). Client channel = local-only derived indicators (`LocalBarIconRegistry`). Corruption is the first client consumer; the channel is generic (string key) for future client indicators — this is the "pratique à utiliser" generalization requested.
- **Producer ≠ view.** ONE driver per peer writes the registry, so every view of a player (main bar + `NoteRibbon` thumbnails) stays consistent and a closing ribbon can't clear an icon the bar still needs. The view (`CharactersBarObject`) sheds its corruption logic entirely.
- **Gate parity.** `GetCharacterInfo(markedId)` defaults its observer to the local client (`GameInfoRevealer.cs:108`), so the driver reproduces the old overlay gate exactly — Glooby AND Robot/Beacons keep working with zero decision changes.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after each script change) -- expected: no compile errors.
- `mcp__UnityMCP__run_tests` EditMode (filter `LocalBarIconRegistry`, then `VisionPower`/`Corruption`) -- expected: all pass.

**Manual checks:**
- In-editor: a corrupted target known to the viewer shows a stacked corruption icon (first), fanning out on hover; no overlay; a target unknown to the viewer shows nothing.

## Suggested Review Order

**Client icon channel (the new seam)**

- Per-NetworkManager, client-only registry — born-clean `For(nm)`, mirrors AvatarManager/PlayerIconManager.
  [`LocalBarIconRegistry.cs:47`](../../Assets/Scripts/GameLogic/LocalBarIconRegistry.cs#L47)

- Set/replace-by-key + fire-only-on-change event discipline (feeds the stack rebuild).
  [`LocalBarIconRegistry.cs:80`](../../Assets/Scripts/GameLogic/LocalBarIconRegistry.cs#L80)

**Corruption producer (one per peer)**

- The gate — byte-for-byte the old overlay's (knowledge + isCorrupted); writes/clears the "corruption" icon.
  [`CorruptionIconDriver.cs:192`](../../Assets/Scripts/Board/UI/CharacterBar/CorruptionIconDriver.cs#L192)

- Spawn-order-safe revealer subscribe (one-shot loop) so a reveal-only change can't be silently missed.
  [`CorruptionIconDriver.cs:108`](../../Assets/Scripts/Board/UI/CharacterBar/CorruptionIconDriver.cs#L108)

**Stack merge (render)**

- Local channel FIRST, then server power slice → [corrupt][power…]; runs even with a null server manager.
  [`CharacterBarIconStack.cs:360`](../../Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs#L360)

- Subscribes the local channel's change event alongside PlayerIconManager.
  [`CharacterBarIconStack.cs:216`](../../Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs#L216)

**Overlay removal**

- Corruption logic gone from the view — UpdateCharacter now sets only the portrait.
  [`CharactersBarObject.cs:189`](../../Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs#L189)

**Tests**

- Two NetworkManagers keep isolated channels (the no-leak guarantee).
  [`LocalBarIconRegistryTests.cs:192`](../../Assets/Scripts/Tests/Editor/LocalBarIconRegistryTests.cs#L192)
