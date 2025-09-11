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
            List<ulong> _anomalyIds = GameManager.instance.GetCharacters(false)
                .Where(_c => _c.role.factionType == FactionType.anomaly)
                .Select(_c => _c.ownerClientId)
                .ToList();
            foreach (var _anomalyId in _anomalyIds)
            {
                var _rpcTarget = NetworkManager.Singleton.RpcTarget.Single(_anomalyId, RpcTargetUse.Persistent);
                ChatManager.instance.DiscoverChatRpc((int)ChatWindowIDs.AnomalyOnly, _rpcTarget);
            }
        }
    }
}