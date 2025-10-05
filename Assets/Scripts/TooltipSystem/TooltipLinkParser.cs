using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Characters.Powers;
using Characters.Powers.PowerComponents;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TooltipSystem
{
    public class TooltipLinkParser : MonoBehaviour
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

            if (_linkKey.StartsWith("powercomponent_"))
            {
                _linkKey = _linkKey.Replace("powercomponent_", "");
                return GetPowerComponentTooltipReference(_linkKey);
            }
            
            return textTooltipReferences.GetValueOrDefault(_linkKey);
        }

        private TooltipReference GetPowerComponentTooltipReference(string _linkKey)
        {
            var _ids = _linkKey.Split('_');
            ulong _ownerClientId = ulong.Parse(_ids[0]);
            ulong _powerObjectId = ulong.Parse(_ids[1]);
            int _componentIndex = int.Parse(_ids[2]);

            Power _power = GameManager.instance.characterManager
                .GetCharacter(_ownerClientId, false)?.role.powers.Find(_p => _p.NetworkObjectId == _powerObjectId);
            if (!_power)
            {
                Debug.LogWarning("Power not found for tooltip: " + _linkKey);
                return null;
            }

            List<PowerComponent> _powerComponents = _power.powerComponents.ToList();
            if (_componentIndex < 0 || _componentIndex >= _powerComponents.Count)
            {
                Debug.LogWarning("PowerComponent index out of range for tooltip: " + _linkKey);
                return null;
            }

            PowerComponent _powerComponent = _powerComponents[_componentIndex];
            
            var _tooltipReference = new TooltipReference()
            {
                title = _powerComponent.componentName.ToString(),
                description = _powerComponent.description.ToString(),
            };
            
            _tooltipReference.description = ParseText(_powerComponent, _tooltipReference.description);

            return _tooltipReference;
        }

        private TooltipReference GetPowerTooltipReference(string _linkID)
        {
            var _ids = _linkID.Split('_');
            ulong _ownerClientId = ulong.Parse(_ids[0]);
            ulong _powerObjectId = ulong.Parse(_ids[1]);

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
            
            List<PowerComponent> _powerComponents = _power.powerComponents.ToList();
            
            if (_powerComponents.Count == 0)
            {
                return _tooltipReference;
            }

            _tooltipReference.description += "\n";
            
            for (var _index = 0; _index < _powerComponents.Count; _index++)
            {
                var _powerComponent = _powerComponents[_index];
                if (_index > 0)
                {
                    _tooltipReference.description += ", ";
                }
                _tooltipReference.description += $"<link=powercomponent_{_ownerClientId}_{_power.NetworkObjectId}_{_index}>{_powerComponent.componentName.ToString()}</link>";
            }

            _tooltipReference.description = ParseText(_power, _tooltipReference.description);
            
            return _tooltipReference;
        }
        
        public string ParseText(Object _parsingObject, string _text)
        {
            var _customBalises = GetTextBetweenBraces(_text);
            
            foreach (var _balise in _customBalises)
            {
                var _baliseCommand = _balise.Split(':')[0];
                var _baliseParam = _balise.Split(':')[1];
                string _replaceText = "";
                switch (_baliseCommand)
                {
                    case "var":
                        var _varName = GetVarValue(_parsingObject, _baliseParam);
                        if (_varName != null)
                        {
                            _replaceText = _varName.ToString();
                        }
                        break;
                    default:
                        Debug.LogWarning("Unknown balise command: " + _baliseCommand);
                        break;
                }
                
                _text = _text.Replace("{" + _balise + "}", _replaceText);
            }
            
            return _text;
        }
        
        private object GetVarValue(Object _parsingObject, string _varName)
        {
            var _type = _parsingObject.GetType();
            var _field = _type.GetField(_varName);
            if (_field == null)
            {
                return null;
            }
            var _varValue = _field.GetValue(_parsingObject);
            if (_varValue is Object _unityObject)
            {
                if (_unityObject == null)
                {
                    return null;
                }
                if (_unityObject is Sprite _sprite)
                {
                    return _sprite.name;
                }
                
                // Gestion générique de NetworkVariable<T>
                var _nvType = _unityObject.GetType();
                if (_nvType.IsGenericType && _nvType.GetGenericTypeDefinition().Name.StartsWith("NetworkVariable"))
                {
                    var _valueProp = _nvType.GetProperty("Value");
                    if (_valueProp != null)
                    {
                        return _valueProp.GetValue(_unityObject);
                    }
                }
                
                return _unityObject.ToString();
            }
            return _varValue;
        }
        
        private List<string> GetTextBetweenBraces(string _text)
        {
            var _matches = System.Text.RegularExpressions.Regex.Matches(_text, @"\{([^}]*)\}");
            return _matches.Select(m => m.Groups[1].Value).ToList();
        }
    }
}
