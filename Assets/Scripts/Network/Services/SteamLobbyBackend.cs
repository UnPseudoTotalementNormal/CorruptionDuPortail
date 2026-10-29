using System;
using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Diagnostics.Logs;
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
        // Rich presence key Steam turns into "Rejoindre la partie" on our profile (SteamInvites reads it back).
        private const string ConnectKey = "connect";
        private const float LobbyDataTimeoutSeconds = 5f;
        private static readonly string[] DirectKeys =
        {
            GameLobby.HostSteamIdKey, nameof(LobbyCreationSettings.LobbyCustomDataKeys.Language),
        };

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
            PublishJoin(_lobby, !settings.isLocked);
            GameLobby _game = ToGameLobby(_lobby);
            GameLog.Guarded(() => GameLog.Info("net", "steam.lobby.created", "lobby", _game.Id,
                "dataKeys", string.Join(",", _game.Data.Keys)));
            return _game;
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
            PublishJoin(_lobby, _lobby.GetData(LockedKey) != "1");
            GameLobby _game = ToGameLobby(_lobby);
            GameLog.Guarded(() => GameLog.Info("net", "steam.lobby.joined", "lobby", _game.Id, "owner", _game.HostId,
                "dataKeys", string.Join(",", _game.Data.Keys), "enumerated", _lobby.Data.Count()));
            return _game;
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

        public async UniTask<GameLobby> GetAsync(string lobbyId)
        {
            RequireSteam();
            if (joined.TryGetValue(lobbyId, out Lobby _mine))
            {
                return ToGameLobby(_mine);
            }
            if (!ulong.TryParse(lobbyId, out ulong _id))
            {
                throw new LobbyBackendException("la partie n'existe plus.", lobbyGone: true);
            }

            // Steam only knows a foreign lobby's data after asking for it; the answer comes as a data-changed callback.
            var _lobby = new Lobby(_id);
            var _arrived = new UniTaskCompletionSource();
            void OnDataChanged(Lobby changed)
            {
                if (changed.Id.Value == _id)
                {
                    _arrived.TrySetResult();
                }
            }
            SteamMatchmaking.OnLobbyDataChanged += OnDataChanged;
            try
            {
                if (!_lobby.Refresh())
                {
                    throw new LobbyBackendException("la partie n'existe plus.", lobbyGone: true);
                }
                await _arrived.Task.Timeout(TimeSpan.FromSeconds(LobbyDataTimeoutSeconds));
            }
            catch (TimeoutException)
            {
                throw new LobbyBackendException("la partie n'existe plus ou Steam ne répond pas.", lobbyGone: true);
            }
            finally
            {
                SteamMatchmaking.OnLobbyDataChanged -= OnDataChanged;
            }

            // Spacewar is shared: a friend may be in another game's lobby. No tag at all = a lobby that no longer exists.
            string _tag = _lobby.GetData(GameKey);
            if (string.IsNullOrEmpty(_tag))
            {
                throw new LobbyBackendException("la partie n'existe plus.", lobbyGone: true);
            }
            if (_tag != GameTag)
            {
                throw new LobbyBackendException("ce n'est pas une partie de Corruption du Portail.", lobbyGone: true);
            }
            return ToGameLobby(_lobby);
        }

        public UniTask<GameLobby> RefreshAsync(GameLobby lobby)
        {
            if (!joined.TryGetValue(lobby.Id, out Lobby _lobby))
            {
                throw new LobbyBackendException("Lobby quitté.", lobbyGone: true);
            }
            // Members follow the host's lock: no "Rejoindre la partie" on their profile once the game started.
            PublishJoin(_lobby, _lobby.GetData(LockedKey) != "1");
            return UniTask.FromResult(ToGameLobby(_lobby));
        }

        public UniTask HeartbeatAsync(GameLobby lobby) => UniTask.CompletedTask;

        public UniTask LeaveAsync(GameLobby lobby)
        {
            if (joined.Remove(lobby.Id, out Lobby _lobby))
            {
                ClearJoin();
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
                ClearJoin();
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
                PublishJoin(_lobby, !locked);
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

        // Friends see "Rejoindre la partie" on our Steam profile while our lobby takes new players.
        private static void PublishJoin(Lobby lobby, bool joinable)
        {
            SteamFriends.SetRichPresence(ConnectKey, joinable ? SteamJoinRequest.ConnectFor(lobby.Id.Value) : string.Empty);
        }

        private static void ClearJoin()
        {
            SteamFriends.SetRichPresence(ConnectKey, string.Empty);
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
            // The keys the joiner needs are read one by one too: on 2026-10-24 the enumerated data of a joined lobby
            // lacked the host's SteamID, while GetData still returned its name.
            foreach (string _key in DirectKeys)
            {
                string _value = lobby.GetData(_key);
                if (!string.IsNullOrEmpty(_value))
                {
                    _data[_key] = _value;
                }
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
