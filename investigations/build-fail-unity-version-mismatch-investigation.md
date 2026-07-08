# Investigation — CI Build failure after Unity 6000.5 upgrade

## Hand-off Brief
The `Build` workflow failed on `Main` (run 28890408645, merge of PR #66) because the project was upgraded to Unity `6000.5.0f1` but the CI workflows still pin `unityVersion: 6000.2.6f2`. The old editor image lacks the builtin modules `manifest.json` now requests (`adaptiveperformance`, `physicscore2d`, `vectorgraphics`), so package resolution aborts with exit 1. Fix = bump the pinned version in `Build.yml:73` and `unity-tests.yml:56` to `6000.5.0f1`.

## Case Info
- Slug: build-fail-unity-version-mismatch
- Date: 2026-07-07
- Trigger: PR #66 (Dev → Main) merge push → `Build` workflow
- Failing run: https://github.com/UnPseudoTotalementNormal/CorruptionDuPortail/actions/runs/28890408645 (failure, 5m4s)
- Status: Concluded

## Problem Statement
"build échouée, pourquoi ?" — the Unity player build (StandaloneWindows64) on `Main` fails.

## Evidence Inventory
| Item | Status | Ref |
|---|---|---|
| Failing CI log | Confirmed | run 28890408645, job 18:51:49Z |
| Project Unity version | Confirmed | ProjectSettings/ProjectVersion.txt → 6000.5.0f1 |
| CI pinned version | Confirmed | .github/workflows/Build.yml:73, unity-tests.yml:56 → 6000.2.6f2 |
| Manifest module versions | Confirmed | Packages/manifest.json (adaptiveperformance/physicscore2d/vectorgraphics @1.0.0) |
| Upgrade commit | Confirmed | 96b0a1f "chore: upgrade to Unity 6000.5.0f1" |

## Confirmed Findings
- CI log (18:51:49Z):
  ```
  com.unity.modules.adaptiveperformance: Package [com.unity.modules.adaptiveperformance@1.0.0] cannot be found
  com.unity.modules.physicscore2d:      Package [com.unity.modules.physicscore2d@1.0.0] cannot be found
  com.unity.modules.vectorgraphics:     Package [com.unity.modules.vectorgraphics@1.0.0] cannot be found
  com.unity.adaptiveperformance (dependency): Package [com.unity.adaptiveperformance@6.0.0] cannot be found
  Build failed, with exit code 1
  ```
- Project = `6000.5.0f1`; both CI workflows pin `6000.2.6f2`.

## Deduced Conclusions
- The 6000.2.6f2 GameCI editor image does not ship the builtin modules introduced/renamed in 6000.5, so it cannot satisfy the manifest → resolution failure → exit 1.
- The upgrade commit bumped `ProjectVersion.txt` but not the CI workflow pins.

## Refuted / Noise
- `[Licensing::Module] Error: Access token is unavailable` and `Gtk-CRITICAL … gtk_label_set_text` — editor spin-up noise, not the failure cause (exit 1 fires right after the package-resolution block).

## Source Code Trace
- Error origin: package manager resolution (Unity editor 6000.2.6f2 image)
- Trigger: `game-ci/unity-builder@v4` with `unityVersion: 6000.2.6f2`
- Condition: `Packages/manifest.json` requests 6000.5-era builtin modules
- Related files: `.github/workflows/Build.yml:73`, `.github/workflows/unity-tests.yml:56`, `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`

## Final Conclusion — confidence High
Version mismatch between project (6000.5.0f1) and CI-pinned editor (6000.2.6f2).

## Fix direction
Bump `unityVersion` to `6000.5.0f1` in both `Build.yml:73` and `unity-tests.yml:56`. Confirm a GameCI editor image exists for 6000.5.0f1; if not, the Library cache key and image tag need review.

## Reproduction / Verification Plan
1. Edit both pins to `6000.5.0f1`.
2. Push to a branch → re-run `Build`; expect package resolution to pass and build to complete.
