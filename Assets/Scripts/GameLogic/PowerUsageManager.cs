#region

using System.Linq;
using Board.UI.PowerBar;
using Characters;
using Characters.Powers;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    public class PowerUsageManager : MonoBehaviour
    {
        [HideInInspector] public Power currentPower;

        // Story 7.4 lane A: scene-wired, replacing the GameManager hub-hop (powersBar) and the
        // CharacterManager singleton locator.
        [SerializeField] private CharacterManager characterManager;
        [SerializeField] private PowersBar powersBar;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "PowerUsageManager.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(powersBar, "PowerUsageManager.powersBar is not wired — wire it in GameScene (the composition root).");
            powersBar.onPowerClicked += OnPowerClicked;
            characterManager.onLocalIdentityChanged += OnLocalIdentityChanged;
        }

        private void OnLocalIdentityChanged()
        {
            if (currentPower != null)
            {
                currentPower.Cancel();
                currentPower = null;
            }
        }

        private void TrySelectPower(Power _power)
        {
            var _playerPower = characterManager.GetLocalCharacter(false).role.powers.FirstOrDefault(_p => _p.IsTheSamePower(_power));
            Assert.IsNotNull(_playerPower, "power was not found in the character's powers");

            if (currentPower != null && !currentPower.IsTheSamePower(_power))
            {
                currentPower.Cancel();
            }
            
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