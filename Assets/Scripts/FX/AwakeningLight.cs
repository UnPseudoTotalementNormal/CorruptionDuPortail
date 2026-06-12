using System;
using Characters;
using DG.Tweening;
using Extensions;
using FMODUnity;
using GameLogic;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX
{
    public class AwakeningLight : MonoBehaviour
    {
        [SerializeField] private Light lightComponent;
        private float baseIntensity;
        
        private bool isTurnedOn = false;
        
        [SerializeField] private EventReference turnOnSoundEvent;
        [SerializeField] private EventReference turnOffSoundEvent;
        // Story 7.4 lane A: scene-wired, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;
        // Story 8.3 lane A: scene-wired GameManager, narrowed to the loop-command slice (IGameLoop).
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "AwakeningLight.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "AwakeningLight.gameManager is not wired — wire it in GameScene (the composition root).");
            if (lightComponent == null)
            {
                lightComponent = GetComponent<Light>();
            }
            baseIntensity = lightComponent.intensity;
            lightComponent.intensity = 0f;
            lightComponent.enabled = true;
            Loop.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            CharacterQuery.GetLocalCharacter(false).isAwakened.OnValueChanged += OnAwakeningChanged;
        }

        private void OnAwakeningChanged(bool _previousValue, bool _newValue)
        {
            if (_newValue)
            {
                TurnOnLight();
            }
            else
            {
                TurnOffLight();
            }
        }

        private void TurnOnLight()
        {
            if (isTurnedOn)
            {
                return;
            }
            lightComponent.DOIntensity(baseIntensity, 1f).SetEase(Ease.OutQuint);
            isTurnedOn = true;
            turnOnSoundEvent.TryPlayOneShot();
        }
        
        private void TurnOffLight()
        {
            if (!isTurnedOn)
            {
                return;
            }
            lightComponent.DOIntensity(0, 0.5f).SetEase(Ease.OutQuint);
            isTurnedOn = false;
            turnOffSoundEvent.TryPlayOneShot();
            
        }

        private void Reset()
        {
            lightComponent = GetComponent<Light>();
        }
    }
}
