#region

using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Characters;
using Characters.Powers;
using Network;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

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
            Dictionary<RoleDataObject, RoleAttributionSetting> _rolesToAttribute = roleAttributionDictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            //remove roles that have no roleToAttribute
            foreach (KeyValuePair<RoleDataObject, RoleAttributionSetting> _roleToAttribute in _rolesToAttribute.ToList())
            {
                if (_roleToAttribute.Value.roleToAttribute <= 0)
                {
                    _rolesToAttribute.Remove(_roleToAttribute.Key);
                }
            }
            
            float _fakeRoleAmountToRemove = Mathf.Abs(gameManager.characterManager.GetCharacters().Count - roleAttributionDictionary.Values.Sum(setting => setting.roleToAttribute));

            Dictionary<RoleDataObject, RoleAttributionSetting> _fakeRoles = _rolesToAttribute
                .Where(_roleToAttribute => _roleToAttribute.Value.canBeFake)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            //remove fake roles from dictionary
            for (int i = 0; i < _fakeRoleAmountToRemove; i++)
            {
                if (_fakeRoles.Count == 0)
                {
                    break;
                }
    
                GiveRandomRole(_fakeRoles, gameManager.characterManager.CreateNewFakeCharacter(), out RoleDataObject _removedRole);
                if (_removedRole)
                {
                    _rolesToAttribute.Remove(_removedRole);
                }
            }

            //give random roles to character
            foreach (Character _character in gameManager.characterManager.GetCharacters().Where(_c => !_c.isFake).ToList())
            {
                GiveRandomRole(_rolesToAttribute, _character, out RoleDataObject _removedRole);
            }
            
            gameManager.NextGameState();
        }

        private void GiveRandomRole(Dictionary<RoleDataObject, RoleAttributionSetting> _rolesToAttribute, Character _character, out RoleDataObject _removedRole)
        {
            _removedRole = null;
            int _randomRoleIndex = Random.Range(0, _rolesToAttribute.Count);
            RoleDataObject _randomRole = _rolesToAttribute.Keys.ToList()[_randomRoleIndex];
            RoleAttributionSetting _randomRoleSettings = _rolesToAttribute[_randomRole];

            
            if (_character != null)
            {
                Role _newRole = (Role)_randomRole.role.Clone();
                _character.role = _newRole;
                _character.role.ownerClientId = _character.ownerClientId;
                
                foreach (var _powerDataObject in _randomRole.powers)
                {
                    Power _newPower = (Power)_powerDataObject.power.Clone();
                    _newPower.ownerClientId = _character.ownerClientId;
                    _newPower.powerGameId = (ulong)Random.Range(int.MinValue, int.MaxValue) ^ (ulong)Random.Range(int.MinValue, int.MaxValue);
                    _character.role.powers.Add(_newPower);
                }
                
                gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateCharacterRpc),
                    new NetworkSerializableObject[] { new(_character) },
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
            }
            _randomRoleSettings.roleToAttribute -= 1;
            if (_randomRoleSettings.roleToAttribute <= 0)
            {
                _rolesToAttribute.Remove(_randomRole);
                _removedRole = _randomRole;
            }
        }

        private void UpdateCharacterRpc(Character _character)
        {
            if (gameManager.IsServer)
            {
                return;
            }

            gameManager.characterManager.GetCharacters().Add(_character);
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