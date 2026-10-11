using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using GameLogic;
using Network.Steam;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Network.Services
{
    /// <summary>
    /// Steam build: Steam lobbies (SteamMatchmaking) as the server browser. Lobby data carries what the menus show
    /// (name, language, password flag, game started) plus the host's SteamID64, which the joiner dials over the
    /// Steam Datagram Relay. Steam has no join code nor lobby password: the host publishes a short
    /// <see cref="LobbyCode"/>, and the password is checked by the host's connection approval
    /// (<see cref="LobbyPasswordGate"/>).
    /// </summary>
    public sealed class SteamLobbyBackend : ILobbyBackend
    {
        // Spacewar (480) lobbies are shared by every game testing on it: ours carry this tag and every query filters on it.
        private const string GameKey = "cdp_game";
        private const string GameTag = "CorruptionDuPortail";
        private const string NameKey = "name";
        private const string CodeKey = "code";
        private const string PasswordKey = "pw";
        private const string LockedKey = "locked";
        private const string VersionKey = "version";
        private const int MaxListedLobbies = 50;

        private readonly IRandomProvider random = new UnityRandomProvider();
        // Steam handles of the lobbies we are in, by id: their data stays live (Steam pushes the updates to members).
        private readonly Dictionary<string, Lobby> joined = new();

        public bool IsReady => SteamRuntime.IsReady;

        public bool NeedsHeartbeat => false;

        public async UniTask<GameLobby> CreateAsync(LobbyCreationSettings settings)
        {
            RequireSteam();
            Lobby? _created = await SteamMatchmaking.CreateLobbyAsync(settings.maxPlayers);
            if (!_created.HasValue)
            {
                throw new LobbyBackendException("Steam n'a pas pu créer le lobby.");
            }

            Lobby _lobby = _created.Value;
            if (settings.isPrivate)
            {
                _lobby.SetFriendsOnly();
            }
            else
            {
                _lobby.SetPublic();
            }
            _lobby.SetJoinable(!settings.isLocked);
            _lobby.SetData(GameKey, GameTag);
            _lobby.SetData(NameKey, settings.lobbyName);
            _lobby.SetData(CodeKey, LobbyCode.Generate(random));
            _lobby.SetData(PasswordKey, string.IsNullOrEmpty(settings.password) ? "0" : "1");
            _lobby.SetData(LockedKey, settings.isLocked ? "1" : "0");
            _lobby.SetData(VersionKey, Application.version);
            _lobby.SetData(GameLobby.HostSteamIdKey, SteamClient.SteamId.Value.ToString());
            foreach (KeyValuePair<string, string> _entry in settings.data)
            {
                _lobby.SetData(_entry.Key, _entry.Value ?? string.Empty);
            }

            joined[IdOf(_lobby)] = _lobby;
            return ToGameLobby(_lobby);
        }

        public async UniTask<List<GameLobby>> QueryAsync()
        {
            RequireSteam();
            Lobby[] _found = await SteamMatchmaking.LobbyList
                .FilterDistanceWorldwide()
                .WithKeyValue(GameKey, GameTag)
                .WithMaxResults(MaxListedLobbies)
                .RequestAsync();

            var _lobbies = new List<GameLobby>();
            if (_found == null) // Steam answers null for "no lobby"
            {
                return _lobbies;
            }
            foreach (Lobby _lobby in _found)
            {
                _lobbies.Add(ToGameLobby(_lobby));
            }
            return _lobbies;
        }

        public async UniTask<GameLobby> JoinByIdAsync(string lobbyId, string password)
        {
            RequireSteam();
            if (!ulong.TryParse(lobbyId, out ulong _id))
            {
                throw new LobbyBackendException("Lobby introuvable.", lobbyGone: true);
            }

            var _lobby = new Lobby(_id);
            RoomEnter _result = await _lobby.Join();
            if (_result != RoomEnter.Success)
            {
                throw new LobbyBackendException(DescribeJoinFailure(_result), lobbyGone: _result == RoomEnter.DoesntExist);
            }

            joined[IdOf(_lobby)] = _lobby;
            return ToGameLobby(_lobby);
        }

        public async UniTask<GameLobby> JoinByCodeAsync(string code)
        {
            RequireSteam();
            Lobby[] _found = await SteamMatchmaking.LobbyList
                .FilterDistanceWorldwide()
                .WithKeyValue(GameKey, GameTag)
                .WithKeyValue(CodeKey, LobbyCode.Normalize(code))
                .WithMaxResults(1)
                .RequestAsync();
            if (_found == null || _found.Length == 0)
            {
                // Steam only lists joinable lobbies: a started game is not found either.
                throw new LobbyBackendException("aucune partie ouverte avec ce code.", lobbyGone: true);
            }
            return await JoinByIdAsync(IdOf(_found[0]), null);
        }

        public UniTask<GameLobby> RefreshAsync(GameLobby lobby)
        {
            if (!joined.TryGetValue(lobby.Id, out Lobby _lobby))
            {
                throw new LobbyBackendException("Lobby quitté.", lobbyGone: true);
            }
            return UniTask.FromResult(ToGameLobby(_lobby));
        }

        public UniTask HeartbeatAsync(GameLobby lobby) => UniTask.CompletedTask;

        public UniTask LeaveAsync(GameLobby lobby)
        {
            if (joined.Remove(lobby.Id, out Lobby _lobby))
            {
                _lobby.Leave();
            }
            return UniTask.CompletedTask;
        }

        // A Steam lobby lives while it has members. Hide it from the list first (not joinable, flagged started), so
        // the members still inside until their own leave never get new joiners, then leave it.
        public UniTask DeleteAsync(GameLobby lobby)
        {
            if (joined.Remove(lobby.Id, out Lobby _lobby))
            {
                _lobby.SetData(LockedKey, "1");
                _lobby.SetJoinable(false);
                _lobby.Leave();
            }
            return UniTask.CompletedTask;
        }

        public UniTask SetLockedAsync(GameLobby lobby, bool locked)
        {
            if (joined.TryGetValue(lobby.Id, out Lobby _lobby))
            {
                _lobby.SetData(LockedKey, locked ? "1" : "0");
                _lobby.SetJoinable(!locked);
            }
            return UniTask.CompletedTask;
        }

        public bool IsHost(GameLobby lobby)
        {
            if (lobby == null || !SteamClient.IsValid)
            {
                return false;
            }
            return joined.TryGetValue(lobby.Id, out Lobby _lobby)
                ? _lobby.IsOwnedBy(SteamClient.SteamId)
                : lobby.HostId == SteamClient.SteamId.Value.ToString();
        }

        private static void RequireSteam()
        {
            if (!SteamRuntime.IsReady)
            {
                throw new LobbyBackendException(SteamRuntime.NotRunningMessage);
            }
        }

        private static string IdOf(Lobby lobby) => lobby.Id.Value.ToString();

        private static string DescribeJoinFailure(RoomEnter result)
        {
            switch (result)
            {
                case RoomEnter.DoesntExist:
                    return "la partie n'existe plus.";
                case RoomEnter.Full:
                    return "la partie est pleine.";
                case RoomEnter.NotAllowed:
                case RoomEnter.Limited:
                    return "Steam refuse l'accès à cette partie.";
                case RoomEnter.Banned:
                case RoomEnter.CommunityBan:
                    return "tu as été banni de cette partie.";
                case RoomEnter.MemberBlockedYou:
                case RoomEnter.YouBlockedMember:
                    return "un joueur de cette partie t'a bloqué (ou tu l'as bloqué) sur Steam.";
                case RoomEnter.RatelimitExceeded:
                    return "trop de tentatives, réessaie dans un instant.";
                default:
                    return $"erreur Steam ({result}).";
            }
        }

        private static GameLobby ToGameLobby(Lobby lobby)
        {
            var _data = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> _entry in lobby.Data)
            {
                _data[_entry.Key] = _entry.Value;
            }
            ulong _owner = lobby.Owner.Id.Value; // 0 for a lobby we are not in: Steam only tells members
            return new GameLobby
            {
                Id = IdOf(lobby),
                Name = lobby.GetData(NameKey),
                Code = lobby.GetData(CodeKey),
                HostId = _owner != 0 ? _owner.ToString() : lobby.GetData(GameLobby.HostSteamIdKey),
                HasPassword = lobby.GetData(PasswordKey) == "1",
                IsLocked = lobby.GetData(LockedKey) == "1",
                PlayerCount = lobby.MemberCount,
                MaxPlayers = lobby.MaxMembers,
                Data = _data,
            };
        }
    }
}
