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
            foreach (var _character in GameManager.instance.characterManager.GetCharacters())
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed),
                    RevealLevel.Personal, true, GameManager.instance.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
            }
        }
    }
}