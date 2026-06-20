#region

using System.Linq;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Smartphone.Apps.Lobby
{
    /// <summary>
    /// Quick-dev lobby-menu-on-tablet (2026-06-20). Host-agnostic Start-Game button handler — the modular
    /// replacement for <c>LobbyUI.StartGame</c>, which depended on the StateUI's pushed <c>owningGameState</c>.
    /// It resolves <see cref="LobbyState"/> through the sanctioned prefab-UI route
    /// (<c>CompositionRoot.For(Singleton)</c>), so it works under ANY host (the HUD overlay or a tablet
    /// SmartphoneApp) by reparenting alone — no code change. Server-authoritative: only the host advances the
    /// loop (the validation lives in <see cref="LobbyState.OnStartGameButtonPressed"/>, unchanged).
    ///
    /// Self-wires to its sibling <see cref="Button"/> in code (no Inspector UnityEvent — project-context
    /// forbids UnityEvent for gameplay wiring), so dropping this component on any button GameObject is enough.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LobbyStartButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(StartGame);
        }

        public void StartGame()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            LobbyState _lobbyState = CompositionRoot.For(NetworkManager.Singleton).GameManager
                .GetGameStates(typeof(LobbyState)).FirstOrDefault() as LobbyState;
            if (_lobbyState == null)
            {
                Debug.LogError("LobbyStartButton: no LobbyState resolved from the composition root — cannot start the game.");
                return;
            }

            _lobbyState.OnStartGameButtonPressed();
        }
    }
}
