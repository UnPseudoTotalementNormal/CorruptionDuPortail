using System;
using GameLogic;
using GameLogic.GameStates;
using Network;

namespace Characters.Powers
{
    [Serializable]
    public class PClandestineObservation : Power
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
            
        }
    }
}