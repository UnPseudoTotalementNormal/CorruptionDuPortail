#region

using Unity.Netcode;

#endregion

namespace UI.GameSettings
{
    public abstract class GameSettingTab : NetworkBehaviour
    {

        private void Start()
        {
            Init();
        }

        protected abstract void Init();

        public abstract void AskForRefreshSettingsRpc();
    }
}
