using System;
using System.Linq;
using Characters.Powers;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public class PowerUsageManager : MonoBehaviour
    {
        [HideInInspector] public Power currentPower;
        
        private void Start()
        {
            GameManager.instance.powersBar.onPowerClicked += OnPowerClicked;
        }

        private void TrySelectPower(Power _power)
        {
            currentPower = _power;
            var _playerPower = GameManager.instance.GetLocalCharacter().role.powers.FirstOrDefault(_p => _p == _power);
            Assert.IsNotNull(_playerPower, "power was not found in the character's powers");
            
            if (_playerPower.CanUse())
            {
                _playerPower.Use();
            }
        }

        private void OnPowerClicked(Power _power)
        {
            TrySelectPower(_power);
        }
    }
}