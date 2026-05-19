using System;
using System.Threading.Tasks;
using DG.Tweening;
using Network;
using Network.Services;
using TMPro;
using Unity.Netcode;
using Netcode.Transports.Facepunch;
using Steamworks;
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
        
        public event Action<Unity.Services.Lobbies.Models.Lobby> onEntryClicked;

        Unity.Services.Lobbies.Models.Lobby lobby;

        private void Awake()
        {
            baseColor = joinButton.targetGraphic.color;
            joinButton.onClick.AddListener(OnJoinClicked);
        }

        public void Setup(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            this.lobby = lobby;
            lobbyNameText.text = lobby.Name;
            lobbyPrivateText.text = lobby.HasPassword ? "Oui" : "Non";
            
            string langue = "Non spécifiée";
            if (lobby.Data != null && 
                lobby.Data.TryGetValue(nameof(LobbyCreationSettings.LobbyCustomDataKeys.Language), 
                    out var value))
            {
                langue = value.Value;
                if (lobbyLanguageCustomDisplayNames.TryGetValue(langue, out var displayName))
                {
                    langue = displayName;
                }
            }
            lobbyLanguageText.text = langue;
            
            lobbyPlayersCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
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