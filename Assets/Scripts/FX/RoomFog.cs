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

        // Story 11.4 lifecycle hygiene: cache the exact GameManager + character whose events/NetworkVariables
        // we subscribe to, so OnDestroy can unsubscribe from the same instances. (These still read the
        // globals — RoomFog is the §4a-entangled component injected fully in Epic 12; this story only fixes
        // the teardown leak, it does not migrate the locator.)
        private GameManager _subscribedGameManager;
        private Character _subscribedAwakeningCharacter;

        private void Awake()
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        private void Start()
        {
            shouldShowFog.Set(FogReason.GameState, false);
            shouldShowFog.Set(FogReason.PlayerAwakened, true);
            UpdateFogState();
            _subscribedGameManager = GameManager.instance;
            _subscribedGameManager.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            _subscribedAwakeningCharacter = CharacterManager.instance.GetLocalCharacter(false);
            _subscribedAwakeningCharacter.isAwakened.OnValueChanged += OnAwakeningChanged;
            _subscribedGameManager.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
        }

        private void OnDestroy()
        {
            // Mirror all three subscriptions (Start → onGameStarted, OnGameStarted → isAwakened + currentGameStateIndex).
            if (_subscribedGameManager != null)
            {
                _subscribedGameManager.onGameStarted -= OnGameStarted;
                _subscribedGameManager.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
            if (_subscribedAwakeningCharacter != null)
            {
                _subscribedAwakeningCharacter.isAwakened.OnValueChanged -= OnAwakeningChanged;
            }
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
