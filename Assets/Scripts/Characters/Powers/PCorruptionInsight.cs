#region

using System;
using GameLogic;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptionInsight : Power
    {
        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            foreach (var _character in GameManager.instance.GetCharacters())
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed),
                    RevealLevel.Personal, GameManager.instance.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
            }
        }
    }
}