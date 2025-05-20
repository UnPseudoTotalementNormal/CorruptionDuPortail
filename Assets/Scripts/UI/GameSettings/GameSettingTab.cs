using System;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

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
