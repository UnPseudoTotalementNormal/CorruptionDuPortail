#region

using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharacterAwakenTimer : MonoBehaviour
    {
        public AwakeningTimerType awakeningTimerType = AwakeningTimerType.SpecificCharacter;
        
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
            
            if (awakeningLayerIndex == awakeningState.currentAwakeningIndex)
            {
                Character _characterOwner = GameManager.instance.characterManager.GetCharacters(false)
                    .First(c => c.ownerClientId == role.ownerClientId);
                bool _isAnySameRoleAwakened = GameManager.instance.characterManager.GetCharacters(false)
                    .Any(c => c.role.IsTheSameRole(role) && c.role.isAwakened);
                if ((awakeningTimerType == AwakeningTimerType.SpecificCharacter && _characterOwner.role.isAwakened) ||
                    (awakeningTimerType == AwakeningTimerType.AnyRole && _isAnySameRoleAwakened))
                {
                    UpdateTimer();
                }
                else
                {
                    timerImage.gameObject.SetActive(false);
                }
            }
            else
            {
                timerImage.gameObject.SetActive(false);
            }
        }

        private void UpdateTimer()
        {
            float _timer = awakeningState.currentAwakeningTimer;
            float _maxTimer = awakeningState.currentAwakeningMaxTime;
            float _percentage = (_timer / _maxTimer);
            timerImage.fillAmount = _percentage;
            timerImage.gameObject.SetActive(true);
        }

        public enum AwakeningTimerType
        {
            AnyRole,
            SpecificCharacter
        }
    }
}
