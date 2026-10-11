using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Network.Services
{
    /// <summary>
    /// One lobby service: UGS (<see cref="UgsLobbyBackend"/>, Relay build) or Steam (<see cref="SteamLobbyBackend"/>,
    /// Steam build). <see cref="LobbyManager"/> picks it once and keeps the session bookkeeping (current lobby,
    /// heartbeat, polling, error reporting). A failed call throws <see cref="LobbyBackendException"/>.
    /// </summary>
    public interface ILobbyBackend
    {
        /// <summary>Signed in (UGS) / Steam client running: the lobby list can be queried.</summary>
        bool IsReady { get; }

        /// <summary>The host must ping the service to keep its lobby alive (UGS). Steam keeps it while members remain.</summary>
        bool NeedsHeartbeat { get; }

        UniTask<GameLobby> CreateAsync(LobbyCreationSettings settings);
        UniTask<List<GameLobby>> QueryAsync();
        UniTask<GameLobby> JoinByIdAsync(string lobbyId, string password);
        UniTask<GameLobby> JoinByCodeAsync(string code);
        /// <summary>Fresh snapshot of a lobby we are in.</summary>
        UniTask<GameLobby> RefreshAsync(GameLobby lobby);
        UniTask HeartbeatAsync(GameLobby lobby);
        UniTask LeaveAsync(GameLobby lobby);
        /// <summary>Host only: close the lobby for everyone.</summary>
        UniTask DeleteAsync(GameLobby lobby);
        UniTask SetLockedAsync(GameLobby lobby, bool locked);
        bool IsHost(GameLobby lobby);
    }

    public sealed class LobbyBackendException : Exception
    {
        /// <summary>The lobby no longer exists (deleted, expired). Leaving it is then already done.</summary>
        public bool LobbyGone { get; }

        public LobbyBackendException(string message, bool lobbyGone = false, Exception inner = null)
            : base(message, inner)
        {
            LobbyGone = lobbyGone;
        }
    }
}
