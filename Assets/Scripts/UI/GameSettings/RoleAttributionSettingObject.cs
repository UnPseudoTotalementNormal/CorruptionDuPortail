#region

using System;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace UI.GameSettings
{
    public class RoleAttributionSettingObject : MonoBehaviour
    {
        private RoleDataObject roleDataObject;
    
        [SerializeField] private TMP_Text roleNameText;
        [SerializeField] private TMP_Text roleNumberToAttributeValueText;
        [SerializeField] private Slider roleNumberToAttributeSlider;

        public event Action onValueChanged;

        private void Start()
        {
            roleNumberToAttributeSlider.onValueChanged.AddListener(OnRoleToAttributeValueChanged);
        }

        public void SetRoleDataObject(RoleDataObject _roleDataObject)
        {
            roleDataObject = _roleDataObject;
            Refresh();
        }

        public void Refresh()
        {
            roleNumberToAttributeSlider.value = GetRoleAttributionSetting().roleToAttribute;
            roleNumberToAttributeValueText.text = GetRoleAttributionSetting().roleToAttribute.ToString();
            roleNameText.text = roleDataObject.role.roleName.ToString();
        }

        private void OnRoleToAttributeValueChanged(float _number)
        {
            int _rolesToAttribute = (int)_number;
            roleNumberToAttributeValueText.text = _rolesToAttribute.ToString();
            GetRoleAttributionSetting().roleToAttribute = _rolesToAttribute;
            onValueChanged?.Invoke();
        }

        private RoleAttributionSetting GetRoleAttributionSetting()
        {
            // Story 12.3: prefab-resident settings object — sanctioned CompositionRoot.For(Singleton) route.
            var _roleAttributionState = (RoleAttributionState)CompositionRoot.For(NetworkManager.Singleton).GameManager.GetGameStates(typeof(RoleAttributionState)).First();
            return _roleAttributionState.roleAttributionDictionary[roleDataObject];
        }
    }
}
