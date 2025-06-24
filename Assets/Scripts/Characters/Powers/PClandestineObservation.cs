using System;
using System.Collections.Generic;
using System.Linq;
using ChatSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PClandestineObservation : Power
    {
        public RoleID targetRoleID;
        
        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(SubscribeToNightOver),
                new NetworkSerializableObject[] { }, new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new[] { ownerClientId }));
        }
        
        public void SubscribeToNightOver()
        {
            foreach (var _awakeningState in GameManager.instance.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndClient += OnNightOver;
            }
        }

        public void OnNightOver()
        {
            List<Character> _targetedCharacters = GameManager.instance.GetCharacters(false)
                .Where(_c => _c.role.roleID == targetRoleID).ToList();

            if (_targetedCharacters.Count == 0)
            {
                ChatManager.instance.AddMessageLocal($"Aucun personnage n'a le rôle {targetRoleID.ToString()} ciblé.",
                    GameValues.CHAT_SERVER_CLIENT_ID);
                return;
            }

            List<TargetingData> _targetingDataList = new();
            foreach (var _targetedCharacter in _targetedCharacters)
            {
                _targetingDataList.Concat(RoleTargetSystem.instance.GetAllTargetingDataForTarget(_targetedCharacter.ownerClientId));
            }
            
            ChatManager.instance.AddMessageLocal($"Total de personne qui ont ciblé le rôle \"{_targetedCharacters[0].role.roleName}\": {_targetingDataList.Count}",
                GameValues.CHAT_SERVER_CLIENT_ID);
        }

        public override void NetworkSerialize<T>(BufferSerializer<T> _serializer)
        {
            base.NetworkSerialize(_serializer);
            _serializer.SerializeValue(ref targetRoleID);
        }
    }
}