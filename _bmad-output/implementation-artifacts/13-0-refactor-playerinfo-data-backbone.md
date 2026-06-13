# Story 13.0: Refactor the PlayerInfo / player-data backbone (groundwork) `# REVIEW-REQUIRED`

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer (Poyo),
I want the hand-made `PlayerInfo` / `LocalPlayerInfoHolder` / `LobbyPlayerInfoHolder` system simplified and de-crufted **without changing any current feature**,
so that adding the personalization variables (appearance) later is a one-line change and the data can be updated at runtime.

> **Epic 13 — groundwork story (13.0), runs before 13.1 polish.** This is a **behavior-preserving refactor** of a year-old hand-made system. Motivation: the appearance/personalization variables (FR8 / DO6, landing in a later 13.x story) will live in this data. Today, adding one variable means editing 4–6 places and there is **no runtime update path**. This story fixes the maintainability and adds the update seam — it does **not** add any personalization variable yet (that is a later story).

## Acceptance Criteria

1. **`PlayerInfo` boilerplate is killed (behavior-preserving).** The hand-written `Equals` / `GetHashCode` are removed in favor of compiler-generated value equality (prefer `record struct`), and the dead `if (serializer.IsWriter) {} else {}` blocks are removed (`PlayerInfo.cs:24-29`). The type stays an `unmanaged`, `IEquatable<PlayerInfo>`, `INetworkSerializable` value type (the `NetworkList<PlayerInfo>` constraint) and **keeps the same public fields** so every consumer's `.playerName` / `.playerClientId` / `.playerFullName` / `.playerSteamId` access is unchanged.
   - ⚠️ **Verify `record struct` compiles in Unity 6000.2.6f2** (`read_console`) before committing to it. If it does not (C# 10 feature; Unity's Roslyn may reject it), fall back to a plain `struct` keeping a single tidy `IEquatable` impl — the other ACs do **not** depend on `record struct`.
2. **Every current feature is preserved, bit-for-bit.** The lobby ask→save flow (`AskForPlayerInfoRpc` → `SavePlayerInfoRpc` → `NetworkList<PlayerInfo>`), name display everywhere, `GetPlayerInfo` lookup, disconnect removal, and `AddDebugPlayer` all behave exactly as today. All listed consumers (see Dev Notes) compile and behave identically.
3. **`LocalPlayerInfoHolder` is de-crufted.** Remove the dead Steam wiring (`using Steamworks;` + the commented `//playerSteamId = SteamClient.SteamId.Value`, `LocalPlayerInfo.cs:3,30`); remove or unify the redundant `GetClientData()` (it just returns the `playerInfo` getter, `:34-37`); add a `#if UNITY_EDITOR [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset of the static `playerInfo` (domain-reload-disabled rule — today the default `"Player"+Random` survives across Play sessions). The default-name and name-set behavior is preserved.
4. **Extensibility seam in place.** After this story, **adding a player variable = one field + one serialize line**, with equality handled automatically. Document the **nested `PlayerCustomization` grouping pattern** (a future `public PlayerCustomization customization;` sub-`INetworkSerializable` holding appearance vars) as the home for personalization fields. *(Do not create an empty `PlayerCustomization` now — introduce it with the first real appearance var. The record-struct change alone delivers the one-line-add property.)*
5. **Runtime update seam added (additive, no behavior change).** Add a server path to **update** an existing entry in `playerInfos` by `playerClientId` (find + replace), e.g. `UpdatePlayerInfoServerRpc(PlayerInfo)` reachable from a client `UpdateLocalPlayerInfo()` helper, wrapping `GetSafeRpcTarget` where a target is addressed (NFR5). No production caller is required yet — this is the seam the personalization story consumes. It must be covered by a test.
6. **Steam fields retained.** `playerFullName` and `playerSteamId` are kept even though they are currently write-only (no reader) — they are the Steam identity fields (DO5, Steam not yet wired). Do not delete them; a one-line comment marks them as Steam-reserved/inert.
7. **Guards & tests green.** The `DiSeamGuard` + `StaticSingletonCensusGuardTests` stay green (do not break `LobbyPlayerInfoHolder.instance`'s whitelisted census entry; if you add/rename any `static instance`/`Instance`, update the whitelist with a reason). Extend `LocalPlayerInfoTests` (and add a holder test) to prove: record-struct equality matches the old hand-written semantics, `NetworkList` add/remove/**update**/`GetPlayerInfo` work, and the bot/`AddDebugPlayer` path is unchanged. Full suite green — baseline **EM 205 / PM 148**.

## Tasks / Subtasks

- [x] **Task 1 — `PlayerInfo` value type** (AC: #1, #6)
  - [x] In `Assets/Scripts/Network/Player/PlayerInfo.cs`: convert to `record struct` (verify compile in Unity 6000.2.6f2 — `read_console`). Keep the four public fields (same names/types) so consumer field access is unchanged. → **`record struct` REJECTED: `CS8773 — record structs not available in C# 9.0`** (Unity 6000.2.6f2). Fell back to plain `struct` per AC#1 caveat; the four public fields are kept identical.
  - [x] Delete the hand-written `Equals(PlayerInfo)` / `Equals(object)` / `GetHashCode` (compiler-generated by `record struct`). If `record struct` fails to compile, keep a single `IEquatable<PlayerInfo>.Equals` + `GetHashCode` and drop only the dead code. → kept ONE tidy `IEquatable` impl (Equals/Equals(object)/GetHashCode), all 4 fields.
  - [x] Remove the empty `if (serializer.IsWriter) {} else {}` blocks (`:24-29`); keep the four `SerializeValue` lines.
  - [x] Add a one-line comment marking `playerFullName` / `playerSteamId` as Steam-reserved (inert until DO5).
- [x] **Task 2 — `LocalPlayerInfoHolder` cleanup** (AC: #3)
  - [x] `Assets/Scripts/Network/Player/LocalPlayerInfo.cs`: drop `using Steamworks;` and the commented SteamId line; unify the API (remove redundant `GetClientData()` OR make `playerInfo` the single accessor — update the one caller if any; `GetClientData` has no production caller per grep). → `GetClientData()` removed (grep confirmed zero production callers); `playerInfo` is the single accessor.
  - [x] Add `#if UNITY_EDITOR [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset()` restoring the default `playerInfo` (mirror `CharacterManager.cs:75-81`). Preserve the `"Player"+Random` default semantics. → `ResetStaticsForDomainReloadDisabled()`; default centralised in `BuildDefault()` so initializer + reset agree.
  - [x] Keep `CreateNewClientData(string)` (used by `LoginMenu.cs:145`) and the `#`-discriminator split behavior intact.
- [x] **Task 3 — Update seam on `LobbyPlayerInfoHolder`** (AC: #5)
  - [x] Add a server-side update: find the entry whose `playerClientId` matches and replace it in `playerInfos` (NetworkList: `RemoveAt` + `Add`, or index-set — mirror the disconnect scan `LobbyPlayerInfoHolder.cs:78-88`). Server-authoritative. → `UpdatePlayerInfo(PlayerInfo)`: `IsServer`-gated in-place index-set replace; idempotent (unknown id = no-op).
  - [x] Expose it via `UpdatePlayerInfoServerRpc` / a client helper, wrapping `characterManager.GetSafeRpcTarget(...)` if a specific target is addressed (NFR5; same pattern as `AskForPlayerInfo`, `:95-98`). Idempotent: updating a non-existent clientId is a no-op (don't add a phantom). → `UpdateLocalPlayerInfo()` client helper + `[Rpc(SendTo.Server)] UpdatePlayerInfoServerRpc`. `SendTo.Server` addresses no specific target → no `GetSafeRpcTarget` to wrap (same shape as `SavePlayerInfoRpc`); guard #1 NOT tripped (does not touch `characterManager`/`CompositionRoot`).
  - [x] No production caller required — this is the personalization seam.
- [x] **Task 4 — Verify consumers compile & behave** (AC: #2)
  - [x] Confirm the readers below still compile unchanged (record struct keeps public fields): `UlongExtensions.GetPlayerName`, `Character.cs:177`, `ChatPanel.cs:173`, `PCardsShuffling.cs:89-90`, `PBlessing.cs:60`, `PHighPriorityBounty.cs:54`, `PTruthChains.cs:55`, `ConnectedPlayerPanel.cs:36-45`, `PlayerButtonObject.cs:25-32`. → plain struct also keeps the public fields → whole game assembly compiles 0 errors.
  - [x] Confirm the mutate-by-copy writers still work: `PseudoInputField.cs:25-27`, `LobbyPlayerInfoHolder.AskForPlayerInfoRpc`, `AddDebugPlayer`. → kept MUTABLE struct fields (no `readonly`/`with`), so all copy-mutate-write sites are unchanged.
- [x] **Task 5 — Tests** (AC: #7)
  - [x] Extend `Assets/Scripts/Tests/Editor/LocalPlayerInfoTests.cs`: equality parity (two equal infos `.Equals` and hash-equal; differ by each field → not equal — proves the generated equality matches the old hand-written set), `CreateNewClientData` `#`-split, default name. → +6 EditMode tests (5 equality parity + reset-restores-default).
  - [x] Add a holder test (EditMode if pure list logic can be isolated; else PlayMode via `NetworkTestHelper`) for `GetPlayerInfo`, add/remove, and the **new update** (replace-by-clientId, no-op on unknown id). → PlayMode `LobbyPlayerInfoHolderTests` (+4): add+get, update-replace, update-unknown-no-op, disconnect-remove. List logic is NetworkList-bound (needs spawn) → PlayMode.
  - [x] `mcp__UnityMCP__read_console` after each change; `mcp__UnityMCP__run_tests` EditMode (guards must stay green) + PlayMode. Baseline EM 205 / PM 148. → **EM 211/211, PM 152/152**, all green (DiSeamGuard + StaticSingletonCensusGuard green).
- [x] **Task 6 — Boot smoke** (AC: #2)
  - [x] Enter Play, host a lobby, confirm player name shows in `ConnectedPlayerPanel` / chat as before; add a debug/simulated player and confirm its name; no NRE, console clean. → Play enter/exit clean, 0 console errors (exercises the new `LocalPlayerInfoHolder` SubsystemRegistration reset at Play entry). ⚠️ The connect/save/lookup + add/update/remove flow is proven by the new PlayMode host tests; the **visual** lobby confirmation (name rendered in `ConnectedPlayerPanel`/chat) is golden-blind UI choreography → wants a Poyo playtest.

## Dev Notes

### Why this is "mal fait" today (the root cause)

The data is a **flat hand-rolled serializable struct**. Adding one variable touches: (1) the field, (2) `NetworkSerialize`, (3) `Equals`, (4) `GetHashCode`, (5) every `new PlayerInfo{...}` site, (6) any reader. The hand-written `Equals`/`GetHashCode` (`PlayerInfo.cs:32-48`) are pure boilerplate that the compiler can generate. And there is **no update path** — `playerInfos` only gets `Add` on connect / `Remove` on disconnect (`LobbyPlayerInfoHolder.cs:74-115`), so a mid-session appearance change has nowhere to go. Those two facts are exactly what makes personalization painful.

### The system, end to end (read before touching)

- **`PlayerInfo`** (`Network/Player/PlayerInfo.cs`) — the replicated DTO: `playerName`, `playerFullName`, `playerClientId`, `playerSteamId`. `INetworkSerializable` + `IEquatable` + `unmanaged` (required by `NetworkList<PlayerInfo>`).
- **`LocalPlayerInfoHolder`** (`Network/Player/LocalPlayerInfo.cs`) — a **static** holder of THIS client's own `PlayerInfo` (the local profile, like a settings blob). Default `"Player"+Random`. Set by `LoginMenu` (`CreateNewClientData`, `LoginMenu.cs:145`) and `PseudoInputField` (`:25-27`).
- **`LobbyPlayerInfoHolder`** (`Network/LobbyPlayerInfoHolder.cs`) — server-replicated `NetworkList<PlayerInfo> playerInfos`. On a client connect the server RPC-asks that client for its `LocalPlayerInfoHolder.playerInfo`, the client replies, the server appends (`:90-115`). Lookups are linear scans (`GetPlayerInfo`, `:117-129`). `instance` is a **whitelisted census survivor** (recorded §4; `:21`).

### Consumer surface (behavior-preserving target — all must keep working)

Readers (all read `.playerName` except where noted):
- `UlongExtensions.GetPlayerName` (`:25-27`) — the static name resolver used widely.
- `Character.cs:177` (owner pseudo), `ChatPanel.cs:173`, `PCardsShuffling.cs:89-90`, `PBlessing.cs:60`, `PHighPriorityBounty.cs:54`, `PTruthChains.cs:55`.
- `ConnectedPlayerPanel.cs:20,36,45` (subscribes `playerInfos.OnListChanged`, iterates), `PlayerButtonObject.cs:25-32` (iterates, reads `.playerClientId` + `.playerName`).

Writers / construction:
- `LocalPlayerInfoHolder` default + `CreateNewClientData`; `PseudoInputField.cs:25-27` (copy-mutate-write); `LobbyPlayerInfoHolder.AskForPlayerInfoRpc:103-105`; `LobbyPlayerInfoHolder.AddDebugPlayer:131-142` (`new PlayerInfo{...}`), called by `CharacterManager.cs:389`.

`record struct` keeps these as public fields → **all reads compile unchanged**. Mutate-by-copy writers also keep working with mutable record-struct fields. Only if you choose `readonly record struct` + `with` do the 3 writer sites change (small, cleaner — your call; mutable fields = least churn).

### Design target (recommended)

- `public record struct PlayerInfo : INetworkSerializable, IEquatable<PlayerInfo>` with the same 4 public fields; compiler generates `Equals`/`GetHashCode`/`==`; you keep the manual `NetworkSerialize` (NGO can't auto-gen it). **Net effect: new var = 1 field + 1 serialize line, equality free.**
- Future grouping (document, don't build now): a nested `public PlayerCustomization customization;` where `PlayerCustomization : INetworkSerializable, IEquatable<>` holds appearance vars; `PlayerInfo.NetworkSerialize` calls `serializer.SerializeValue(ref customization)`; record-struct equality auto-includes it. Lands with the first appearance var (a later 13.x story).
- Update seam on the holder so personalization can replicate at runtime (AC #5).
- **Verify `record struct` support first** — if Unity 6000.2.6f2's compiler rejects it, fall back to a plain `struct` with one tidy `IEquatable` impl; keep all other ACs.

### Guard / census safety (don't trip CI)

- `LobbyPlayerInfoHolder.instance` is a **whitelisted** entry in `StaticSingletonCensusGuardTests` — leave it as-is. If you add or rename any `static instance`/`Instance`, the census guard fails until you whitelist it with a reason.
- `LobbyPlayerInfoHolder` is in `DiSeamMigratedConsumers.NoLocatorOnly` (`:146`) — it must keep resolving `CharacterManager` via `CompositionRoot.For(NetworkManager)` (lane C, `:48`), never `.instance`/`.For(` directly (guard #1 forbids those substrings). Your new update RPC must use the same resolved `characterManager` field, not a fresh locator lookup.

### NGO / project rules that bind this story

- Server-authoritative state: `playerInfos` is mutated **server-side only**; clients propose via `ServerRpc` (the existing `SavePlayerInfoRpc` and your new `UpdatePlayerInfoServerRpc`).
- `NetworkList<T>` requires `T : unmanaged, IEquatable<T>` — `record struct` over `FixedString64Bytes`/`ulong` satisfies it (FixedString is unmanaged). Verify no managed field sneaks in.
- `FixedString*` for networked strings (already used) — never managed `string` in the DTO.
- `OnListChanged` subscribers (`ConnectedPlayerPanel`) must keep working; don't change the list's element semantics.
- Domain reload disabled → reset the `LocalPlayerInfoHolder` static (AC #3).
- Prefer `mcp__UnityMCP__manage_script` for any brand-new `.cs` (avoids the silent compile-exclusion gotcha, `reference_unity_silent_compile_exclusion`); edits to existing files use `Edit`.

### Scope guardrails

- **No new personalization variable** in this story (keep features identical). Only the structure + update seam.
- **No persistence** (PlayerPrefs/save) — that's a feature add, out of scope unless Poyo asks.
- **No Steam wiring** — fields stay inert (DO5).
- `# REVIEW-REQUIRED`: NGO replication + ~10 consumers + value-equality change → run `gds-code-review` on the diff before merge (the project's network-touching gate).

### Project Structure Notes

- Files stay where they are (`Network/`, `Network/Player/`), `Game` asmdef. No new system/folder. Naming/serialization conventions unchanged.
- Variance: introducing `record struct` is a small modernization; gated on the compile-verify caveat above.

### Project Context Rules (from `_bmad-output/project-context.md`)

- Server-authority strict; clients propose via `ServerRpc`. `GetSafeRpcTarget` / `IsLocalOrSimulated` semantics preserved verbatim (NFR5).
- `FixedString*` for networked strings; `NetworkVariable`/`NetworkList` mutated by event, never per frame.
- `[SerializeField] private`; rename serialized only with `[FormerlySerializedAs]` (N/A here — `PlayerInfo` fields are plain public struct fields, not serialized-in-inspector).
- Domain reload disabled → reset mutable statics via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- Async = `UniTask` (no `Task`); Audio = FMOD (not relevant here).
- Unity MCP: `read_console` after every change; `run_tests` filtered before done.

### References

- [Source: Assets/Scripts/Network/Player/PlayerInfo.cs] — the DTO (dead `IsWriter` blocks, hand-written equality).
- [Source: Assets/Scripts/Network/Player/LocalPlayerInfo.cs] — static local holder (dead Steam, redundant `GetClientData`, no reset).
- [Source: Assets/Scripts/Network/LobbyPlayerInfoHolder.cs] — replicated list + ask/save flow + lane-C CharacterManager + whitelisted `instance`.
- [Source: Assets/Scripts/Extensions/UlongExtensions.cs#17-28] — the widely-used name resolver.
- [Source: Assets/Scripts/Characters/CharacterManager.cs#75-81,389] — statics-reset pattern + `AddDebugPlayer` caller.
- [Source: Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs#146 + StaticSingletonCensusGuardTests.cs] — guards/whitelist to keep green.
- [Source: _bmad-output/planning-artifacts/epics-player-embodiment.md] — Epic 13 (FR8/DO6 personalization motivation).
- [Source: _bmad-output/project-context.md] — NGO/server-authority/statics/testing rules.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- **`record struct` compile probe (AC#1)** — first attempt with `public record struct PlayerInfo` failed:
  `Assets/Scripts/Network/Player/PlayerInfo.cs(25,19): error CS8773: Feature 'record structs' is not available in C# 9.0. Please use language version 10.0 or greater.` Unity 6000.2.6f2 compiles at C# 9.0; bumping `langversion` is out of scope/risky for a behaviour-preserving story, so the AC#1 documented fallback (plain `struct` + one tidy `IEquatable`) was taken.
- First EditMode run: 1 failure — `Reset_RestoresPlayerPrefixedDefault` self-guard used seed `"Custom#42"` whose `#`-split short name IS literally `"Custom"`, so the `AreNotEqual("Custom", …)` guard was wrong. Fixed the guard to `AreEqual("Custom", …)` (custom name correctly set pre-reset). Re-run green.

### Completion Notes List

- **AC#1 (boilerplate killed) — delivered via fallback.** `record struct` rejected by Unity's C# 9 compiler (CS8773); `PlayerInfo` stays a plain `struct` with the dead `if (serializer.IsWriter){}else{}` blocks removed and a single tidy `IEquatable<PlayerInfo>` impl (Equals/Equals(object)/GetHashCode over all 4 fields). Public fields unchanged → every consumer's `.playerName`/`.playerClientId`/`.playerFullName`/`.playerSteamId` access compiles unchanged.
- **AC#2 (behaviour preserved bit-for-bit).** Mutable struct fields kept (no `readonly`/`with`), so the copy-mutate-write sites (`PseudoInputField`, `AskForPlayerInfoRpc`, `AddDebugPlayer`) are untouched. Wire order in `NetworkSerialize` unchanged. Whole game assembly compiles 0 errors; full suite green.
- **AC#3 (`LocalPlayerInfoHolder` de-crufted).** Dropped `using Steamworks;` + the commented SteamId line; removed the redundant `GetClientData()` (grep: zero production callers); added the `#if UNITY_EDITOR [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] ResetStaticsForDomainReloadDisabled()` restoring a fresh `"Player####"` default (centralised in `BuildDefault()`). `CreateNewClientData` + `#`-split preserved.
- **AC#4 (extensibility seam) — partial vs the literal "one-line-add".** Because `record struct` is unavailable, equality is hand-written, so a new var is `field + SerializeValue line + 1 Equals term + 1 HashCode.Combine arg` (the C#9-best, not the literal one-liner). The **nested `PlayerCustomization` grouping pattern** is documented in `PlayerInfo.cs` as the personalization home so per-variable churn lands in one place from the first appearance var on. No empty `PlayerCustomization` created (per AC#4).
- **AC#5 (runtime update seam).** Added `UpdatePlayerInfo(PlayerInfo)` (server-auth, `IsServer`-gated, in-place NetworkList index-set replace-by-clientId, idempotent no-op on unknown id) + `[Rpc(SendTo.Server)] UpdatePlayerInfoServerRpc` + `UpdateLocalPlayerInfo()` client helper (stamps `LocalClient.ClientId`, mirrors `AskForPlayerInfoRpc`). `SendTo.Server` addresses no specific target → no `GetSafeRpcTarget` to wrap (same shape as `SavePlayerInfoRpc`); NFR5 honoured. No production caller (the personalization seam).
- **AC#6 (Steam fields retained).** `playerFullName` / `playerSteamId` kept, each one-line-commented Steam-reserved/inert (DO5).
- **AC#7 (guards & tests green).** `LobbyPlayerInfoHolder.instance` census whitelist untouched; no new `static instance`/`Instance` added (the new `LocalPlayerInfoHolder` reset is a method, not an accessor). `LobbyPlayerInfoHolder` stays in `NoLocatorOnly` and the new update RPC uses no locator → DiSeamGuard #1 stays green. EM **211/211** (was 205, +6), PM **152/152** (was 148, +4); DiSeamGuard + StaticSingletonCensusGuard green; GameScene Play enter/exit boot smoke clean (0 errors).
- **Review handoff (`# REVIEW-REQUIRED`).** NGO replication touch + value-equality change → run `gds-code-review` on the diff before merge. ⚠️ Visual lobby confirmation (name rendered in `ConnectedPlayerPanel`/chat) is golden-blind UI → wants a Poyo playtest.

### File List

- `Assets/Scripts/Network/Player/PlayerInfo.cs` (modified) — plain `struct` (record-struct fallback); dead `IsWriter` blocks removed; single tidy `IEquatable`; Steam-reserved comments; nested-grouping pattern documented.
- `Assets/Scripts/Network/Player/LocalPlayerInfo.cs` (modified) — dropped Steamworks using + commented SteamId; removed `GetClientData()`; `BuildDefault()` + editor `SubsystemRegistration` reset.
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs` (modified) — added `UpdateLocalPlayerInfo()` / `UpdatePlayerInfoServerRpc` / `UpdatePlayerInfo(PlayerInfo)` runtime-update seam.
- `Assets/Scripts/Tests/Editor/LocalPlayerInfoTests.cs` (modified) — +6 EditMode tests (equality parity ×5, reset-restores-default ×1).
- `Assets/Scripts/Tests/PlayMode/LobbyPlayerInfoHolderTests.cs` (added) — +5 PlayMode tests (add+get, update-replace, update-unknown-no-op, disconnect-remove, + review-F4 RPC-seam end-to-end).

## Change Log

| Date | Version | Description |
|---|---|---|
| 2026-06-13 | 0.1 | Story 13.0 implemented: PlayerInfo de-boilerplated (plain-struct fallback — `record struct` rejected by Unity C# 9, CS8773), LocalPlayerInfoHolder de-crufted + SubsystemRegistration reset, runtime-update seam on LobbyPlayerInfoHolder. EM 211/211, PM 152/152, guards + boot smoke green. Status → review (`# REVIEW-REQUIRED`). |
| 2026-06-13 | 0.2 | gds-code-review (3 adversarial layers): 5 patches applied — F1 `UpdateLocalPlayerInfo` pre-connect NRE guard; F2 server-authority — `UpdatePlayerInfoServerRpc` stamps `Receive.SenderClientId` (anti-impersonation); F3 `Equals` no-op short-circuit; F4 end-to-end RPC-seam test; F5 reset-test null-check. 3 deferred (pre-existing: duplicate-clientId first-match, FixedString truncation, leading-`#` short name). ~8 dismissed. Re-gated EM 211/211, PM 153/153, guards green. Status → done. |

### Review Findings (gds-code-review, 2026-06-13)

3 adversarial layers (Blind Hunter / Edge Case Hunter / Acceptance Auditor). Auditor: no AC violated; the substantive items are on the new update seam. 0 decision-needed, 5 patch, 3 defer, ~8 dismissed.

**Patch (all resolved 2026-06-13 — applied + re-gated EM 211/211, PM 153/153):**

- [x] [Review][Patch] `UpdateLocalPlayerInfo` NREs before connect — reads `NetworkManager.LocalClient.ClientId` with no `IsSpawned`/null guard; a 13.x UI caller invoking it pre-connect crashes [Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:UpdateLocalPlayerInfo] (blind+edge, High) → **FIXED**: `if (!IsSpawned || NetworkManager == null || NetworkManager.LocalClient == null) return;`.
- [x] [Review][Patch] Update RPC trusts the client-supplied `playerClientId` — a client can overwrite ANOTHER client's replicated entry (impersonation); stamp `rpcParams.Receive.SenderClientId` server-side so a client can only update its own [Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:UpdatePlayerInfoServerRpc] (blind+edge, Med-High; server-authority hard rule) → **FIXED**: RPC now `(PlayerInfo, RpcParams)` and stamps `playerInfo.playerClientId = rpcParams.Receive.SenderClientId`; the public `UpdatePlayerInfo` stays the server/bot arbitrary-id path. Proven by the new F4 seam test (asserts the host's own id 0 is the one replaced).
- [x] [Review][Patch] `UpdatePlayerInfo` index-set replicates even on an unchanged value — short-circuit with the new `Equals` to avoid `OnListChanged` spam if the personalization UI updates redundantly [Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:UpdatePlayerInfo] (edge, Med) → **FIXED**: `if (!playerInfos[i].Equals(_info)) playerInfos[i] = _info;`.
- [x] [Review][Patch] AC#5 client-helper/RPC chain untested — the tests call `UpdatePlayerInfo` directly, never `UpdateLocalPlayerInfo()`→`UpdatePlayerInfoServerRpc`; add a host test that drives the seam end-to-end [Assets/Scripts/Tests/PlayMode/LobbyPlayerInfoHolderTests.cs] (blind+auditor, Med) → **FIXED**: `UpdateLocalPlayerInfo_DrivesRpcSeam_ReplacesOwnEntryByStampedSenderId` (PM 152→153) exercises helper→RPC→server-stamp→replace end-to-end.
- [x] [Review][Patch] Reset test dereferences `GetMethod(...)` without a null check — renaming the private reset throws NRE instead of a clear failure; assert non-null first [Assets/Scripts/Tests/Editor/LocalPlayerInfoTests.cs:Reset_RestoresPlayerPrefixedDefault] (blind, Low) → **FIXED**: `Assert.IsNotNull(resetMethod, …)` before `Invoke`.

**Defer (pre-existing, not caused by 13.0):**

- [x] [Review][Defer] Duplicate `playerClientId` entries — update/get/disconnect act on the FIRST match only [Assets/Scripts/Network/LobbyPlayerInfoHolder.cs] — deferred, pre-existing (Save/AddDebugPlayer `Add` with no uniqueness; Get/Disconnect already first-match; 13.0's update is consistent with them)
- [x] [Review][Defer] `FixedString64Bytes` truncation makes two long names share a 63-byte prefix compare equal [Assets/Scripts/Network/Player/PlayerInfo.cs] — deferred, pre-existing (fields were always FixedString64Bytes; equality always compared them)
- [x] [Review][Defer] `CreateNewClientData` leading-`#` yields an empty short name, untested [Assets/Scripts/Network/Player/LocalPlayerInfo.cs] — deferred, pre-existing `#`-split behavior unchanged by 13.0

**Dismissed (noise / false-positive / handled):** reset `#if UNITY_EDITOR`-only (spec-mandated, Task 2); `GetClientData` removal (grep + clean compile = zero callers); `Equals` field reorder (inert, same 4-field set); AC#7 baseline counts unverifiable from diff (verified at runtime — EM 211/PM 152 green); SetUp shared static (handled by captured `before` counts + teardown, project pattern); update id-0-before-entry-exists "lost update" (= the spec-mandated idempotent no-op, AC#5); public `UpdatePlayerInfo` host bypass (gated by `IsServer` + closed by the SenderClientId patch); disconnect-test reflection brittleness (mirrors project `ReflectionHelper` pattern).
