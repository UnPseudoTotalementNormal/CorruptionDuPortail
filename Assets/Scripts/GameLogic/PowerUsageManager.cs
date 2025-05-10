using System;
using System.Linq;
using Board.UI.PowerBar;
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
            var _playerPower = GameManager.instance.GetLocalCharacter(false).role.powers.FirstOrDefault(_p => _p.IsTheSamePower(_power));
            Assert.IsNotNull(_playerPower, "power was not found in the character's powers");
            
            if (_playerPower.CanUse())
            {
                _playerPower.StartUse();
                currentPower = _playerPower;
            }
            else
            {
                _playerPower.Cancel();
            }
        }

        private void OnPowerClicked(Power _power)
        {
            TrySelectPower(_power);
        }

        private void Update()
        {
            if (currentPower == null)
            {
                return;
            }

            if (currentPower.isCurrentlyUsed == false)
            {
                currentPower = null;
                return;
            }
            
            currentPower.UsingPowerUpdate();
        }
    }
}