using Unity.Netcode;
using UnityEngine;

namespace UI.EndGame
{
    /// <summary>
    /// End screen, players who are not the host (T9 maquettes menus v1 C1 + C2): « Quitter la partie » takes them back
    /// to the menu alone, under « L'hôte va terminer la partie. ». Hidden on the host, who has « Terminer la partie »
    /// (<see cref="ShutOffGameButton"/>). Lives on GameEndigStateUI.prefab, with the line it shows.
    /// </summary>
    public class EndLeaveButton : MonoBehaviour
    {
        [Tooltip("The « L'hôte va terminer la partie. » line, shown and hidden with this button.")]
        [SerializeField] private GameObject _hostWaitLine;

        private void Awake()
        {
            bool _isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            gameObject.SetActive(!_isHost);
            if (_hostWaitLine != null) _hostWaitLine.SetActive(!_isHost);
            if (_isHost) return;
            GetComponent<CustomButton>().onButtonClicked += () => LeaveGameButton.Leave("end-screen");
        }
    }
}
