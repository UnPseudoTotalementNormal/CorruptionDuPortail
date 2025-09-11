#region

using System;
using GameLogic;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PAutoCorruption : Power
    {
        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            GameManager.instance.CorruptPlayerRpc(ownerClientId);
        }
    }
}