#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using GameLogic.GameSettings;
using Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "RoleAttributionState", menuName = "GameStates/RoleAttributionState")]
    public class RoleAttributionState : GameState
    {
        public SerializedDictionary<RoleDataObject, RoleAttributionSetting> roleAttributionDictionary = new();

        [SerializeField]
        [Tooltip("Faction-minimum rules for the start gate + distribution guarantee. Null ⇒ no faction rules " +
                 "(the scalar coverage/guaranteed-fit rules still apply) — the headless golden harness leaves it null.")]
        private CompositionRuleSet _compositionRules;

        public override void OnStateCreated()
        {
            base.OnStateCreated();

            // NET-07: every peer creates this state, so every peer learns the role pool (RoleID → RoleDataObject)
            // and can rebuild a replicated role locally from its id.
            foreach (RoleDataObject _role in roleAttributionDictionary.Keys)
            {
                RoleRegistry.Register(_role);
            }
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();

            // Story 3.3: the selection arithmetic (two-loop draw + shared-count depletion) lives in the
            // pure RoleDistributor POCO; the adapter maps the decided indices back to RoleDataObjects and
            // applies the side effects, preserving the fake-then-real order + the Clone/power/RPC flow.
            IReadOnlyList<RoleDataObject> _frozenOrder = GetFrozenRolePoolOrder();

            // Quick-dev gamesettings-refonte (2026-06-20): the per-role counts + canBeFake now live on the
            // replicated, server-authoritative gameSettingsManager (pushed lane-B by SetupGameStates), so
            // distribution honours the host's lobby edits. The authored roleAttributionDictionary remains the
            // role POOL + order (GetFrozenRolePoolOrder) + the manager's seed source + a fallback for the
            // standalone test harnesses that build this state without the DI graph (the RoleAssignment golden
            // master) — there the manager is null and the authored counts are used, byte-identical to before.
            var _initialCounts = new List<int>(_frozenOrder.Count);
            var _forced = new List<int>(_frozenOrder.Count);
            var _factions = new List<FactionType>(_frozenOrder.Count);
            foreach (RoleDataObject _role in _frozenOrder)
            {
                RoleAttributionSetting _authored = roleAttributionDictionary[_role];
                _initialCounts.Add(gameSettingsManager != null ? gameSettingsManager.GetRoleCount(_role.role.roleID) : _authored.max);
                _forced.Add(gameSettingsManager != null ? gameSettingsManager.GetForced(_role.role.roleID) : _authored.forced);
                _factions.Add(_role.role.factionType);
            }

            // Derive the total from the SAME per-role sum the gate (BuildCompositionSnapshot) and _initialCounts
            // use — NOT GameSettingsManager.GetTotalRolesToAttribute() (which sums the manager's own _settings
            // list). Keeps fakeCount consistent with the gate's coverage math, so a gate-accepted config can never
            // make the real loop over-draw. Byte-identical in the normal seeded flow (the two sums are equal).
            int _totalRolesToAttribute = 0;
            foreach (int _count in _initialCounts)
            {
                _totalRolesToAttribute += _count;
            }
            int _fakeRoleAmountToRemove = (int)Mathf.Abs(CharacterQuery.GetCharacters().Count - _totalRolesToAttribute);
            List<Character> _realCharacters = CharacterQuery.GetCharacters().Where(_c => !_c.isFake).ToList();

            IReadOnlyList<FactionMinimum> _minimums = _compositionRules != null
                ? _compositionRules.ToMinimums()
                : System.Array.Empty<FactionMinimum>();

            RoleDistribution _distribution = new RoleDistributor().Distribute(
                _initialCounts, _forced, _factions, _minimums, _fakeRoleAmountToRemove, _realCharacters.Count, new UnityRandomProvider());

            //assign fake roles to freshly created fake characters (fakes draw first, in order)
            foreach (int _fakeRoleIndex in _distribution.FakeRoleIndices)
            {
                ApplyRole(_frozenOrder[_fakeRoleIndex], Command.CreateNewFakeCharacter());
            }

            //assign the remaining draws to the real characters, in processing order
            for (int i = 0; i < _distribution.RealRoleIndices.Count; i++)
            {
                ApplyRole(_frozenOrder[_distribution.RealRoleIndices[i]], _realCharacters[i]);
            }

            Loop.NextGameState();
        }

        // [DETERMINISM §3b A] Canonical, drift-free role-pool ordering: the authored
        // SerializedDictionary order. A plain Dictionary's key enumeration order is
        // implementation-defined and can shift after asset reload / removals, so the
        // random *selection* must index into this frozen sequence (filtered to the
        // still-available roles), not into Dictionary.Keys. The selection itself is
        // untouched — only the list it indexes into is now order-stable.
        // Behavior-preserving: SerializedDictionary enumerates in serialized (authored)
        // order, which is exactly the de-facto order the old Dictionary.Keys produced
        // for this add-only-then-remove flow. Frozen now to remove the latent drift.
        internal IReadOnlyList<RoleDataObject> GetFrozenRolePoolOrder()
        {
            return new List<RoleDataObject>(roleAttributionDictionary.Keys);
        }

        // Composition snapshot for the pure CompositionValidator — joins the replicated max/forced (the host's
        // lobby edits, via gameSettingsManager) with each role's authored faction. Falls back to the authored
        // settings when no manager is wired (headless harness), mirroring OnStartStateServer's count sourcing.
        public CompositionSnapshot BuildCompositionSnapshot(int playerCount)
        {
            var _roles = new List<RoleComposition>(roleAttributionDictionary.Count);
            foreach (RoleDataObject _role in GetFrozenRolePoolOrder())
            {
                RoleAttributionSetting _authored = roleAttributionDictionary[_role];
                int _max = gameSettingsManager != null ? gameSettingsManager.GetRoleCount(_role.role.roleID) : _authored.max;
                int _forcedValue = gameSettingsManager != null ? gameSettingsManager.GetForced(_role.role.roleID) : _authored.forced;
                _roles.Add(new RoleComposition(_role.role.factionType, _max, _forcedValue));
            }
            return new CompositionSnapshot(playerCount, _roles);
        }

        // The authoritative start-gate check (LobbyState delegates here). Uses the wired rule set, or no faction
        // rules when unwired (the scalar coverage / guaranteed-fit rules still apply). Shared by the client mirror.
        public CompositionValidation ValidateComposition(int playerCount)
        {
            return CompositionValidator.Validate(BuildCompositionSnapshot(playerCount), GetFactionMinimums());
        }

        // The wired faction rules (or none when unwired). Exposed so the client footer mirror runs the SAME rule
        // set as the server gate — the single source of truth stays this state's CompositionRuleSet.
        public IReadOnlyList<FactionMinimum> GetFactionMinimums()
        {
            return _compositionRules != null ? _compositionRules.ToMinimums() : System.Array.Empty<FactionMinimum>();
        }

        // Applies a decided role (RoleDistributor output) to a character: clone, set, give powers, replicate.
        // Side effects only — the selection + count depletion are owned by the POCO (Story 3.3). The order
        // (Clone → role set → ownerClientId → GivePowerToCharacter loop → replicate) is preserved; NET-07 replaced
        // the replicate step (GiveRoleToCharacterRpc) with the Character.roleId NetworkVariable.
        private void ApplyRole(RoleDataObject _randomRole, Character _character)
        {
            if (_character)
            {
                Role _newRole = (Role)_randomRole.role.Clone();
                _character.role = _newRole;
                _character.role.ownerClientId = _character.ownerClientId.Value;

                foreach (var _powerDataObject in _randomRole.powers)
                {
                    Command.GivePowerToCharacter(_character.ownerClientId.Value, _powerDataObject);
                }

                // NET-07: the role replicates as state (Character.roleId); each peer rebuilds it from RoleRegistry.
                RoleRegistry.Register(_randomRole);
                _character.CheckForPowersLocal();
                _character.CommitRoleServer();
                _character.CheckForPowersRpc();
            }
        }

        private void UpdateCharacterRpc(Character _character)
        {
            if (gameManager.IsServer)
            {
                return;
            }

            CharacterQuery.GetCharacters().Add(_character);
        }
        
        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }

    [Serializable]
    public class RoleAttributionSetting : INetworkSerializable
    {
        // max = pool cap (random draw fills up to it). forced = guaranteed minimum reals (forced ≤ max).
        // [FormerlySerializedAs] remaps the existing serialized `roleToAttribute` int onto `max` (int→int).
        // The old `canBeFake` bool is intentionally dropped; `forced` defaults to 0 (≡ old canBeFake=true,
        // the whole pool is fakeable). Authored assets all had roleToAttribute=0 so the migration is lossless.
        [FormerlySerializedAs("roleToAttribute")]
        [Range(0, 10)] public int max;
        [Range(0, 10)] public int forced;

        // Derived fake eligibility (C1 shim — keeps RoleDistributor's bool-canBeFake path unchanged):
        // a role's non-guaranteed copies (max − forced) are fakeable. forced == max ⇒ 0 fakeable ⇒
        // old canBeFake == false. forced == 0 ⇒ whole pool fakeable ⇒ old canBeFake == true.
        public bool CanBeFake => forced < max;

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref max);
            _serializer.SerializeValue(ref forced);
        }
    }
}