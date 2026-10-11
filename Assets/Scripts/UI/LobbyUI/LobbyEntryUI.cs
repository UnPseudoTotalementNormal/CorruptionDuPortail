using System;
using DG.Tweening;
using Network;
using Network.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.LobbyUI
{
    public class LobbyEntryUI : MonoBehaviour
    {
        [SerializeField] public LayoutElement[] entries;
        [SerializeField] TextMeshProUGUI lobbyNameText;
        [SerializeField] TextMeshProUGUI lobbyPrivateText;
        [SerializeField] TextMeshProUGUI lobbyLanguageText;
        [SerializeField] TextMeshProUGUI lobbyPlayersCountText;
        [SerializeField] Button joinButton;

        [SerializeField] private Color selectedColor;
        private Color baseColor;
        
        [SerializeField] private AYellowpaper.SerializedCollections.SerializedDictionary<string, string> lobbyLanguageCustomDisplayNames = new();
        
        public event Action<GameLobby> onEntryClicked;

        GameLobby lobby;

        private void Awake()
        {
            baseColor = joinButton.targetGraphic.color;
            joinButton.onClick.AddListener(OnJoinClicked);
        }

        public void Setup(GameLobby lobby)
        {
            this.lobby = lobby;
            lobbyNameText.text = lobby.Name;
            lobbyPrivateText.text = lobby.HasPassword ? "Oui" : "Non";
            
            string langue = "Non spécifiée";
            if (lobby.TryGetData(nameof(LobbyCreationSettings.LobbyCustomDataKeys.Language), out string value))
            {
                langue = value;
                if (lobbyLanguageCustomDisplayNames.TryGetValue(langue, out var displayName))
                {
                    langue = displayName;
                }
            }
            lobbyLanguageText.text = langue;
            
            lobbyPlayersCountText.text = $"{lobby.PlayerCount}/{lobby.MaxPlayers}";
        }

        public void OnSelectedFeedback()
        {
            joinButton.targetGraphic.DOColor(selectedColor, 0.5f).SetEase(Ease.OutQuint);
        }
        
        public void OnDeselectedFeedback()
        {
            joinButton.targetGraphic.DOColor(baseColor, 0.5f).SetEase(Ease.OutQuint);
        }

        private void OnJoinClicked()
        {
            onEntryClicked?.Invoke(lobby);
        }
    }
}