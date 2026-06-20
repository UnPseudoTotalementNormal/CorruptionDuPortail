#region

using UnityEngine;

#endregion

namespace UI.GameSettings
{
    // Quick-dev gamesettings-refonte (2026-06-20): demoted from an (empty) NetworkBehaviour to a pure view
    // container — all settings networking now lives on the spawned GameSettingsManager. Kept as a marker
    // component so the existing prefab reference stays valid (no missing-script on LobbyStateUI).
    public class GameSettingsUI : MonoBehaviour
    {
    }
}
