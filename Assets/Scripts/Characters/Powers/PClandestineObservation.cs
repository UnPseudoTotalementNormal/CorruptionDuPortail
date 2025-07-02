using System;
using System.Collections.Generic;
using System.Linq;
using ChatSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;

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
            
            GameManager.instance.GetCharacter(ownerClientId, false).onCharacterAwakened += DeclareAllTargetFocusServer;
        }
        

        public void DeclareAllTargetFocusServer()
        {
            Debug.Log("decalre all target focus");
            if (!CanUse())
            {
                return;
            }
            
            List<Character> _targetedCharacters = GameManager.instance.GetCharacters(false)
                .Where(_c => _c.role.roleID == targetRoleID).ToList();

            if (_targetedCharacters.Count == 0)
            {
                ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID, 
                    $"Total de personne qui ont ciblé le rôle \"{targetRoleID.ToString()}\": 0."),
                    0,
                    NetworkManager.Singleton.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
                return;
            }

            List<TargetingData> _targetingDataList = new();
            foreach (var _targetedCharacter in _targetedCharacters)
            {
                _targetingDataList.AddRange(RoleTargetSystem.instance.GetAllTargetingDataForTarget(_targetedCharacter.ownerClientId));
            }
            
            ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                $"Total de personne qui ont ciblé le rôle \"{_targetedCharacters[0].role.roleName}\": {_targetingDataList.Count}"),
                0,
                NetworkManager.Singleton.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
        }

        public override void NetworkSerialize<T>(BufferSerializer<T> _serializer)
        {
            base.NetworkSerialize(_serializer);
            _serializer.SerializeValue(ref targetRoleID);
        }
    }
}