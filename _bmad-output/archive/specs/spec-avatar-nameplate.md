# Spec: Player nameplate above the 3D avatar

Status: ready-for-dev

## Intent

Each player's pseudo (display name) floats above their 3D avatar (cat) so players can
identify who is who while looking around the table.

Design decisions (Poyo, 2026-07-13):

- **Scope:** every avatar EXCEPT the local player's own (you never see your own head in
  first-person anyway).
- **Visibility:** shown ONLY while the local camera is in first-person
  (`FreeRoam` walk-around OR the seated Embodied vote with the first-person node live).
  Hidden in board overview / any non-first-person view.
- **Render:** code-generated billboard text (world-space `TextMeshPro`), TMP default font,
  NO prefab asset, NO designed panel. Placeholder visuals — values tokenized/serialized so
  Poyo tunes them in the Inspector later.

## Context (seams — verified)

- **Anchor:** `PlayerAvatar.EyePivot` ([PlayerAvatar.cs:36](../../Assets/Scripts/Avatars/PlayerAvatar.cs)) — head-height pivot already on the prefab.
- **Identity → pseudo:** `PlayerAvatar.ownerClientId` (replicated) →
  `CompositionRoot.For(nm).LobbyPlayerInfoHolder.GetPlayerInfo(clientId).playerName`
  ([LobbyPlayerInfoHolder.cs:117](../../Assets/Scripts/Network/LobbyPlayerInfoHolder.cs)). Mirrors `Character.GetOwnerPseudo()` ([Character.cs:174](../../Assets/Scripts/Characters/Character.cs)).
  Live updates via `playerInfos.OnListChanged`.
- **First-person gate:** `CameraModeChannel` ([CameraModeChannel.cs](../../Assets/Scripts/Avatars/CameraModeChannel.cs)) — `Current == FreeRoam || SeatedFirstPersonLive`
  (same predicate the arbiter uses, [AvatarCameraArbiter.cs:301](../../Assets/Scripts/Avatars/AvatarCameraArbiter.cs)). Subscribe `OnChanged` + `OnSeatedFirstPersonLiveChanged`.
- **Billboard:** `Camera.main` (Cinemachine brain).
- **Local test:** `PlayerAvatar.IsOwner` on the local replica identifies "my avatar".
- Pure presentation, client-local: reads replicated values only. **No RPC, no NetworkVariable, no server state.** Server authority untouched.
- No nameplate exists today — this is the first above-head world UI in the project.

## Architecture

`Game` assembly ([Assets/Scripts/Game.asmdef](../../Assets/Scripts/Game.asmdef)) — TMP already referenced.

1. **`Assets/Scripts/Avatars/NameplatePolicy.cs`** — pure static POCO (no Unity types):
   - `bool ShouldShow(bool isLocalAvatar, bool isFirstPerson) => !isLocalAvatar && isFirstPerson`
   - `string ResolveLabel(string pseudo)` — trim; whitespace/null → empty.
2. **`Assets/Scripts/Avatars/AvatarNameplate.cs`** — `MonoBehaviour`, `[RequireComponent(typeof(PlayerAvatar))]`, namespace `Avatars`:
   - Serialized (all provisional): `_cameraModeChannel`, `_worldHeightOffset`, `_fontSize`, `_worldScale`, `_color`, optional `_fontOverride`, optional `_anchorOverride`.
   - Lazy init in `Update` once `PlayerAvatar` NetworkObject `IsSpawned` **and** the holder resolves.
   - If `IsOwner` (my avatar) → suppress permanently, build nothing.
   - Else: build one child GO with `TextMeshPro`, font = `_fontOverride ?? TMP_Settings.defaultFontAsset`, text = resolved pseudo.
   - `LateUpdate` (only while active): reposition at `anchor.position + up * _worldHeightOffset`, billboard by aligning forward with `Camera.main.forward`.
   - Visibility toggled via `NameplatePolicy.ShouldShow(false, isFirstPerson)` on channel events (and guarded each `LateUpdate`).
   - Live name via `playerInfos.OnListChanged`. Unsubscribe channel + list in `OnDisable`/`OnDestroy` (null-guarded).
3. **`Assets/Scripts/Tests/Editor/Avatars/NameplatePolicyTests.cs`** — EditMode, `Tests.Editor` asmdef (already refs `Game`).

## Acceptance criteria (Given/When/Then)

1. **AC1 — others only.** Given a match with ≥2 real players, When the local player is in first-person, Then a nameplate showing the correct pseudo floats above every OTHER player's avatar and NONE above the local player's own.
2. **AC2 — first-person gate.** Given first-person is active (`FreeRoam` or Embodied+`SeatedFirstPersonLive`), When it turns off (board overview / non-first-person), Then all nameplates hide; and reappear when it turns on again.
3. **AC3 — billboard + anchor.** Given a visible nameplate, When the camera moves, Then the text stays legible (faces the camera) and stays pinned above the avatar's head (`EyePivot`), following the avatar as it moves.
4. **AC4 — live name.** Given a player's `PlayerInfo` name resolves/changes after the plate is built, When `playerInfos.OnListChanged` fires, Then the plate text updates.
5. **AC5 — no network mutation.** The feature adds no RPC / NetworkVariable / server-state write; bots (`clientId >= 100`, which get no avatar) produce no nameplate.
6. **AC6 — policy tested.** `NameplatePolicy.ShouldShow` truth table + `ResolveLabel` (null/empty/whitespace/trim) covered by green EditMode tests.

## Tasks

- [ ] T1 — `NameplatePolicy.cs` (POCO).
- [ ] T2 — `NameplatePolicyTests.cs` (EditMode).
- [ ] T3 — `AvatarNameplate.cs` (component).
- [ ] T4 — **Unity wiring (deferred — needs Editor on this branch, NOT the worktree):**
  add `AvatarNameplate` to `Assets/Prefabs/Avatars/PlayerAvatar.prefab`; wire `_cameraModeChannel`
  to the existing `CameraModeChannel` asset (same one `AvatarCameraArbiter` uses); leave
  `_anchorOverride` empty (defaults to `EyePivot`). Verify TMP default font asset is set in
  Project Settings → TextMeshPro (else wire `_fontOverride`).
- [ ] T5 — **Verify (deferred to Editor):** `read_console` clean; `run_tests` EditMode (policy)
  green; 2-build playtest — two real clients, confirm each sees the other's pseudo in
  first-person only, none over own, follows head, updates live.

## Constraints / notes

- **Worktree limit:** Unity MCP is bound to the main checkout, not this worktree — T1–T3
  written here as `.cs`; compile, prefab wiring (T4), and tests (T5) run when Unity opens
  this branch. Nothing here can be green-verified in the worktree.
- Placeholder visuals per project convention (UI look is design-owned + provisional) — all
  sizes/colors serialized, no palette invented.
- 3D `TextMeshPro` writes depth → occluded by geometry (acceptable/wanted for a table scene).
