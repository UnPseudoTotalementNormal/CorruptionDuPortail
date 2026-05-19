using System;
using DG.Tweening;
using Extensions;
using FMODUnity;
using GameLogic;
using UnityEngine;

namespace FX
{
    public class AwakeningLight : MonoBehaviour
    {
        [SerializeField] private Light lightComponent;
        private float baseIntensity;
        
        private bool isTurnedOn = false;
        
        [SerializeField] private EventReference turnOnSoundEvent;
        [SerializeField] private EventReference turnOffSoundEvent;

        private void Start()
        {
            if (lightComponent == null)
            {
                lightComponent = GetComponent<Light>();
            }
            baseIntensity = lightComponent.intensity;
            lightComponent.intensity = 0f;
            lightComponent.enabled = true;
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            GameManager.instance.characterManager.GetLocalCharacter(false).isAwakened.OnValueChanged += OnAwakeningChanged;
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
