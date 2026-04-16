using System;
using System.Collections.Generic;
using System.Linq;
using ChatSystem;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PEyeOfTheVoid : Power
    {
        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            List<ulong> _anomalyIds = GameManager.instance.characterManager.GetCharacters(false)
                .Where(_c => _c.role.factionType == FactionType.anomaly)
                .Select(_c => _c.ownerClientId.Value)
                .ToList();
            foreach (var _anomalyId in _anomalyIds)
            {
                var _rpcTarget = CharacterManager.instance.GetSafeRpcTarget(_anomalyId);
                ChatManager.instance.DiscoverChatRpc((int)ChatWindowIDs.AnomalyOnly, _rpcParams: _rpcTarget);
            }
        }
    }
}