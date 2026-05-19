using System;
using System.Collections.Generic;
using System.Linq;
using ChatSystem;
using GameLogic;
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
            
            GameManager.instance.characterManager.GetCharacter(ownerClientId.Value, false).onCharacterAwakened += DeclareAllTargetFocusServer;
        }
        

        public void DeclareAllTargetFocusServer()
        {
            if (!CanUse())
            {
                return;
            }
            List<Character> _targetedCharacters = GameManager.instance.characterManager.GetCharacters(false)
                .Where(_c => _c.role.roleID == targetRoleID).ToList();
            if (_targetedCharacters.Count == 0)
            {
                ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID, 
                    $"Total de personne qui ont ciblé le rôle \"{targetRoleID.ToString()}\": 0.", 
                    (int)ChatWindowIDs.Server),
                    CharacterManager.instance.GetSafeRpcTarget(ownerClientId.Value));
                return;
            }
            List<TargetingData> _targetingDataList = new();
            foreach (var _targetedCharacter in _targetedCharacters)
            {
                _targetingDataList.AddRange(RoleTargetSystem.instance.GetAllTargetingDataForTarget(_targetedCharacter.ownerClientId.Value));
            }
            ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                $"Total de personne qui ont ciblé le rôle \"{_targetedCharacters[0].role.roleName}\": {_targetingDataList.Distinct().Count()}",
                (int)ChatWindowIDs.Server),
                CharacterManager.instance.GetSafeRpcTarget(ownerClientId.Value));
        }
    }
}