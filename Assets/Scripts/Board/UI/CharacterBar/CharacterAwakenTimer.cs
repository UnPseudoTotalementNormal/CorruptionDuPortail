#region

using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
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
            
            awakeningState = (AwakeningState)CompositionRoot.For(NetworkManager.Singleton).GameManager.GetGameStates(typeof(AwakeningState)).FirstOrDefault();
            Assert.IsNotNull(awakeningState, "AwakeningState is null");
            
            awakeningLayerIndex = awakeningState.GetAwakeningLayerIndex(role);
        }

        public void Update()
        {
            
            if (awakeningLayerIndex == awakeningState.currentAwakeningIndex)
            {
                // Story 12.3: prefab-resident UI leaf (CharacterBarObject prefab) — resolves through the sanctioned
                // CompositionRoot.For(Singleton) instead of the CharacterManager God-Object façade.
                ICharacterQuery _characters = CompositionRoot.For(NetworkManager.Singleton).CharacterQuery;
                Character _characterOwner = _characters.GetCharacters(false)
                    .First(c => c.ownerClientId.Value == role.ownerClientId);
                bool _isAnySameRoleAwakened = _characters.GetCharacters(false)
                    .Any(c => c.role.IsTheSameRole(role) && c.isAwakened.Value);
                if ((awakeningTimerType == AwakeningTimerType.SpecificCharacter && _characterOwner.isAwakened.Value) ||
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
