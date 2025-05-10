using System;
using System.Linq;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace Board.UI.CharacterBar
{
    public class CharacterAwakenTimer : MonoBehaviour
    {
        [SerializeField] private Image timerImage;
        
        private Role role;

        private AwakeningState awakeningState;
        private int awakeningLayerIndex;
        
        private void Start()
        {
            role = GetComponentInParent<CharactersBarObject>().playerCharacter.role;
            
            awakeningState = (AwakeningState)GameManager.instance.GetGameStates(typeof(AwakeningState)).FirstOrDefault();
            Assert.IsNotNull(awakeningState, "AwakeningState is null");
            
            AwakeningLayerObject _awakeningLayerObject = awakeningState.awakeningOrder.FirstOrDefault(
                _a => _a.awakeningCharacters.Any(_r => _r.role.IsTheSameRole(role)));
            Assert.IsNotNull(_awakeningLayerObject, "AwakeningLayerObject is null");

            awakeningLayerIndex = awakeningState.awakeningOrder.IndexOf(_awakeningLayerObject);
        }

        public void Update()
        {
            
            if (awakeningLayerIndex == awakeningState.currentAwakeningIndex && 
                GameManager.instance.GetCharacters(false).First(_c => _c.role.IsTheSameRole(role)).role.isAwakened)
            {
                float _timer = awakeningState.currentAwakeningTimer;
                float _maxTimer = awakeningState.currentAwakeningMaxTime;
                float _percentage = (_timer / _maxTimer);
                timerImage.fillAmount = _percentage;
                timerImage.enabled = true;
            }
            else
            {
                timerImage.enabled = false;
            }
        }
    }
}
