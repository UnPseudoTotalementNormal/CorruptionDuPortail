#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using Network;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "RoleAttributionState", menuName = "GameStates/RoleAttributionState")]
    public class RoleAttributionState : GameState
    {
        public SerializedDictionary<RoleDataObject, RoleAttributionSetting> roleAttributionDictionary = new();
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();

            // Story 3.3: the selection arithmetic (two-loop draw + shared-count depletion) lives in the
            // pure RoleDistributor POCO; the adapter maps the decided indices back to RoleDataObjects and
            // applies the side effects, preserving the fake-then-real order + the Clone/power/RPC flow.
            IReadOnlyList<RoleDataObject> _frozenOrder = GetFrozenRolePoolOrder();

            var _initialCounts = new List<int>(_frozenOrder.Count);
            var _canBeFake = new List<bool>(_frozenOrder.Count);
            foreach (RoleDataObject _role in _frozenOrder)
            {
                RoleAttributionSetting _setting = roleAttributionDictionary[_role];
                _initialCounts.Add(_setting.roleToAttribute);
                _canBeFake.Add(_setting.canBeFake);
            }

            int _fakeRoleAmountToRemove = (int)Mathf.Abs(characterManager.GetCharacters().Count - roleAttributionDictionary.Values.Sum(setting => setting.roleToAttribute));
            List<Character> _realCharacters = characterManager.GetCharacters().Where(_c => !_c.isFake).ToList();

            RoleDistribution _distribution = new RoleDistributor().Distribute(
                _initialCounts, _canBeFake, _fakeRoleAmountToRemove, _realCharacters.Count, new UnityRandomProvider());

            //assign fake roles to freshly created fake characters (fakes draw first, in order)
            foreach (int _fakeRoleIndex in _distribution.FakeRoleIndices)
            {
                ApplyRole(_frozenOrder[_fakeRoleIndex], characterManager.CreateNewFakeCharacter());
            }

            //assign the remaining draws to the real characters, in processing order
            for (int i = 0; i < _distribution.RealRoleIndices.Count; i++)
            {
                ApplyRole(_frozenOrder[_distribution.RealRoleIndices[i]], _realCharacters[i]);
            }

            gameManager.NextGameState();
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

        // Applies a decided role (RoleDistributor output) to a character: clone, set, give powers, replicate.
        // Side effects only — the selection + count depletion are owned by the POCO (Story 3.3). The order
        // (Clone → role set → ownerClientId → GivePowerToCharacter loop → GiveRoleToCharacterRpc) is preserved.
        private void ApplyRole(RoleDataObject _randomRole, Character _character)
        {
            if (_character)
            {
                Role _newRole = (Role)_randomRole.role.Clone();
                _character.role = _newRole;
                _character.role.ownerClientId = _character.ownerClientId.Value;

                foreach (var _powerDataObject in _randomRole.powers)
                {
                    characterManager.GivePowerToCharacter(_character.ownerClientId.Value, _powerDataObject);
                }

                characterManager.GiveRoleToCharacterRpc(_character.ownerClientId.Value, _character.role);
            }
        }

        private void UpdateCharacterRpc(Character _character)
        {
            if (gameManager.IsServer)
            {
                return;
            }

            characterManager.GetCharacters().Add(_character);
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
        [Range(0, 10)] public int roleToAttribute;
        public bool canBeFake = true;
        
        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref roleToAttribute);
            _serializer.SerializeValue(ref canBeFake);
        }
    }
}