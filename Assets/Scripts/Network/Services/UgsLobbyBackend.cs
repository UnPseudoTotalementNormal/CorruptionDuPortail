using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Network.Services
{
    /// <summary>Relay build: Unity Gaming Services lobbies (anonymous UGS sign-in from LoginMenu).</summary>
    public sealed class UgsLobbyBackend : ILobbyBackend
    {
        public bool IsReady =>
            UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

        public bool NeedsHeartbeat => true;

        public async UniTask<GameLobby> CreateAsync(LobbyCreationSettings settings)
        {
            var _data = new Dictionary<string, DataObject>();
            foreach (KeyValuePair<string, string> _entry in settings.data)
            {
                _data[_entry.Key] = new DataObject(DataObject.VisibilityOptions.Public, _entry.Value);
            }
            var _options = new CreateLobbyOptions
            {
                IsPrivate = settings.isPrivate,
                IsLocked = settings.isLocked,
                Password = string.IsNullOrEmpty(settings.password) ? null : settings.password,
                Data = _data,
            };
            return await Call(() => LobbyService.Instance.CreateLobbyAsync(settings.lobbyName, settings.maxPlayers, _options));
        }

        public async UniTask<List<GameLobby>> QueryAsync()
        {
            QueryResponse _response = await CallRaw(() => LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions()));
            var _lobbies = new List<GameLobby>(_response.Results.Count);
            foreach (Lobby _lobby in _response.Results)
            {
                _lobbies.Add(ToGameLobby(_lobby));
            }
            return _lobbies;
        }

        public async UniTask<GameLobby> JoinByIdAsync(string lobbyId, string password)
        {
            JoinLobbyByIdOptions _options = string.IsNullOrEmpty(password) ? null : new JoinLobbyByIdOptions { Password = password };
            return await Call(() => LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, _options));
        }

        public async UniTask<GameLobby> JoinByCodeAsync(string code)
        {
            return await Call(() => LobbyService.Instance.JoinLobbyByCodeAsync(code));
        }

        public async UniTask<GameLobby> RefreshAsync(GameLobby lobby)
        {
            return await Call(() => LobbyService.Instance.GetLobbyAsync(lobby.Id));
        }

        public async UniTask HeartbeatAsync(GameLobby lobby)
        {
            await CallRaw(async () =>
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobby.Id);
                return true;
            });
        }

        public async UniTask LeaveAsync(GameLobby lobby)
        {
            string _playerId = AuthenticationService.Instance.PlayerId;
            await CallRaw(async () =>
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, _playerId);
                return true;
            });
        }

        public async UniTask DeleteAsync(GameLobby lobby)
        {
            await CallRaw(async () =>
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                return true;
            });
        }

        public async UniTask SetLockedAsync(GameLobby lobby, bool locked)
        {
            await CallRaw(() => LobbyService.Instance.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { IsLocked = locked }));
        }

        // Unity's Lobby HostId is the authoritative owner and the only member allowed to delete the lobby or ping it.
        public bool IsHost(GameLobby lobby) =>
            lobby != null && !string.IsNullOrEmpty(lobby.HostId) && lobby.HostId == AuthenticationService.Instance.PlayerId;

        private static async UniTask<GameLobby> Call(System.Func<System.Threading.Tasks.Task<Lobby>> request)
        {
            return ToGameLobby(await CallRaw(request));
        }

        private static async UniTask<T> CallRaw<T>(System.Func<System.Threading.Tasks.Task<T>> request)
        {
            try
            {
                return await request();
            }
            catch (LobbyServiceException e)
            {
                throw new LobbyBackendException(e.Message, e.Reason == LobbyExceptionReason.LobbyNotFound, e);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // The SDK also throws plain exceptions on transient failures (a NullReferenceException inside
                // WrappedLobbyService.TryCatchRequest when an error response has no readable body): same channel, so a
                // failed leave still forgets the lobby and the menus get a message instead of an unhandled throw.
                throw new LobbyBackendException(e.Message, false, e);
            }
        }

        private static GameLobby ToGameLobby(Lobby lobby)
        {
            var _data = new Dictionary<string, string>();
            if (lobby.Data != null)
            {
                foreach (KeyValuePair<string, DataObject> _entry in lobby.Data)
                {
                    _data[_entry.Key] = _entry.Value?.Value;
                }
            }
            return new GameLobby
            {
                Id = lobby.Id,
                Name = lobby.Name,
                Code = lobby.LobbyCode,
                HostId = lobby.HostId,
                HasPassword = lobby.HasPassword,
                IsLocked = lobby.IsLocked,
                PlayerCount = lobby.Players?.Count ?? 0,
                MaxPlayers = lobby.MaxPlayers,
                Data = _data,
            };
        }
    }
}
