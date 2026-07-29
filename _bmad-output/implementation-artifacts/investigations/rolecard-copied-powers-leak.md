# Case: Copied powers leak in the RoleCard (all power-copy roles)

## Hand-off Brief (15s read)

The RoleCard enumerates `role.powers`, which includes powers granted at runtime. Ugues (Marque
d'Hurluberluges) and Luma (Mélange des cartes) tag their copies with the networked `isStolenCopy` flag, so
the RoleCard filter added on RoleCardController now hides them — **verified correct**. L'Incomplet
(Réincarnation) and any Héritage (PLegacy) role grant powers **without** that flag, so their granted powers
**still leak** in the RoleCard — the fix does NOT cover them.

## Case Info

- Slug: `rolecard-copied-powers-leak`
- Date: 2026-07-16
- Origin: Discord bug "Faire que les pouvoirs copié ne s'affiche pas lors des descriptions de rôle" (Bug,
  Haute Priorité). Follow-up scope: verify the fix + extend to l'Incomplet and Luma.
- Status: **Concluded** — Ugues/Luma verified fixed; l'Incomplet fix applied; Legacy out of scope (Poyo).

## Resolution (2026-07-16)

Poyo's calls: (1) hide L'Incomplet's reincarnated powers = YES; (2) Héritage/PLegacy = out of scope.

Applied:
- Added networked marker `NetworkVariable<bool> hideFromRoleCardRuntime` on Power (Power.cs) — distinct from
  `isStolenCopy` so reincarnation grants keep normal power-bar / stealable semantics.
- PReincarnation.GrantRolePowers now passes `ConfigureGrantedPower` onReady, setting the marker on each
  granted power (PReincarnation.cs).
- RoleCard filter extended to `&& !p.hideFromRoleCardRuntime.Value` on both builders (RoleCardController.cs).
- PLegacy left untouched (out of scope).

Compile + tests deferred (worktree has no Unity MCP). Pending: a PlayMode test covering the RoleCard power
filter would lock this in.

## Follow-up: 2026-07-16 #2 — Réincarnation shows as a passive in its own card

Poyo flagged: `isPassive=true` (set post-reincarnation) is a mechanic hack that leaked into the RoleCard —
the Réincarnation power jumped to the passive section after use, and it should stay shown as its authored
active power.

- **Confirmed** the live `isPassive` flip is intentional + tested-locked: it disables re-use
  (PowerUsability.cs:58, Power.cs:279), skips awakening (AwakeningState.cs:384), hides from the power bar
  (PowersBar.cs:126) — and EntrapmentPowerTests.cs:267 asserts `isPassive==true` after reincarnation. So the
  flip stays; only its RoleCard side effect is wrong.
- Root cause: the RoleCard categorized active/passive by the *runtime-mutated* `isPassive`, whereas
  Réincarnation is authored active (ReincarnationDecision.IsPassive => false; prefab `isPassive: 0`).

Applied (presentation-only, mechanic untouched):
- Power.cs: added `[NonSerialized] public bool authoredIsPassive`, snapshotted in a new
  `protected virtual void Awake()` (runs at instantiation on host + clients, before any RPC flip).
- PInfiniteMessage.cs + PPersonalBeacons.cs: their `private void Awake()` now `protected override` calling
  `base.Awake()`, so the snapshot isn't skipped (they were the only Power subclasses defining Awake).
- RoleCardController.cs: both builders now categorize by `authoredIsPassive` instead of the live `isPassive`.

Result: a spent Réincarnation stays in the ACTIVE section of L'Incomplet's card; genuine passives unchanged
(authored==runtime for every power except Réincarnation). Live `isPassive` still drives usability / awakening
/ power bar — no gameplay change.

## Follow-up: 2026-07-16 #3 — Design locked by EditMode tests

Extracted the "what shows in the RoleCard, in which section" rule into a pure classifier so the design is
unit-testable (no NGO / UI Toolkit), mirroring StolenPowerSelector / PowerUsability.

- New `RoleCardPowerVisibility.Classify(...) -> RoleCardSlot { Hidden, ActivePill, PassiveRow }`
  (Assets/Scripts/Domain/RoleCardPowerVisibility.cs) — the single source of truth for the ordered rules
  (hideFromRoleCard → isStolenCopy → hideFromRoleCardRuntime → authoredIsPassive section → empty-desc passive skip).
- RoleCardController now routes both builders through `SlotOf(power)` instead of inline `.Where(...)`
  predicates; the empty-description passive skip moved into the classifier.
- EditMode tests: Assets/Scripts/Tests/Editor/RoleCardPowerVisibilityTests.cs — full truth table incl. the
  `SpentReincarnationPower_StaysActivePill` regression guard and the three hide-flag precedence cases.
- PlayMode test: Assets/Scripts/Tests/PlayMode/EntrapmentPowerTests.cs ::
  `PReincarnation_RoleCard_HidesGrantsKeepsReincarnationActive` — end-to-end over the real grant flow (host NM).
  Proves what EditMode can't: (a) the granted clone carries `hideFromRoleCardRuntime.Value == true`
  (PReincarnation's onReady ran through GivePowerToCharacter's spawn/reparent), (b) the Réincarnation power's
  `authoredIsPassive` stays false after the live `isPassive` flips true (Awake snapshot on a real spawned
  NetworkBehaviour), then routes both REAL powers through RoleCardPowerVisibility.Classify → grant Hidden,
  Réincarnation ActivePill.
- asmdefs already reference `CorruptionDuPortail.Domain` (Game line 29, Tests.Editor line 8, Tests.PlayMode
  line 13) — no asmdef change.

Verification still deferred (worktree, no Unity MCP). When Unity opens the branch: run the
`RoleCardPowerVisibility` category (EditMode). Watch [[reference_unity_silent_compile_exclusion]] — the two
new .cs were created via Write (not Unity), so confirm they land in their assemblies (tests actually run, no
CS0246).

## Problem Statement

Opening a character's RoleCard (click a character in the CharactersBar) lists that role's active + passive
powers. When a power was copied/granted at runtime, showing it leaks (a) the role is real (a factice decoy
never copies), and (b) which power/role was copied.

## Evidence Inventory

| Item | State | Cite |
|---|---|---|
| RoleCard enumerates `role.powers` | Confirmed | Assets/Scripts/UI/RoleCard/RoleCardController.cs:234,263 |
| Fix filters `!p.isStolenCopy.Value` (both builders) | Confirmed | RoleCardController.cs:237,266 |
| `isStolenCopy` is `NetworkVariable<bool>` (replicates to all clients) | Confirmed | Assets/Scripts/Characters/Powers/Power.cs:102 |
| Ugues copies set `isStolenCopy=true` | Confirmed | PMarqueHurluberluges.cs:121 (ConfigureStolenCopy) |
| Luma copies set `isStolenCopy=true` | Confirmed | PCardsShuffling.cs:189 (ConfigureCopy) |
| L'Incomplet = Réincarnation power | Confirmed | Incomplet.asset:107 guid == Reincarnation.prefab.meta guid `eeb80e99…` |
| Reincarnation grants powers WITHOUT flag | Confirmed | PReincarnation.cs:26 (GivePowerToCharacter, no onReady) |
| PLegacy grants `legacyPower` WITHOUT flag | Confirmed | PLegacy.cs:53 (GivePowerToCharacter, no onReady) |
| Initial authored powers ALSO flow through GivePowerToCharacter | Confirmed | RoleAttributionState.cs:153 |
| `isStolenCopy` has OTHER consumers (semantic weight) | Confirmed | PowersBar.cs:156 (hide spent copy); PCardsShuffling.cs:153 + PMarqueHurluberluges.cs:95 (steal-eligibility) |

## Confirmed Findings

### The four runtime power-grant paths

| Role / power | Script | Sets `isStolenCopy`? | Hidden by fix? |
|---|---|---|---|
| **Ugues** — Marque d'Hurluberluges | PMarqueHurluberluges | ✅ ConfigureStolenCopy | ✅ yes |
| **Luma** — Mélange des cartes (fake card) | PCardsShuffling.GrantCopyFromFakeRole | ✅ ConfigureCopy | ✅ yes |
| **L'Incomplet** — Réincarnation | PReincarnation.GrantRolePowers | ❌ no callback | ❌ **still leaks** |
| **Héritage** (roleForLegacy chained) | PLegacy.GrantLegacy | ❌ no callback | ❌ **still leaks** |

- **Ugues + Luma: verification PASS.** Both call `GivePowerToCharacter(..., ConfigureCopy)` which sets the
  networked `isStolenCopy.Value = true`. That NetworkVariable replicates to every client, so the RoleCard
  filter (`!p.isStolenCopy.Value`) hides the copies on the inspecting client. The added `index++` runs
  after the filter, so pill numbering stays contiguous.

- **L'Incomplet: verification FAIL.** Réincarnation grants ALL powers of the picked chosen role via a bare
  `GivePowerToCharacter` (no onReady). Those powers land in `role.powers` (same path that produced the
  original Ugues bug) with `isStolenCopy=false`, so the filter passes them through → the granted powers show
  in L'Incomplet's RoleCard → same leak.

- **Héritage (PLegacy): same failure class** (bonus finding, not in the user's ask). The inherited
  `legacyPower` is granted flagless → leaks in the heir's RoleCard.

### Why the fix can't just be reused / hoisted

- **Can't reuse `isStolenCopy` for reincarnation/legacy.** That flag means "one-shot copied power" and drives
  two other behaviours: PowersBar hides a stolen copy once spent (PowersBar.cs:156), and the steal-eligibility
  kernel treats an `isStolenCopy` power as non-stealable. Reincarnation/legacy grants are **permanent, full
  powers**, not one-shot copies — tagging them `isStolenCopy` would wrongly make them vanish from the power
  bar when used up and become un-stealable.
- **Can't flag at the `GivePowerToCharacter` chokepoint.** Initial authored powers are assigned through the
  SAME method (RoleAttributionState.cs:153), so a blanket flag there would hide every power. The signal must
  be set per-grant by the granting power, exactly like `isStolenCopy` is.

## Fix Direction (for gds-quick-dev — needs Poyo's call first)

Root cause: "power was granted at runtime and must be hidden from the RoleCard" needs a **networked** signal,
and reincarnation/legacy grants set none.

Minimal option: add a dedicated networked marker on Power, e.g. `NetworkVariable<bool> hideFromRoleCardRuntime`
(distinct from `isStolenCopy` to avoid the power-bar / steal side effects), set it in an onReady callback on
PReincarnation.GrantRolePowers and PLegacy.GrantLegacy (mirror of ConfigureCopy but flag-only), then extend
the RoleCard filter to `!p.isStolenCopy.Value && !p.hideFromRoleCardRuntime.Value`.

**Design questions for Poyo (design-owned):**
1. Should L'Incomplet's reincarnated powers be hidden from its RoleCard (confirm same secrecy intent as Ugues/Luma)?
2. Same question for Héritage (PLegacy) — include it, or out of scope?

## Reproduction / Verification Plan

- Repro (l'Incomplet): start a game with L'Incomplet, reincarnate into a chosen role, open L'Incomplet's
  RoleCard from another client → granted powers are listed (leak).
- Verify Ugues/Luma fix: open Ugues' / Luma's RoleCard after a steal → copied powers absent, numbering
  contiguous.
- No PlayMode test currently covers the RoleCard power filter — a good add-on when the fix lands.

## Final Conclusion

**Confidence: High.** The fix is Confirmed correct for Ugues and Luma (both set the networked `isStolenCopy`).
It is Confirmed insufficient for L'Incomplet (Réincarnation) and Héritage (PLegacy), which grant powers without
any networked hide flag and therefore still leak in the RoleCard.
