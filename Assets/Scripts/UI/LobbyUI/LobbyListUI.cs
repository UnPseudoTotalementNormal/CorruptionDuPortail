using System.Collections.Generic;
using Network.Services;
using UnityEngine;
using UnityEngine.UI;

namespace UI.LobbyUI
{
    public class LobbyListUI : MonoBehaviour
    {
        [SerializeField] RectTransform contentRoot;
        [SerializeField] LobbyEntryUI lobbyEntryPrefab;
        [SerializeField] Button refreshButton;

        void Start()
        {
            refreshButton.onClick.AddListener(Refresh);
            Refresh();
        }

        public async void Refresh()
        {
            Clear();

            List<Unity.Services.Lobbies.Models.Lobby> _lobbies = await LobbyManager.instance.GetLobbies();
            foreach (Unity.Services.Lobbies.Models.Lobby _lobby in _lobbies)
                CreateEntry(_lobby);
        }

        void CreateEntry(Unity.Services.Lobbies.Models.Lobby _lobby)
        {
            LobbyEntryUI _entry = Instantiate(lobbyEntryPrefab, contentRoot);
            _entry.Setup(_lobby);
        }

        void Clear()
        {
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Destroy(contentRoot.GetChild(i).gameObject);
        }
    }
}