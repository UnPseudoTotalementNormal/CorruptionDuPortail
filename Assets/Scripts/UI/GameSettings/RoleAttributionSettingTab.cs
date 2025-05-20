using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

namespace UI.GameSettings
{
    public class RoleAttributionSettingTab : GameSettingTab
    {
        [SerializeField] private RoleAttributionSettingObject roleAttributionSettingObjectPrefab;
        [SerializeField] private Transform layoutTransform;
        
        private List<RoleAttributionSettingObject> roleAttributionSettingObjects = new();
    
        protected override void Init()
        {
            Debug.Log("RoleAttributionSettingTab Init");
            foreach (var _roleDataObject in GetRoleAttributionState().roleAttributionDictionary.Keys.ToList())
            {
                Debug.Log($"RoleAttributionSettingTab Init - {nameof(_roleDataObject)}: {_roleDataObject}");
                RoleAttributionSettingObject _roleAttributionSettingObject = Instantiate(roleAttributionSettingObjectPrefab, layoutTransform);
                _roleAttributionSettingObject.SetRoleDataObject(_roleDataObject);
                _roleAttributionSettingObject.onValueChanged += OnRoleAttributionSettingObjectValueChanged;
                roleAttributionSettingObjects.Add(_roleAttributionSettingObject);
            }
                
            AskForRefreshSettingsRpc();
        }

        private void OnRoleAttributionSettingObjectValueChanged()
        {
            AskForRefreshSettingsRpc();
        }

        [Rpc(SendTo.Server)]
        public override void AskForRefreshSettingsRpc()
        {
            List<RoleSettingsUpdater> _sendingRoleSettings = new();
            foreach (var _roleAttributionSetting in GetRoleAttributionState().roleAttributionDictionary)
            {
                if (_roleAttributionSetting.Key == null || _roleAttributionSetting.Key.role == null)
                {
                    Debug.LogError("RoleAttributionSettingTab: _roleAttributionSetting.Key ou .role est null lors de la création de RoleSettingsUpdater");
                    continue;
                }
                RoleSettingsUpdater _roleSettingsUpdater = new()
                {
                    forRole = _roleAttributionSetting.Key.role,
                    roleAttributionSetting = _roleAttributionSetting.Value
                };
                if (_roleSettingsUpdater.forRole == null)
                {
                    Debug.LogError("RoleAttributionSettingTab: forRole est null juste après l'assignation !");
                }
                _sendingRoleSettings.Add(_roleSettingsUpdater);
            }
            OnRefreshSettingsRpc(_sendingRoleSettings.ToArray());
        }
        
        [Rpc(SendTo.NotServer)]
        private void OnRefreshSettingsRpc(RoleSettingsUpdater[] _newRoleSettings)
        {
            foreach (var _newRoleSetting in _newRoleSettings)
            {
                var _rolePair = GetRoleAttributionState().roleAttributionDictionary
                    .First(_rs => _rs.Key.role.IsTheSameRole(_newRoleSetting.forRole));
                GetRoleAttributionState().roleAttributionDictionary[_rolePair.Key] = _newRoleSetting.roleAttributionSetting;
                _rolePair.Key.role = _newRoleSetting.forRole;
            }
            
            roleAttributionSettingObjects.ForEach(_r => _r.Refresh());
        }
    
        private RoleAttributionState GetRoleAttributionState()
        {
            return (RoleAttributionState)GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First();
        }
        
        [Serializable]
        private class RoleSettingsUpdater : INetworkSerializable
        {
            public Role forRole;
            public RoleAttributionSetting roleAttributionSetting;
            
            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                if (_serializer.IsReader && forRole == null)
                {
                    forRole = new Role();
                }
                if (_serializer.IsReader && roleAttributionSetting == null)
                {
                    roleAttributionSetting = new RoleAttributionSetting();
                }

                if (forRole == null)
                {
                    throw new Exception("forRole null lors de la sérialisation réseau." + ((roleAttributionSetting == null ? " role attribution settings aussi" : "") + " " + _serializer.IsReader));
                }
                if (roleAttributionSetting == null)
                {
                    throw new Exception("roleAttributionSetting null lors de la sérialisation réseau." + ((forRole == null ? " forRole aussi" : "") + " " + _serializer.IsReader));
                }

                _serializer.SerializeValue(ref forRole);
                _serializer.SerializeValue(ref roleAttributionSetting);
            }
        }
    }
}

