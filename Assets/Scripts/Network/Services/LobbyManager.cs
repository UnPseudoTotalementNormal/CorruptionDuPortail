using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Network.Services
{
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager instance { get; private set; }
        
        // Events
        public event Action<Lobby> OnLobbyCreated;
        public event Action<Lobby> OnLobbyJoined;
        public event Action<Lobby> OnLobbyUpdated;
        public event System.Action OnLobbyLeft;
        public event Action<string> OnLobbyError;
        
        private Lobby currentLobby;
        private CancellationTokenSource heartbeatCancellation;
        private CancellationTokenSource pollCancellation;
        private const float HEARTBEAT_INTERVAL = 15f; 
        private const float LOBBY_POLL_INTERVAL = 5.1f; 
        
        public Lobby CurrentLobby => currentLobby;
        public bool IsInLobby => currentLobby != null;

        /// <summary>
        /// True only when the local player owns the current lobby (its HostId).
        /// Only the lobby host may send heartbeat pings; the Unity Lobby service
        /// rejects "only lobby host can send heartbeat" otherwise.
        /// </summary>
        public bool IsLobbyHost =>
            currentLobby != null
            && AuthenticationService.Instance.IsSignedIn
            && currentLobby.HostId == AuthenticationService.Instance.PlayerId;

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
                _ = LeaveLobby(); // Fire and forget
            }
        }

        public async Task<Lobby> CreateLobby(LobbyCreationSettings _settings)
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
            
            if (_settings.password != null && _settings.password.Length < 8)
            {
                OnLobbyError?.Invoke("Le mot de passe doit contenir au moins 8 caractères");
                return null;
            }

            try
            {
                currentLobby = await LobbyService.Instance.CreateLobbyAsync(
                    _settings.lobbyName,
                    _settings.maxPlayers,
                    _settings.Options);

                StartHeartbeat().Forget();
                StartLobbyPolling().Forget();
                OnLobbyCreated?.Invoke(currentLobby);
                
                Debug.Log($"Lobby créé: {currentLobby.Name} (ID: {currentLobby.Id})");
                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de création du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de créer le lobby: {e.Message}");
                return null;
            }
        }

        public async Task<List<Lobby>> GetLobbies()
        {
            try
            {
                var _q = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions());
                return _q.Results;
            }
            catch (LobbyServiceException e)
            {
                OnLobbyError?.Invoke($"Impossible de récupérer les lobbies: {e.Message}");
                throw new Exception($"Échec de récupération des lobbies: {e.Message}");
            }
        }

        public async Task<Lobby> JoinLobby(string _lobbyId, string _password = null)
        {
            if (string.IsNullOrWhiteSpace(_lobbyId))
            {
                OnLobbyError?.Invoke("L'ID du lobby ne peut pas être vide");
                return null;
            }

            try
            {
                JoinLobbyByIdOptions options = null;
                if (!string.IsNullOrEmpty(_password))
                {
                    options = new JoinLobbyByIdOptions
                    {
                        Password = _password
                    };
                }
                
                currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(_lobbyId, options);
                StartHeartbeat().Forget();
                StartLobbyPolling().Forget();
                OnLobbyJoined?.Invoke(currentLobby);
                
                Debug.Log($"Lobby rejoint: {currentLobby.Name} (ID: {currentLobby.Id})");
                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de rejoindre le lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de rejoindre le lobby: {e.Message}");
                return null;
            }
        }

        public async Task<Lobby> JoinLobbyByCode(string _lobbyCode)
        {
            if (string.IsNullOrWhiteSpace(_lobbyCode))
            {
                OnLobbyError?.Invoke("Le code du lobby ne peut pas être vide");
                return null;
            }

            try
            {
                currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(_lobbyCode);
                StartHeartbeat().Forget();
                StartLobbyPolling().Forget();
                OnLobbyJoined?.Invoke(currentLobby);
                
                Debug.Log($"Lobby rejoint par code: {currentLobby.Name} (Code: {_lobbyCode})");
                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de rejoindre le lobby par code: {e.Message}");
                OnLobbyError?.Invoke($"Code invalide ou lobby introuvable: {e.Message}");
                return null;
            }
        }

        public async Task UpdateLobbyData(string _key, string _value, DataObject.VisibilityOptions _visibility = DataObject.VisibilityOptions.Public)
        {
            if (currentLobby == null)
            {
                OnLobbyError?.Invoke("Aucun lobby actif");
                return;
            }

            try
            {
                var updatedData = new Dictionary<string, DataObject>();
                
                if (currentLobby.Data != null)
                {
                    foreach (var kvp in currentLobby.Data)
                    {
                        updatedData[kvp.Key] = kvp.Value;
                    }
                }
                
                updatedData[_key] = new DataObject(_visibility, _value);

                await LobbyService.Instance.UpdateLobbyAsync(
                    currentLobby.Id,
                    new UpdateLobbyOptions
                    {
                        Data = updatedData
                    });
                    
                Debug.Log($"Donnée du lobby mise à jour: {_key} = {_value}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de mise à jour de {_key}: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de mettre à jour {_key}: {e.Message}");
                throw;
            }
        }

        public async Task UpdateLobbyJoinCode(string _lobbyId, string _joinCode)
        {
            if (string.IsNullOrWhiteSpace(_lobbyId))
            {
                OnLobbyError?.Invoke("L'ID du lobby ne peut pas être vide");
                return;
            }

            try
            {
                var lobby = await LobbyService.Instance.GetLobbyAsync(_lobbyId);
                var updatedData = new Dictionary<string, DataObject>();
                
                if (lobby.Data != null)
                {
                    foreach (var kvp in lobby.Data)
                    {
                        updatedData[kvp.Key] = kvp.Value;
                    }
                }
                
                updatedData["joinCode"] = new DataObject(DataObject.VisibilityOptions.Public, _joinCode);

                await LobbyService.Instance.UpdateLobbyAsync(
                    _lobbyId,
                    new UpdateLobbyOptions
                    {
                        Data = updatedData
                    });
                    
                Debug.Log($"Code de lobby mis à jour: {_joinCode}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de mise à jour du code: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de mettre à jour le code: {e.Message}");
            }
        }
        
        public async Task<Lobby> RefreshLobby(string _lobbyId)
        {
            if (string.IsNullOrWhiteSpace(_lobbyId))
            {
                OnLobbyError?.Invoke("L'ID du lobby ne peut pas être vide");
                return null;
            }

            try
            {
                var _lobby = await LobbyService.Instance.GetLobbyAsync(_lobbyId);
                if (currentLobby != null && currentLobby.Id == _lobbyId)
                {
                    currentLobby = _lobby;
                }
                return _lobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de rafraîchissement du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de rafraîchir le lobby: {e.Message}");
                return null;
            }
        }
        
        public void UpdateIsPrivateLobby(bool isPrivate)
        {
            UpdateLobbyOptions updateOptions = new()
            {
                IsPrivate = isPrivate
            };
            LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, updateOptions);
        }

        public async Task LeaveLobby()
        {
            if (currentLobby == null)
            {
                Debug.LogWarning("Aucun lobby à quitter");
                return;
            }

            try
            {
                string _playerId = Unity.Services.Authentication.AuthenticationService.Instance.PlayerId;
                await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, _playerId);
                
                StopHeartbeat();
                StopLobbyPolling();
                currentLobby = null;
                OnLobbyLeft?.Invoke();
                
                Debug.Log("Lobby quitté avec succès");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de quitter le lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de quitter le lobby: {e.Message}");
            }
        }

        public async Task DeleteLobby()
        {
            if (currentLobby == null)
            {
                Debug.LogWarning("Aucun lobby à supprimer");
                return;
            }

            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                
                StopHeartbeat();
                StopLobbyPolling();
                currentLobby = null;
                OnLobbyLeft?.Invoke();
                
                Debug.Log("Lobby supprimé avec succès");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de suppression du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de supprimer le lobby: {e.Message}");
            }
        }

        private async UniTaskVoid StartHeartbeat()
        {
            StopHeartbeat();

            heartbeatCancellation = new CancellationTokenSource();
            CancellationToken _token = heartbeatCancellation.Token;

            try
            {
                while (!_token.IsCancellationRequested && currentLobby != null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(HEARTBEAT_INTERVAL), cancellationToken: _token);

                    // Seul l'host du lobby peut heartbeat ; un client non-host déclenche
                    // "only lobby host can send heartbeat" côté service. Revérifié à chaque
                    // itération car l'host peut changer (migration).
                    if (currentLobby != null && IsLobbyHost)
                    {
                        await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
                        Debug.Log($"Heartbeat envoyé pour le lobby: {currentLobby.Name}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal lors de l'arrêt (annulation du heartbeat) : sortie silencieuse
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec du heartbeat: {e.Message}");
                OnLobbyError?.Invoke($"Connexion au lobby perdue: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Erreur inattendue dans le heartbeat: {e}");
                OnLobbyError?.Invoke($"Connexion au lobby perdue: {e.Message}");
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

            try
            {
                while (!_token.IsCancellationRequested && currentLobby != null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(LOBBY_POLL_INTERVAL), cancellationToken: _token);

                    if (currentLobby != null)
                    {
                        var updatedLobby = await RefreshLobby(currentLobby.Id);
                        if (updatedLobby != null)
                        {
                            OnLobbyUpdated?.Invoke(updatedLobby);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal lors de l'arrêt (annulation du polling) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec du polling du lobby: {e}");
                OnLobbyError?.Invoke($"Connexion au lobby perdue: {e.Message}");
            }
        }

        private void StopLobbyPolling()
        {
            pollCancellation?.Cancel();
            pollCancellation?.Dispose();
            pollCancellation = null;
        }

        public async Task SetLobbyLocked(bool isLocked)
        {
            if (currentLobby == null)
            {
                OnLobbyError?.Invoke("Aucun lobby actif");
                return;
            }

            try
            {
                await LobbyService.Instance.UpdateLobbyAsync(
                    currentLobby.Id,
                    new UpdateLobbyOptions
                    {
                        IsLocked = isLocked
                    });
                    
                Debug.Log($"Lobby {(isLocked ? "verrouillé" : "déverrouillé")}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Échec de verrouillage du lobby: {e.Message}");
                OnLobbyError?.Invoke($"Impossible de verrouiller le lobby: {e.Message}");
            }
        }

    }
}