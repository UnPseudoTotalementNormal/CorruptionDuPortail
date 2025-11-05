using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Network.Services
{
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager instance { get; private set; }
        
        Lobby currentLobby;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            instance = this;
        }

        public async Task<Lobby> CreateLobby(string _lobbyName, int _maxPlayers)
        {
            currentLobby = await LobbyService.Instance.CreateLobbyAsync(
                _lobbyName,
                _maxPlayers,
                new CreateLobbyOptions
                {
                    IsPrivate = false
                });

            return currentLobby;
        }

        public async Task<List<Lobby>> GetLobbies()
        {
            var _q = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions());
            return _q.Results;
        }

        public async Task<Lobby> JoinLobby(string _lobbyId)
        {
            return await LobbyService.Instance.JoinLobbyByIdAsync(_lobbyId);
        }

        public async Task<Lobby> JoinLobbyByCode(string _lobbyCode)
        {
            try
            {
                currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(_lobbyCode);
                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Failed to join lobby by code: {e.Message}");
                return null;
            }
        }

        public async Task UpdateLobbyJoinCode(string _lobbyId, string _joinCode)
        {
            await LobbyService.Instance.UpdateLobbyAsync(
                _lobbyId,
                new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { "joinCode", new DataObject(DataObject.VisibilityOptions.Public, _joinCode) }
                    }
                });
        }
        
        public async Task<Lobby> RefreshLobby(string _lobbyId)
        {
            return await LobbyService.Instance.GetLobbyAsync(_lobbyId);
        }

    }
}