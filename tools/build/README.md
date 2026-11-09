# Local CI (no GitHub Actions)

`local_ci.py` does on this PC what the GitHub Actions workflows did (Actions was blocked by a GitHub billing lock,
2026-11-07), with the same Discord messages, field for field. One loop (`watch`, every 2 min) for all of them:

| Workflow (now `workflow_dispatch` only) | Local job |
|---|---|
| `Build.yml` | each new `origin/Main` head: Relay + Steam builds, below |
| `DevPush.yml` | each new `origin/Dev` head: « ⚠️ Push direct sur Dev » (webhook Dev), the pushed commits, compare link |
| `DevPROpened.yml` | each new PR into Dev (GitHub API through `gh`): « 📥 Nouvelle PR », its commits |
| `DevMergeNotif.yml` | each PR into Dev merged: « ✅ PR mergée », author and merger |
| `fmod-sync.yml` | a Dev push or PR head touching `FMODProject/`, `Assets/FMODBanks/`, `tools/fmod/banks.py`: `banks.py diff` on that commit (`git archive`, no checkout), result as a commit status |
| `unity-tests.yml` | was already off (`if: false`): nothing |

The build runs in its own process, so notifications keep flowing during a build.

## Builds

| Step | How |
|---|---|
| Trigger | `watch` polls `origin/Main` every 2 min (started at logon by `CdP Local CI.cmd` in the Startup folder) |
| « 🔨 Builds démarrées » | webhook **Dev** (#dev), author = commit author, full commit message |
| Builds | Unity in batchmode, `CdpBuild.BuildFromCommandLine -cdpFlavor relay\|steam -cdpLogs on -buildVersion 0.0.<commits>` (the version game-ci used), in its own checkout `<repo>-ci` (detached worktree, its Library kept between builds), below normal priority |
| Files | zips `CorruptionDuPortail_b<n>.zip` / `CorruptionDuPortail_Steam_b<n>.zip` (inside: `StandaloneWindows64/`, like the old artifact) on Google Drive, `Corruption du Portail - Builds/b<n>/`, shared by link; Unity logs in `b<n>/logs/` |
| Changelog | `claude -p` (Opus), then `codex exec`, then `gemini -p`, with `.github/changelog-prompt.md`, commits since the last successful build |
| « ✅ Builds réussies » / « ⚠️ Une build sur deux » | webhook **Builder** (#builds), download links = Drive |
| « ❌ Builds échouées » | webhook **Dev**, link = the build's Drive folder (logs) |
| Cleanup | the 3 newest builds stay on Drive, older ones deleted (not trashed) |

Commands: `python tools/build/local_ci.py watch | build [sha] | status | install`.
State and logs: `%LOCALAPPDATA%\CdpLocalCI\` (`state.json`: last build number, last successful sha; `local_ci.log`;
`logs/b<n>-<flavor>.log`).

Setup (done once on Poyo's PC, 2026-11-09):
- `.env` (gitignored): `DISCORD_DEV_WEBHOOK`, `DISCORD_BUILD_WEBHOOK` (the webhooks « Dev » and « Builder » of the
  workflow, read through the bot: `GET /guilds/{id}/webhooks`).
- rclone remote `gdrive` (`rclone config create gdrive drive scope=drive.file`, account azertizeryt@gmail.com):
  sees only the files it created. rclone warns its shared Google client id retires during 2026: when uploads stop,
  create an own client id (https://rclone.org/drive/#making-your-own-client-id) and `rclone config update gdrive`.
- `python tools/build/local_ci.py install`: starts the watcher with the PC's own Python (`pythonw`, not a venv)
  and puts `CdP Local CI.cmd` in the Startup folder (an ONLOGON scheduled task needs admin rights).

The PC must be on (and logged in) for a build to happen; a push while it is off is built at the next start.
`Build.yml` only runs by hand now (`workflow_dispatch`), so the two never post twice.
