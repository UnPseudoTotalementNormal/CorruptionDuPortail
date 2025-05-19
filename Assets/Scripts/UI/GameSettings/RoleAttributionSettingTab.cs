using System.Linq;
using GameLogic;
using GameLogic.GameStates;
using UI.GameSettings;
using UnityEngine;

public class RoleAttributionSettingTab : GameSettingTab
{
    [SerializeField] private RoleAttributionSettingObject roleAttributionSettingObjectPrefab;
    [SerializeField] private Transform layoutTransform;
    
    protected override void Init()
    {
        if (IsServer)
        {
            Debug.Log("RoleAttributionSettingTab Init");
            foreach (var _roleDataObject in GetRoleAttributionState().roleAttributionDictionary.Keys.ToList())
            {
                Debug.Log($"RoleAttributionSettingTab Init - {nameof(_roleDataObject)}: {_roleDataObject}");
                RoleAttributionSettingObject _roleAttributionSettingObject = Instantiate(roleAttributionSettingObjectPrefab, layoutTransform);
                _roleAttributionSettingObject.SetRoleDataObject(_roleDataObject);
            }
        }
    }

    public override void ApplySettingsServer()
    {
        
    }
    
    private RoleAttributionState GetRoleAttributionState()
    {
        return (RoleAttributionState)GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First();
    }
}
