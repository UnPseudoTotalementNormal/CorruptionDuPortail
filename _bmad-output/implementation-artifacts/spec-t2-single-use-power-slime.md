---
title: 'T2 — slime mark on single-use (stolen copy) power objects'
type: 'feature'
created: '2026-10-07'
status: 'done'
baseline_commit: '4a0a859a'
context: ['{project-root}/_bmad-output/project-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A power copied for a single use (Luma's « Mélange des cartes » copy, Ugës's Marque d'Hurluberluges copies, a stolen Réincarnation's one-shot grants) looks exactly like a normal power on the table, so the player cannot tell it disappears once used (board T2, and GD wish on T1: « une bave gluante sur les objets »).

**Approach:** When the local power bar builds a 3D power object whose power is a one-shot copy (`Power.isStolenCopy`), coat its model with a glossy translucent slime overlay material and attach slow slime drips (particle system emitting from the model's mesh). Client-only cosmetic, no network change. Look is provisional, tunable on assets by the GD.

## Boundaries & Constraints

**Always:** Marker keyed on `isStolenCopy` only (one-shot lifetime), evaluated per visual rebuild. Overlay appended to `sharedMaterials` (no material instance leak). Slime tint lives on the material asset (placeholder, GD-tunable), not in code. Outline layers (hover / used / highlight) keep working on marked objects. New serialized fields appended, wired on `PowerBarObject3D.prefab`, read back.

**Ask First:** Any player-facing text (tooltip line). Any change to network state, power lifetime or `PowersBar` rebuild logic.

**Never:** Mark l'Incomplet's permanent grants (`isCopiedPower` without `isStolenCopy`). VFX Graph (crashes CI), `AudioSource`. Touch the RoleCard filter. (New ShaderGraph: allowed since Poyo asked for one on 2026-10-07, see Spec Change Log.)

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| One-shot copy | bar object built for power with `isStolenCopy = true` | model renderers get slime overlay as last material; drip particles child plays, emitting from model mesh | N/A |
| Normal / permanent copy | `isStolenCopy = false` | model unchanged, no drips | N/A |
| Rebuild | `Init()` called again (SetPower) | old visual destroyed with its drips; new visual marked once (no double overlay) | N/A |
| Spent copy | `powerUseLeft` 0 → animate out | drips scale out with the object and are destroyed with it | N/A |
| Assets missing | coat material or drip prefab null | mark skipped for the missing part, no exception | silent skip |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs` -- instantiates the visual model (`Init`), owns outline layers; hook point.
- `Assets/Scripts/Board/UI/PowerBar/PowersBarObject.cs` -- base; `SetPower` caches `wasStolenCopy`.
- `Assets/Scripts/Board/UI/PowerBar/PowersBar.cs` -- builds bar objects, animates out spent copies (unchanged).
- `Assets/Scripts/Characters/Powers/Power.cs` -- `isStolenCopy` (set by `ConfigureAsOneShotStolenCopy` before the bar sees the copy).
- `Assets/Prefabs/PowersBar/PowerBarObject3D.prefab` -- wire the new fields.
- `Assets/Prefabs/PowersBar/3DModels/BasePower.prefab` -- the only power model (one MeshRenderer, URP Lit, scale 100).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Board/UI/PowerBar/SingleUseSlimeMark.cs` (new, via Unity) -- static `Apply(Transform visual, Material coat, GameObject dripPrefab)`: append coat to each renderer's `sharedMaterials` (skip ParticleSystemRenderer), instantiate drip prefab under visual, point its shape at the first Mesh/SkinnedMeshRenderer; null-safe -- testable seam.
- [x] `PowerBarObject3D.cs` -- append `[SerializeField] Material singleUseCoatMaterial; [SerializeField] GameObject singleUseDripPrefab;`; in `Init` after the visual is built, call `Apply` when `power.isStolenCopy.Value` -- marks the object.
- [x] `Assets/Art/Materials/Powers/SingleUseSlimeCoat.mat` -- URP Lit transparent, smoothness ~0.95, placeholder translucent tint -- the wet coat.
- [x] `Assets/Prefabs/PowersBar/SingleUseSlimeDrips.prefab` + `SingleUseSlimeDrip.mat` -- ParticleSystem: MeshRenderer shape, world space, gravity, low rate, small drops, URP particle material -- the drips.
- [x] `PowerBarObject3D.prefab` -- wire both fields, read back.
- [x] `Assets/Scripts/Tests/Editor/SingleUseSlimeMarkTests.cs` (via Unity) -- matrix rows: marked / unmarked / rebuild no double / null assets.

**Acceptance Criteria:**
- Given a night where Ugës holds Marque copies (or Luma holds a fake-card copy), when the owner looks at his power objects, then copies show the slime coat and drips and his normal powers don't.
- Given a marked copy is used, when it leaves the bar, then it shrinks out with its drips and nothing is left behind.
- Given the change, when EditMode suite + SceneWiringGuard run, then all green and no console error.

- 2026-10-07, Poyo renegotiated the intent after the first DM (« C'est un shader la bave ? » → « Tu peux faire un shadergraph oui »):
  the coat is now a Shader Graph `Corruption/SingleUseSlime` (`Assets/Art/Shaders/SingleUseSlime.shadergraph`, URP Lit,
  transparent, no shadow casting, world-space fragment normal) whose logic is `SingleUseSlime.hlsl` (two file Custom
  Function nodes): jelly shell inflated over the model that breathes, hanging drips pulled along world gravity on side
  faces (grow then reset per column), flowing domain-warped thickness with holes, wet bumps (normal from the thickness
  gradient), trapped bubbles, fresnel rim + inner glow. All look values are material properties of
  `SingleUseSlimeCoat.mat` (same asset/GUID, prefab wiring unchanged). Checked by offscreen renders from the headless
  editor (plain vs slimed button) before the autoplay. KEEP: overlay as appended shared material, tint on the asset.

## Design Notes

Overlay-by-extra-material: a MeshRenderer with more materials than submeshes redraws the last submesh with the extra one, so a transparent glossy material reads as a wet coat without touching the base material. BasePower has one submesh, so the coat covers the whole model.

## Verification

**Commands:**
- `tools/autoplay/unityctl.sh compile` -- expected: no compile error.
- `tools/autoplay/unityctl.sh editmode` -- expected: all green incl. `SingleUseSlimeMarkTests`, `SceneWiringGuard`.
- autoplay `copies-uges-client` (Relay) with capture of the owner's power bar during night 2 -- expected: slimed copies visible next to plain powers; captures/video sent to Poyo by Discord DM before asking for validation.

## Suggested Review Order

**Marking a one-shot copy**

- Entry point: the mark follows the replicated flag, which flips after the bar first built the copy.
  [`PowerBarObject3D.cs:145`](../../Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs#L145)

- Subscription tracked per power, dropped when SetPower swaps it or the object dies.
  [`PowerBarObject3D.cs:75`](../../Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs#L75)

- Idempotent coat + drips; shared material appended, never instanced.
  [`SingleUseSlimeMark.cs:27`](../../Assets/Scripts/Board/UI/PowerBar/SingleUseSlimeMark.cs#L27)

- Mesh shape only when CPU-readable, else the model bounds (player builds).
  [`SingleUseSlimeMark.cs:130`](../../Assets/Scripts/Board/UI/PowerBar/SingleUseSlimeMark.cs#L130)

**Leaving the bar**

- New hook before the shrink tween; base no-op.
  [`PowersBarObject.cs:96`](../../Assets/Scripts/Board/UI/PowerBar/PowersBarObject.cs#L96)

- Falling drops detached and left to finish, emitter self-destroys.
  [`SingleUseSlimeMark.cs:115`](../../Assets/Scripts/Board/UI/PowerBar/SingleUseSlimeMark.cs#L115)

**Evidence**

- Autoplay probe: coat / live drops per power object of this peer.
  [`AutoplayDriver.cs:1170`](../../Assets/Scripts/Autoplay/AutoplayDriver.cs#L1170)

- Run check: copies marked, normal powers untouched, situation covered.
  [`analyze_slime.py:1`](../../tools/autoplay/analyze_slime.py#L1)

- EditMode cases (coat, idempotence, missing assets, non-readable mesh, TMP/inactive, release).
  [`SingleUseSlimeMarkTests.cs:1`](../../Assets/Scripts/Tests/Editor/SingleUseSlimeMarkTests.cs#L1)
