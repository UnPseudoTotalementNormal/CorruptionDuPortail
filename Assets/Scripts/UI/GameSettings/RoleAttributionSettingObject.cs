#region

using Characters;
using GameLogic.GameSettings;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace UI.GameSettings
{
    // Quick-dev gamesettings-refonte (2026-06-20): pure view. It used to mutate the RoleAttributionState SO
    // clone directly and resolve the state per call; it now reads/writes the role count through the
    // replicated, server-authoritative GameSettingsManager (passed in by the tab) and mirrors replicated
    // changes via Refresh(). Host-authoritative: the slider is interactable only on the host — non-host
    // views are read-only mirrors (preserves the pre-refactor de-facto behaviour).
    public class RoleAttributionSettingObject : MonoBehaviour
    {
        private RoleDataObject roleDataObject;
        private GameSettingsManager gameSettingsManager;

        [SerializeField] private TMP_Text roleNameText;
        [SerializeField] private TMP_Text roleNumberToAttributeValueText;
        [SerializeField] private Slider roleNumberToAttributeSlider;

        private void Start()
        {
            roleNumberToAttributeSlider.onValueChanged.AddListener(OnRoleToAttributeValueChanged);
        }

        public void Setup(RoleDataObject _roleDataObject, GameSettingsManager _gameSettingsManager)
        {
            roleDataObject = _roleDataObject;
            gameSettingsManager = _gameSettingsManager;
            // Host-authoritative editing (spec default): non-host sliders are read-only mirrors. Flip
            // GameSettingsManager._allowClientEditing (+ this gate) to open editing to all clients.
            roleNumberToAttributeSlider.interactable = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            Refresh();
        }

        public void Refresh()
        {
            int _count = gameSettingsManager != null ? gameSettingsManager.GetRoleCount(roleDataObject.role.roleID) : 0;
            // SetValueWithoutNotify: Refresh mirrors the replicated value; firing onValueChanged here would
            // bounce a redundant write back into the manager (and loop on the replication callback).
            roleNumberToAttributeSlider.SetValueWithoutNotify(_count);
            roleNumberToAttributeValueText.text = _count.ToString();
            roleNameText.text = roleDataObject.role.roleName.ToString();
        }

        private void OnRoleToAttributeValueChanged(float _number)
        {
            int _rolesToAttribute = (int)_number;
            roleNumberToAttributeValueText.text = _rolesToAttribute.ToString();
            gameSettingsManager?.RequestSetRoleCount(roleDataObject.role.roleID, _rolesToAttribute);
        }
    }
}
