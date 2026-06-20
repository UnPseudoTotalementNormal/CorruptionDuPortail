#region

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace UI.GameSettings
{
    // Quick-dev gamesettings-refonte (2026-06-20): pure view. It used to be a NetworkBehaviour with a
    // hand-rolled [Rpc] settings sync (AskForRefreshSettingsRpc / OnRefreshSettingsRpc / RoleSettingsUpdater)
    // embedded in a runtime-Instantiated prefab — fragile NGO identity. The role-attribution settings now
    // live on the replicated, server-authoritative GameSettingsManager; this tab just builds one widget per
    // authored role and refreshes them when the manager's replicated state changes.
    public class RoleAttributionSettingTab : GameSettingTab
    {
        [SerializeField] private RoleAttributionSettingObject roleAttributionSettingObjectPrefab;
        [SerializeField] private Transform layoutTransform;

        private readonly List<RoleAttributionSettingObject> roleAttributionSettingObjects = new();
        private GameSettingsManager gameSettingsManager;

        protected override void Init()
        {
            BuildWhenReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        // The lobby UI is now host-agnostic and can live on the tablet (a SCENE object whose Start runs before
        // GameManager.OnNetworkSpawn registers the per-NM instance). Wait until the composition root resolves
        // the graph before building — keeps the modular "resolve own deps" contract (no serialized scene ref
        // that would break on a reparent) without racing spawn. In the HUD overlay (instantiated post-spawn)
        // the predicate is already true, so this completes on the first check.
        private async UniTaskVoid BuildWhenReadyAsync(CancellationToken _cancellationToken)
        {
            await UniTask.WaitUntil(
                () => NetworkManager.Singleton != null
                      && CompositionRoot.For(NetworkManager.Singleton).GameSettingsManager != null
                      && CompositionRoot.For(NetworkManager.Singleton).GameManager != null,
                cancellationToken: _cancellationToken);

            // Defensive: if the tab was destroyed in the same frame the wait resolved, bail before mutating
            // shared state / subscribing, so OnDestroy's unsubscribe (which already ran) is not undone by a
            // late continuation (no leaked OnSettingsChanged handler firing on a destroyed tab).
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            gameSettingsManager = CompositionRoot.For(NetworkManager.Singleton).GameSettingsManager;

            foreach (RoleDataObject _roleDataObject in GetRoleAttributionState().roleAttributionDictionary.Keys.ToList())
            {
                RoleAttributionSettingObject _settingObject = Instantiate(roleAttributionSettingObjectPrefab, layoutTransform);
                _settingObject.Setup(_roleDataObject, gameSettingsManager);
                roleAttributionSettingObjects.Add(_settingObject);
            }

            gameSettingsManager.OnSettingsChanged += RefreshAll;
        }

        private void OnDestroy()
        {
            // Subscription symmetry (archi §5b): unsubscribe from the cached manager in the teardown mirror.
            if (gameSettingsManager != null)
            {
                gameSettingsManager.OnSettingsChanged -= RefreshAll;
            }
        }

        private void RefreshAll()
        {
            roleAttributionSettingObjects.ForEach(_settingObject => _settingObject.Refresh());
        }

        private RoleAttributionState GetRoleAttributionState()
        {
            // Prefab-resident view — the sanctioned Story-12.3 CompositionRoot.For(Singleton) route. The role
            // POOL + names live on the authored RoleAttributionState; the editable COUNTS live on the manager.
            return (RoleAttributionState)CompositionRoot.For(NetworkManager.Singleton).GameManager.GetGameStates(typeof(RoleAttributionState)).First();
        }
    }
}
