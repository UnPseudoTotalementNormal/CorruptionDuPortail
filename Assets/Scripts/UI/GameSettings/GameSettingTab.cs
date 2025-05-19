using Unity.Netcode;
using UnityEngine;

namespace UI.GameSettings
{
    public abstract class GameSettingTab : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Init();
        }

        protected abstract void Init();

        public abstract void ApplySettingsServer();
    }
}
