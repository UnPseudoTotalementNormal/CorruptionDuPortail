#region

using UnityEngine;

#endregion

namespace UI.GameSettings
{
    // Quick-dev gamesettings-refonte (2026-06-20): demoted from NetworkBehaviour to a pure MonoBehaviour view
    // base — settings networking now lives on the spawned GameSettingsManager, so a tab carries no NGO
    // identity (the old base declared a hand-rolled refresh RPC). Subclasses just build/refresh widgets in Init().
    public abstract class GameSettingTab : MonoBehaviour
    {
        private void Start()
        {
            Init();
        }

        protected abstract void Init();
    }
}
