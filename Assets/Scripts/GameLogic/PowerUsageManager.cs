#region

using System.Linq;
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
        
        [HideInInspector] public bool hasGameStarted; //temp
        
        private void Start()
        {
            hasGameStarted = false;
            GameManager.instance.powersBar.onPowerClicked += OnPowerClicked;
            if (NetworkManager.Singleton.IsServer)
            {
                var _roleAttributionGameState = GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First();
                int _index = GameManager.instance.gameStates.Keys.ToList().IndexOf(_roleAttributionGameState);
                var _startGameState = GameManager.instance.gameStates.Keys.ToList()[_index + 1];
                _startGameState.onStateStartServer += OnGameStarted; //TODO : ADD A REAL GAME STARTED EVENT
            }
        }

        private void OnGameStarted()
        {
            if (hasGameStarted)
            {
                return;
            }

            hasGameStarted = true;
            
            foreach (var _rolePower in GameManager.instance.GetCharacters().SelectMany(_character => _character.role.powers))
            {
                _rolePower.OnGameStartedServer();
            }
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