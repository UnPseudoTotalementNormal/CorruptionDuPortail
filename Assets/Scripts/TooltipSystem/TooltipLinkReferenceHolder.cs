using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters.Powers;
using Characters.Powers.PowerComponents;
using GameLogic;
using UnityEngine;

namespace TooltipSystem
{
    public class TooltipLinkReferenceHolder : MonoBehaviour
    {
        [SerializeField] private SerializedDictionary<string, TooltipReference> textTooltipReferences = new();

        private void Awake()
        {
            foreach (var _key in new List<string>(textTooltipReferences.Keys))
            {
                var _value = textTooltipReferences[_key];
                textTooltipReferences.Remove(_key);
                textTooltipReferences[_key.ToLower()] = _value;
            }
        }

        public TooltipReference GetTooltipReference(string _linkKey)
        {
            _linkKey = _linkKey.ToLower();
            
            if (_linkKey.StartsWith("power_"))
            {
                _linkKey = _linkKey.Replace("power_", "");
                return GetPowerTooltipReference(_linkKey);
            }
            
            return textTooltipReferences.GetValueOrDefault(_linkKey);
        }
        
        private TooltipReference GetPowerTooltipReference(string _linkID)
        {
            var _ids = _linkID.Split('_');
            ulong _ownerClientId = (ulong)int.Parse(_ids[0]);
            ulong _powerObjectId = (ulong)int.Parse(_ids[1]);

            Power _power = GameManager.instance.characterManager
                .GetCharacter(_ownerClientId, false)?.role.powers.Find(_p => _p.NetworkObjectId == _powerObjectId);
            if (!_power)
            {
                Debug.LogWarning("Power not found for tooltip: " + _linkID);
                return null;
            }
            
            var _tooltipReference = new TooltipReference()
            {
                title = _power.powerName.ToString(),
                description = _power.powerDescription.ToString(),
            };
            
            List<PowerComponent> _powerComponents = new();
            
            return _tooltipReference;
        }
    }
}
