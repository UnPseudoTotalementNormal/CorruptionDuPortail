using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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

        // Garde de rentrance pour éviter le double déclenchement du rafraîchissement.
        private bool _isBusy;

        void Start()
        {
            refreshButton.onClick.AddListener(Refresh);
            Refresh();
        }

        public void Refresh()
        {
            if (_isBusy)
            {
                return;
            }
            _isBusy = true;

            RefreshAsync().Forget();
        }

        private async UniTaskVoid RefreshAsync()
        {
            try
            {
                Clear();

                List<Unity.Services.Lobbies.Models.Lobby> _lobbies = await LobbyManager.instance.GetLobbies();
                foreach (Unity.Services.Lobbies.Models.Lobby _lobby in _lobbies)
                    CreateEntry(_lobby);
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (destruction de l'objet) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec du rafraîchissement de la liste des lobbies: {e}");
            }
            finally
            {
                _isBusy = false;
            }
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