using System;
using Characters.Powers.Interfaces;
using UnityEngine;

namespace Characters.Powers.PowerComponents
{
    [Tooltip("Ends the turn (spends the precedent power); does the concentrated action if the precedent power wasn't used this turn")]
    public class PCConcentrated : PowerComponent
    {
        public string concentratedEffectDescription => "Termine le tour ; si \"{var:precedentPowerName}\" n'est pas utilisé, " 
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

            // The concentrated effect only when the precedent power is untouched this turn...
            if (_precedentPower.powerUseLeft.Value == _precedentPower.maxPowerUse)
            {
                if (power is IConcentratedPowerEffect _cPower)
                {
                    _cPower.OnConcentratedEffectServer();
                }
                else
                {
                    Debug.LogError("PCConcentrated used on a power that doesn't implement IConcentratedPowerEffect: " + power.powerName);
                }
            }

            // ...but using this power always ends the turn (GD wording of Corruption Ciblée: "puis finit son tour"):
            // the precedent power is spent too, so AwakeningState's no-usable-power check puts the owner to sleep.
            _precedentPower.powerUseLeft.Value = 0;
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