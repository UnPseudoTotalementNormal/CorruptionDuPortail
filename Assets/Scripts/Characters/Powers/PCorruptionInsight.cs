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
            foreach (var _character in characterManager.GetCharacters())
            {
                gameInfoRevealer.SendRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed),
                    RevealLevel.Personal, ownerClientId.Value);
            }
        }
    }
}