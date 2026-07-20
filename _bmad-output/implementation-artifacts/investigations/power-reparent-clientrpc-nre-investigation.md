# Investigation: NullReferenceException in Power.OnReparentedClientRpc (client-only)

## Hand-off Brief

1. **What happened.** A client (not the host) throws `NullReferenceException` at `Power.OnReparentedClientRpc` (Power.cs:430) when a power copy is reparented — the `SendTo.Everyone` RPC dereferences `_newParentCharacter.role.powers` with **no null-guard on `_newParentCharacter`**, and the character lookup returns null on the remote client. **Confirmed** origin, **Deduced** cause.
2. **Where the case stands.** Root cause structurally confirmed: unguarded deref in a network RPC that races replicated state on remotes; the sister RPC in the same subsystem (`PowerManager.RemovePowerFromCharacterPowerListRpc`, PowerManager.cs:147) guards exactly `_character == null || _character.role == null`. The precise reason the client lookup returns null (spawn/reparent-vs-RPC ordering, or a not-yet-resolvable target character) is the one open item.
3. **What's needed next.** Add the same null-guard (`_newParentCharacter`, plus the trailing unguarded derefs at 435–436) → `gds-quick-dev`. Optionally confirm the exact null with one tagged log before fixing.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                         |
| Date opened      | 2026-07-19                                                                  |
| Status           | Active                                                                      |
| System           | Windows player build (Client), Unity 6000.x, NGO; profile "hey", lobby "mah" |
| Evidence sources | Player.log stack trace (client build), source code, git (commit 023f1379 / #90) |

## Problem Statement

Player ran a Client build (not host). Development Console shows two identical `NullReferenceException`. Player.log stack: `Characters.Powers.Power.OnReparentedClientRpc … Power.cs:430` → rethrown as "Unhandled RPC exception!". Host reportedly had **no** error. Scene involves a Mage with temporary copy powers on the bar.

## Evidence Inventory

| Source              | Status    | Notes                                                                              |
| ------------------- | --------- | ---------------------------------------------------------------------------------- |
| Client Player.log   | Available | Two identical NRE at Power.cs:430, via NGO RpcMessageHelpers.Handle. Client role.  |
| Host log            | Missing   | User reports host had no error; not captured. Would confirm asymmetry.             |
| Source code         | Available | Power.cs, PowerManager.cs, CharacterManager.cs, RoleAttributionState.cs read.      |
| Runtime repro logs  | Missing   | No tagged instrumentation of `_newParentId` / lookup result on the client.         |

## Timeline of Events

| Time  | Event                                                                                  | Source              | Confidence |
| ----- | -------------------------------------------------------------------------------------- | ------------------- | ---------- |
| t0    | Server reparents a power copy (fresh Spawn → TrySetParent → ownerClientId.Value set)   | GivePowerToCharacter / ReparentPowerToCharacterServer | Confirmed |
| t1    | Server fires `OnReparentedClientRpc(oldId, newId)` `SendTo.Everyone`                    | Power.cs:417        | Confirmed  |
| t2    | Client handles RPC; `GetCharacter(newId)` returns null; deref at 430 → NRE (×2)         | Player.log          | Confirmed  |

## Confirmed Findings

### Finding 1: NRE origin is an unguarded deref in a SendTo.Everyone RPC

**Evidence:** Power.cs:430 —
```csharp
if (!_newParentCharacter.role.powers.Contains(this))   // _newParentCharacter not null-checked
```
`_newParentCharacter = characterManager.GetCharacter(_newParentId, false)` (Power.cs:424). The prior line guards `_oldParentCharacter` with `if (_oldParentCharacter)` (425) but `_newParentCharacter` is dereferenced with no guard. Lines 435–436 then deref **both** `_oldParentCharacter` and `_newParentCharacter` unguarded again.

**Detail:** `[Rpc(SendTo.Everyone)]` (Power.cs:420) runs on every peer including remote clients. The stack confirms the throw path is the NGO RPC handler on a client build.

### Finding 2: The lookup matches by ownerClientId over a self-healing cache

**Evidence:** CharacterManager.cs:330 `GetCharacter → GetCharacters().FirstOrDefault(_c => _c.ownerClientId.Value == _characterId)`; cache rebuilt from `networkedCharacters` NetworkList (RebuildCharactersCache, 150–184), which *skips references still spawning* and stays dirty until resolved. A remote client can therefore transiently have no Character matching `_newParentId`.

### Finding 3: The sister RPC in the same subsystem already guards this exact case

**Evidence:** PowerManager.cs:143–150 — `RemovePowerFromCharacterPowerListRpc` is also `SendTo.Everyone` and opens with:
```csharp
Character _character = characterManager.GetCharacter(_characterId);
if (_character == null || _character.role == null) return;
```
with a comment that a `SendTo.Everyone` RPC races the Despawn/spawn on remotes. `OnReparentedClientRpc` omits the identical guard.

### Finding 4: `role` is replicated to all clients (so `.role` is not the usual null)

**Evidence:** RoleAttributionState.cs:156 `GiveRoleToCharacterRpc(id, _character.role)` is `SendTo.Everyone` with the real `Role` (CharacterManager.cs:348–361). Role secrecy is a UI-layer concern; the `Role` object exists on every client post-attribution. ⇒ the null at 430 is most likely `_newParentCharacter` itself, not `.role`.

## Deduced Conclusions

### Deduction 1: Client-only because the host never fails the lookup

**Based on:** Findings 1, 2, 4.

**Reasoning:** On the host, game state is authoritative and synchronous — the new-parent Character is always present in the cache when the RPC self-invokes, and `role` is set. On a remote client the RPC can arrive while the target Character (or the freshly-spawned copy carrying the RPC) is still resolving in `networkedCharacters`, so `GetCharacter(_newParentId)` returns null → deref throws.

**Conclusion:** The asymmetry (host OK / client NRE) is explained by a missing remote-race guard, not by faction/role secrecy.

## Hypothesized Paths

### Hypothesis 1: `_newParentCharacter` is null due to spawn/reparent-vs-RPC ordering

**Status:** Confirmed (2026-07-19, revised) — this is the mechanism, with H2's fake-id as the *why this character specifically*.

**Resolution:** Initial read (deterministic → not a race) was wrong: the `charCount=2` in the log is a *snapshot at the throw instant*, not proof of permanent absence. Per Poyo (game-owner): every peer does receive the bot Character (it is board-/vote-visible), it is simply **not yet replicated on the client at the moment the reparent RPC fires** (game start, immediately after role distribution). The fake-client / bot Character (owner `ulong.MaxValue`) is the one whose replication lags relative to the fire-and-forget `OnReparentedClientRpc`; the real players (id 0/1) are already synced, which is why only the bot's powers fail. So: a replication-ordering race (H1) whose victim is the fake-id character (H2).

**Theory:** `GivePowerToCharacter` spawns a NEW power NetworkObject then reparents and immediately fires the RPC (CharacterManager.cs:495–512 → OnPowerReparentComplete → OnReparentedServer → RPC). On the remote client the RPC may run before the target Character is resolvable in the cache, or before the copy's own spawn settles.

**Would confirm:** A tagged log on the client printing `_newParentId` and `GetCharacter(_newParentId)==null` at the moment of throw, correlated with the copy-give path.

**Would refute:** Client log shows a valid, already-spawned Character for `_newParentId` at throw time (would point at `.role`/`.powers` instead).

### Hypothesis 2: `_newParentId` targets a character not replicated on this client (bot / eliminated / disconnected)

**Status:** Confirmed (2026-07-19)

**Theory:** Copy grants cast `(ulong)_ownerSlot` (PMarqueHurluberluges.cs:110, PReincarnation.cs:46, PLegacy.cs:53); a chain-reparent targets `_potentialLegacyHolder[0]` (PCReparentOnChain.cs:60). If the target id has no resolvable Character on this specific client, the lookup is null.

**Would confirm:** The logged `_newParentId` corresponds to a slot with no client-side Character.

**Would refute:** `_newParentId` is a normal, present player character.

**Resolution:** Confirmed by runtime log — `_newParentId = 18446744073709551615 = ulong.MaxValue = GameValues.FAKE_CLIENT_ID` (GameValues.cs:5) — a **bot / fake client** (`CharacterManagerRoster.cs:12`: "fake ids (ulong.MaxValue-n)"). The bot Character *does* eventually replicate to the client (board-/vote-visible per Poyo); it is just **not yet in the client roster when the reparent RPC fires** (`charCount=2` snapshot vs host `charCount=3`) → `GetCharacter(FAKE_CLIENT_ID)` returns null → NRE. Refinement of H1, not a competing cause: the fake-id character is the specific victim of the H1 replication race because its spawn/sync lags at game start. Only the bot's own role powers (`Soin Baveux`, `Savoir de la corruption`) crash; real players' powers (newId 0/1) resolve on both peers.

## Missing Evidence

| Gap                                   | Impact                                             | How to Obtain                                                       |
| ------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------ |
| Client value of `_newParentId` + lookup result at throw | Distinguishes H1 (race) vs H2 (unresolvable target) | Add a `[REPARENT]`-tagged Debug.Log in OnReparentedClientRpc, repro, read Player.log |
| Host log for the same moment          | Confirms host took the same path without null      | Capture host Player.log during repro                               |

## Source Code Trace

| Element       | Detail                                                                                          |
| ------------- | ----------------------------------------------------------------------------------------------- |
| Error origin  | `Assets/Scripts/Characters/Powers/Power.cs:430`, `Power.OnReparentedClientRpc`                   |
| Trigger       | Server reparents a power (copy grant / chain legacy) → `OnReparentedServer` (Power.cs:409–418) fires `OnReparentedClientRpc` `SendTo.Everyone` |
| Condition     | On a remote client, `GetCharacter(_newParentId, false)` returns null (target Character not resolvable at RPC time); unguarded deref at 430 (and 435–436) |
| Related files | PowerManager.cs (ReparentPowerToCharacterServer:111, RemovePowerFromCharacterPowerListRpc:143 — the guarded sibling), CharacterManager.cs (GetCharacter:328, GivePowerToCharacter:485), RoleAttributionState.cs:156 |

## Conclusion

**Confidence:** High (Confirmed root cause, runtime-log repro).

**Confirmed:** `OnReparentedClientRpc` (`[Rpc(SendTo.Everyone)]`, Power.cs:420) dereferences `_newParentCharacter` (line 430) and `_oldParentCharacter`/`_newParentCharacter` (lines 435–436) with no null-guard. At game start, powers of the **bot / fake-client** character (owner `ulong.MaxValue` = `GameValues.FAKE_CLIENT_ID`) are reparented and the fire-and-forget RPC runs on every peer. On a real remote client the bot Character is board-visible and **eventually** replicated, but is **not yet in the roster at the instant the RPC fires** (client `charCount=2` vs host `charCount=3`) → `GetCharacter(FAKE_CLIENT_ID)` returns null → NRE. A replication-ordering race (H1) whose victim is the lagging fake-id character (H2); real players' powers (id 0/1) already synced, so they never crash. Host never fails (it holds the simulated bot synchronously). `.role` is not the culprit (roles replicate to all clients).

**Convergence is safe with a plain null-guard:** `Character.CheckForPowersRpc` (Character.cs:116, `SendTo.Everyone`) rebuilds `role.powers` from `GetComponentsInChildren<Power>()` — it re-adds any reparented Power child independently of `OnReparentedClientRpc`, and fires from `GiveRoleToCharacterAsync` (CharacterManager.cs:360, which itself awaits the character spawn) and `AskForRoleUpdateRpc`. So a client that skips the `Add` because the bot wasn't synced yet still converges once the bot + its Power children arrive. No defer/retry needed. (The old-parent `Remove` is not self-healed by CheckForPowersRpc, but real reparents already remove via the guarded `PowerManager.RemovePowerFromCharacterPowerListRpc` before reparenting, PowerManager.cs:127; and these game-start gives have no meaningful old parent.)

## Recommended Next Steps

### Fix direction

Guard the remote-race in `OnReparentedClientRpc` the same way the sibling RPC does. Deref nothing until each character is confirmed non-null:

```csharp
[Rpc(SendTo.Everyone)]
public virtual void OnReparentedClientRpc(ulong _oldParentId, ulong _newParentId)
{
    var _oldParentCharacter = characterManager.GetCharacter(_oldParentId, false);
    var _newParentCharacter = characterManager.GetCharacter(_newParentId, false);

    if (_oldParentCharacter != null)
    {
        _oldParentCharacter.role?.powers.Remove(this);
        _oldParentCharacter.InvokeOnPowersUpdated();
    }

    if (_newParentCharacter != null && _newParentCharacter.role != null)
    {
        if (!_newParentCharacter.role.powers.Contains(this))
            _newParentCharacter.role.powers.Add(this);
        _newParentCharacter.InvokeOnPowersUpdated();
    }

    onPowerReparented?.Invoke();
}
```

Note: this also fixes the pre-existing unguarded re-derefs at 435–436. Caveat — if H1 (race) is the true cause, a client that *drops* the add because the character wasn't resolvable yet must still converge later. Verify the copy still lands in the new owner's `role.powers` on that client (an `AskForRoleUpdateRpc` / powers-refresh path likely re-syncs it); if not, the fix should defer/retry rather than silently no-op.

### Diagnostic

Before or alongside the fix, add a `[REPARENT]` tagged log printing `_oldParentId`, `_newParentId`, and whether each `GetCharacter` resolved, in `OnReparentedClientRpc`. Repro the Mage copy-power flow on a 2-machine (host + client) session, filter the client log on `[REPARENT]` — confirms H1 vs H2.

## Reproduction Plan

Host + one real client (the loopback 2-NM test harness cannot exercise this — it needs a genuine remote spawn ordering). Mage acquires a temporary copy power (Marque d'Hurluberluges / Luma fake-card / copied Réincarnation) so a power is reparented via `GivePowerToCharacter`. Observe: **client** throws NRE at Power.cs:430 (×1 per reparented copy); host clean. Expected after fix: no NRE, copy appears in the new owner's bar on both peers.

## Side Findings

- Power.cs:435–436 dereference `_oldParentCharacter` and `_newParentCharacter` unguarded *after* the truthiness `if` at 425 — a second latent NRE on the same RPC even when the 430 guard is added piecemeal. Fix both together.
- `GetCharacter` default `_triggerUpdate = true` starts an end-of-frame coroutine; the RPC correctly passes `false`. No issue, noted for callers that don't.
