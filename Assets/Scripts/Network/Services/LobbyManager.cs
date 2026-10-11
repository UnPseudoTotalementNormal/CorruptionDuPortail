using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Network.Services
{
    /// <summary>
    /// The lobby the local player is in (server browser, create, join, leave), over the backend of this build:
    /// UGS lobbies in the Relay build, Steam lobbies in the Steam build (<see cref="GameFlavor"/>). Keeps the session
    /// bookkeeping both share: current lobby, host heartbeat, polling, and the player-facing error channel.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager instance { get; private set; }

        // Events
        public event Action<GameLobby> OnLobbyCreated;
        public event Action<GameLobby> OnLobbyJoined;
        public event Action<GameLobby> OnLobbyUpdated;
        public event System.Action OnLobbyLeft;
        public event Action<string> OnLobbyError;

        private ILobbyBackend backend;
        private GameLobby currentLobby;
        private CancellationTokenSource heartbeatCancellation;
        private CancellationTokenSource pollCancellation;
        private const float HEARTBEAT_INTERVAL = 15f;
        private const float LOBBY_POLL_INTERVAL = 5.1f;

        public GameLobby CurrentLobby => currentLobby;
        public bool IsInLobby => currentLobby != null;

        /// <summary>The lobby list can be queried: signed in to UGS / Steam client running.</summary>
        public bool IsReady => Backend.IsReady;

        // True when the local player owns the current lobby (the only member allowed to delete it). Used to decide,
        // on leave, whether to delete the whole lobby (host) or just remove ourselves (non-host), and to gate the
        // heartbeat ping (only the host may send it).
        public bool IsLobbyHost => currentLobby != null && Backend.IsHost(currentLobby);

        private ILobbyBackend Backend => backend ??= GameFlavor.IsSteam ? new SteamLobbyBackend() : new UgsLobbyBackend();

        /// <summary>
        /// Surface a message on the shared lobby-error channel (investigation join-started-game-gate). The event
        /// itself cannot be invoked from outside the declaring type, and the join UIs — which produce the most
        /// user-visible failures — had no way to reach the notification that
        /// <c>ClientDisconnectHandler.OnLobbyError</c> already renders. Their messages were logged and never shown.
        /// </summary>
        public void ReportError(string _message)
        {
            if (string.IsNullOrEmpty(_message))
            {
                return;
            }

            OnLobbyError?.Invoke(_message);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                StopHeartbeat();
                StopLobbyPolling();
                instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            if (currentLobby != null)
            {
                LeaveOrDeleteLobby().Forget(); // host deletes the lobby, a client just leaves.
            }
        }

        public async UniTask<GameLobby> CreateLobby(LobbyCreationSettings _settings)
        {
            if (string.IsNullOrWhiteSpace(_settings.lobbyName))
            {
                OnLobbyError?.Invoke("Le nom du lobby ne peut pas être vide");
                return null;
            }

            if (_settings.maxPlayers < 2 || _settings.maxPlayers > 20)
            {
                OnLobbyError?.Invoke("Le nombre de joueurs doit être entre 2 et 20");
                return null;
            }

            if (!string.IsNullOrEmpty(_settings.password) && _settings.password.Length < 8)
            {
                OnLobbyError?.Invoke("Le mot de passe doit contenir au moins 8 caractères");
                return null;
            }

            // UGS caps it at 64; the Steam build sends it inside the connection request, whose strings are bounded.
            if (!string.IsNullOrEmpty(_settings.password) && _settings.password.Length > MaxPasswordLength)
            {
                OnLobbyError?.Invoke($"Le mot de passe doit contenir au plus {MaxPasswordLength} caractères");
                return null;
            }

            try
            {
                currentLobby = await Backend.CreateAsync(_settings);

                StartSessionLoops();
                OnLobbyCreated?.Invoke(currentLobby);

                Debug.Log($"Lobby créé: {currentLobby.Name} (ID: {currentLobby.Id}, code: {currentLobby.Code})");
                return currentLobby;
            }
            catch (LobbyBackendException e)
            {
                Debug.LogError($"Échec de création du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de créer le lobby: {e.Message}");
                return null;
            }
        }

        public async UniTask<List<GameLobby>> GetLobbies()
        {
            try
            {
                return await Backend.QueryAsync();
            }
            catch (LobbyBackendException e)
            {
                OnLobbyError?.Invoke($"Impossible de récupérer les lobbies: {e.Message}");
                throw new Exception($"Échec de récupération des lobbies: {e.Message}");
            }
        }

        public async UniTask<GameLobby> JoinLobby(string _lobbyId, string _password = null)
        {
            if (string.IsNullOrWhiteSpace(_lobbyId))
            {
                OnLobbyError?.Invoke("L'ID du lobby ne peut pas être vide");
                return null;
            }

            try
            {
                currentLobby = await Backend.JoinByIdAsync(_lobbyId, _password);
                StartSessionLoops();
                OnLobbyJoined?.Invoke(currentLobby);

                Debug.Log($"Lobby rejoint: {currentLobby.Name} (ID: {currentLobby.Id})");
                return currentLobby;
            }
            catch (LobbyBackendException e)
            {
                Debug.LogError($"Échec de rejoindre le lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de rejoindre le lobby: {e.Message}");
                return null;
            }
        }

        public async UniTask<GameLobby> JoinLobbyByCode(string _lobbyCode)
        {
            if (string.IsNullOrWhiteSpace(_lobbyCode))
            {
                OnLobbyError?.Invoke("Le code du lobby ne peut pas être vide");
                return null;
            }

            try
            {
                currentLobby = await Backend.JoinByCodeAsync(_lobbyCode.Trim());
                StartSessionLoops();
                OnLobbyJoined?.Invoke(currentLobby);

                Debug.Log($"Lobby rejoint par code: {currentLobby.Name} (Code: {_lobbyCode})");
                return currentLobby;
            }
            catch (LobbyBackendException e)
            {
                Debug.LogError($"Échec de rejoindre le lobby par code: {e.Message}");
                OnLobbyError?.Invoke($"Code invalide ou lobby introuvable: {e.Message}");
                return null;
            }
        }

        public async UniTask LeaveLobby()
        {
            if (currentLobby == null)
            {
                Debug.LogWarning("Aucun lobby à quitter");
                return;
            }

            try
            {
                await Backend.LeaveAsync(currentLobby);
                ForgetCurrentLobby();
                Debug.Log("Lobby quitté avec succès");
            }
            catch (LobbyBackendException e)
            {
                // Leaving is final whatever the service says: never keep polling / heartbeating a lobby we left.
                ForgetCurrentLobby();
                // Already gone (the host deleted it on a graceful end, or it expired): that IS the outcome wanted.
                if (e.LobbyGone)
                {
                    Debug.Log($"Lobby déjà fermé en le quittant: {e.Message}");
                    return;
                }
                Debug.LogError($"Échec de quitter le lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de quitter le lobby: {e.Message}");
            }
        }

        public async UniTask DeleteLobby()
        {
            if (currentLobby == null)
            {
                Debug.LogWarning("Aucun lobby à supprimer");
                return;
            }

            try
            {
                await Backend.DeleteAsync(currentLobby);
                ForgetCurrentLobby();
                Debug.Log("Lobby supprimé avec succès");
            }
            catch (LobbyBackendException e)
            {
                ForgetCurrentLobby();
                if (e.LobbyGone)
                {
                    Debug.Log($"Lobby déjà supprimé: {e.Message}");
                    return;
                }
                Debug.LogError($"Échec de suppression du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de supprimer le lobby: {e.Message}");
            }
        }

        private void ForgetCurrentLobby()
        {
            StopHeartbeat();
            StopLobbyPolling();
            if (currentLobby == null)
            {
                return;
            }
            currentLobby = null;
            OnLobbyLeft?.Invoke();
        }

        // Leave the lobby cleanly on the way out. If we are the host, DELETE the whole lobby now instead of only
        // removing ourselves — otherwise the lobby lingers (with the remaining members) until UGS's no-heartbeat
        // auto-expiry (~30s), or stays listed on Steam under a new owner, and its members poll a hostless lobby.
        // A non-host just removes itself. Safe to call when not in a lobby.
        public async UniTask LeaveOrDeleteLobby()
        {
            if (currentLobby == null)
            {
                return;
            }

            if (IsLobbyHost)
            {
                await DeleteLobby();
            }
            else
            {
                await LeaveLobby();
            }
        }

        private void StartSessionLoops()
        {
            if (Backend.NeedsHeartbeat)
            {
                StartHeartbeat().Forget();
            }
            StartLobbyPolling().Forget();
        }

        private async UniTaskVoid StartHeartbeat()
        {
            StopHeartbeat();

            heartbeatCancellation = new CancellationTokenSource();
            CancellationToken _token = heartbeatCancellation.Token;
            int _consecutiveFailures = 0;

            try
            {
                while (!_token.IsCancellationRequested && currentLobby != null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(HEARTBEAT_INTERVAL), cancellationToken: _token);

                    // Seul l'host du lobby peut heartbeat ; un client non-host déclenche
                    // "only lobby host can send heartbeat" côté service. Revérifié à chaque
                    // itération car l'host peut changer (migration).
                    if (currentLobby == null || !IsLobbyHost)
                    {
                        continue;
                    }

                    // Same rule as the polling: one failed ping used to stop the heartbeat for good, so the
                    // lobby expired ~30 s later although the host was fine. Retry; report a lasting failure only.
                    try
                    {
                        await Backend.HeartbeatAsync(currentLobby);
                        _consecutiveFailures = 0;
                        Debug.Log($"Heartbeat envoyé pour le lobby: {currentLobby?.Name}");
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        _consecutiveFailures++;
                        Debug.LogWarning($"Heartbeat du lobby en échec ({_consecutiveFailures}/{MaxConsecutivePollFailures}): {e.Message}");
                        if (_consecutiveFailures >= MaxConsecutivePollFailures)
                        {
                            Debug.LogError($"Échec du heartbeat: {e}");
                            if (!IsNetworkSessionRunning())
                            {
                                OnLobbyError?.Invoke($"Connexion au lobby perdue: {e.Message}");
                            }
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal lors de l'arrêt (annulation du heartbeat) : sortie silencieuse
            }
        }

        private void StopHeartbeat()
        {
            heartbeatCancellation?.Cancel();
            heartbeatCancellation?.Dispose();
            heartbeatCancellation = null;
        }

        private async UniTaskVoid StartLobbyPolling()
        {
            StopLobbyPolling();

            pollCancellation = new CancellationTokenSource();
            CancellationToken _token = pollCancellation.Token;
            int _consecutiveFailures = 0;

            try
            {
                while (!_token.IsCancellationRequested && currentLobby != null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(LOBBY_POLL_INTERVAL), cancellationToken: _token);

                    if (currentLobby == null)
                    {
                        continue;
                    }

                    // One failed poll must not end the loop nor reach the player: the UGS SDK throws on transient
                    // failures (a NullReferenceException inside WrappedLobbyService.TryCatchRequest when an error
                    // response has no readable body, rate limits, network blips). Before, the first one stopped the
                    // polling for good and popped "Connexion au lobby perdue: Object reference not set…" mid-game
                    // (found by autoplay, 2026-10-07). Retry, and report only a lasting failure.
                    string _lobbyId = currentLobby.Id;
                    try
                    {
                        GameLobby _updatedLobby = await Backend.RefreshAsync(currentLobby);
                        _consecutiveFailures = 0;
                        if (currentLobby != null && currentLobby.Id == _lobbyId)
                        {
                            currentLobby = _updatedLobby;
                            OnLobbyUpdated?.Invoke(_updatedLobby);
                        }
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        _consecutiveFailures++;
                        Debug.LogWarning($"Polling du lobby en échec ({_consecutiveFailures}/{MaxConsecutivePollFailures}): {e.Message}");
                        if (_consecutiveFailures >= MaxConsecutivePollFailures)
                        {
                            Debug.LogError($"Échec du polling du lobby: {e}");
                            // A live network session is the real link to the host (its loss is reported by
                            // ClientDisconnectHandler); the cloud lobby is only bookkeeping by then.
                            if (!IsNetworkSessionRunning())
                            {
                                OnLobbyError?.Invoke($"Connexion au lobby perdue: {e.Message}");
                            }
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal lors de l'arrêt (annulation du polling) : sortie silencieuse
            }
        }

        private const int MaxConsecutivePollFailures = 3;
        private const int MaxPasswordLength = 64;

        private static bool IsNetworkSessionRunning()
        {
            var _nm = Unity.Netcode.NetworkManager.Singleton;
            return _nm != null && (_nm.IsServer || _nm.IsConnectedClient);
        }

        private void StopLobbyPolling()
        {
            pollCancellation?.Cancel();
            pollCancellation?.Dispose();
            pollCancellation = null;
        }

        public async UniTask SetLobbyLocked(bool isLocked)
        {
            if (currentLobby == null)
            {
                OnLobbyError?.Invoke("Aucun lobby actif");
                return;
            }

            try
            {
                await Backend.SetLockedAsync(currentLobby, isLocked);
                Debug.Log($"Lobby {(isLocked ? "verrouillé" : "déverrouillé")}");
            }
            catch (LobbyBackendException e)
            {
                Debug.LogError($"Échec de verrouillage du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de verrouiller le lobby: {e.Message}");
            }
        }

    }
}
