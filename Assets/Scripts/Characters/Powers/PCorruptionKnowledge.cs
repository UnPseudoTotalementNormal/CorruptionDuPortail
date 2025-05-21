#region

using System;
using GameLogic;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptionKnowledge : Power
    {
        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();
        }
        

        public override void OnUsed()
        {
            base.OnUsed();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }
        
        protected override void StopUse()
        {
            base.StopUse();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            foreach (var _character in GameManager.instance.GetCharacters(false))
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                    _character.ownerClientId,
                    nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed), RevealLevel.Personal,
                    GameManager.instance.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
            }
            
        }

        private void OnGameStartedClient()
        {
        }
    }
}
