using System;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;

public class RoleAttributionSettingObject : MonoBehaviour
{
    private RoleDataObject roleDataObject;
    
    [SerializeField] private TMP_Text roleNameText;
    [SerializeField] private TMP_Text roleNumberToAttributeValueText;
    [SerializeField] private Slider roleNumberToAttributeSlider;

    public void SetRoleDataObject(RoleDataObject _roleDataObject)
    {
        roleDataObject = _roleDataObject;
        Init();
    }

    private void Init()
    {
        roleNumberToAttributeSlider.value = GetRoleAttributionSetting().roleToAttribute;
        roleNumberToAttributeValueText.text = GetRoleAttributionSetting().roleToAttribute.ToString();
        roleNameText.text = roleDataObject.role.roleName.ToString();
        
        roleNumberToAttributeSlider.onValueChanged.AddListener(OnRoleToAttributeValueChanged);
    }

    private void OnRoleToAttributeValueChanged(float _number)
    {
        int _rolesToAttribute = (int)_number;
        roleNumberToAttributeValueText.text = _rolesToAttribute.ToString();
        GetRoleAttributionSetting().roleToAttribute = _rolesToAttribute;
    }

    private RoleAttributionSetting GetRoleAttributionSetting()
    {
        var _roleAttributionState = (RoleAttributionState)GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First();
        return _roleAttributionState.roleAttributionDictionary[roleDataObject];
    }
}
