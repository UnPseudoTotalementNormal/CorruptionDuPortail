using System;
using Characters.Powers.Interfaces;
using UnityEngine;

namespace Characters.Powers.PowerComponents
{
    [Tooltip("Will do the power concentrated action if the precedent power wasn't used this turn")]
    public class PCConcentrated : PowerComponent
    {
        public string concentratedEffectDescription => "Si \"{var:precedentPowerName}\" n'est pas utilisé, renonce à son utilisation et " 
                                                       + (power as IConcentratedPowerEffect)?.concentratedEffectDescription;
        public string precedentPowerName => GetPrecedentPower() ? GetPrecedentPower().powerName.ToString() : "Power not found";
        
        protected override void Init()
        {
            if (!NetworkManager.IsServer)
            {
                return;
            }
            
            power.onPowerUsedServer += OnPowerUsed;
        }

        private void Reset()
        {
            componentName = "Concentration";
            description = "{var:concentratedEffectDescription}";
        }

        private void OnPowerUsed()
        {
            var _precedentPower = GetPrecedentPower();
            if (!_precedentPower)
            {
                return;
            }

            if (_precedentPower.powerUseLeft.Value != _precedentPower.maxPowerUse)
            {
                return;
            }
            
            if (power is IConcentratedPowerEffect _cPower)
            {
                _cPower.OnConcentratedEffectServer();
                _precedentPower.powerUseLeft.Value = 0;
            }
            else
            {
                Debug.LogError("PCConcentrated used on a power that doesn't implement IConcentratedPowerEffect: " + power.powerName);
            }
        }

        private Power GetPrecedentPower()
        {
            int _siblingIndex = power.transform.GetSiblingIndex();
            Power _precedentPower = null;
            
            if (_siblingIndex == 0)
            {
                return _precedentPower;
            }
            
            _precedentPower = power.transform.parent.GetChild(_siblingIndex - 1).GetComponent<Power>();

            return _precedentPower;
        }
    }
}