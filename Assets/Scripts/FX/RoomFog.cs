using System;
using Booleans;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;

namespace FX
{
    public class RoomFog : MonoBehaviour
    {
        private enum FogReason
        {
            GameState,
            PlayerAwakened
        }
        
        private ParticleSystem[] particleSystems;
        private CompositeBool<FogReason> shouldShowFog = new();

        private void Awake()
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        private void Start()
        {
            shouldShowFog.Set(FogReason.GameState, false);
            shouldShowFog.Set(FogReason.PlayerAwakened, true);
            UpdateFogState();
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            CharacterManager.instance.GetLocalCharacter(false).isAwakened.OnValueChanged += OnAwakeningChanged;
            GameManager.instance.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            bool isInAwakeningState = GameManager.instance.GetGameState(_newValue) is AwakeningState;
            shouldShowFog.Set(FogReason.GameState, isInAwakeningState);
            UpdateFogState();
        }
        

        private void OnAwakeningChanged(bool _previousValue, bool _newValue)
        {
            shouldShowFog.Set(FogReason.PlayerAwakened, !_newValue);
            UpdateFogState();
        }

        private void UpdateFogState()
        {
            if (shouldShowFog)
            {
                EnableFog();
            }
            else
            {
                DisableFog();
            }
        }

        public void EnableFog()
        {
            foreach (var _particleSystem in particleSystems)
            {
                _particleSystem.Play(true);
            }
        }

        public void DisableFog()
        {
            foreach (var _particleSystem in particleSystems)
            {
                _particleSystem.Stop(true);
            }
        }
    }
}
