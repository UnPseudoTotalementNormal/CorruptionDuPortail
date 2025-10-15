using GameLogic;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    public class PCorruptionParanoia : Power
    {
        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                ownerClientId.Value, 
                nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, true, 
                NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
        }
    }
}