#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters;
using Extensions;
using FMODUnity;
using Network;
using UnityEngine;
using Random = UnityEngine.Random;

#endregion

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "AwakeningState", menuName = "GameStates/AwakeningState")]
    public class AwakeningState : GameState
    {
        public List<AwakeningLayerObject> awakeningOrder;
        public List<Character> currentlyAwakenedCharacters = new();
        
        public int currentAwakeningIndex;

        public float currentAwakeningTimer;
        public float currentAwakeningMaxTime;
        
        private float updateAwakeningTimer;
        private const float UpdateAwakeningTimerInterval = 1f;
        
        public EventReference awakeningAnnouncementSound;
        public EventReference awakenedLoopSound;
        public const string AWAKENED_LOOP_KEY = "AwakenedLoopFeedback";

        private void AwakeLayer(int _layerToAwake)
        {
            SleepCurrentlyAwakenedCharacters();

            foreach (RoleDataObject _roleToAwake in awakeningOrder[_layerToAwake].awakeningCharacters)
            {
                List<Character> _charactersInGame = gameManager.characterManager.GetCharacters().ToList();
                foreach (Character _currentCharacter in _charactersInGame)
                {
                    if (!_currentCharacter.role.IsTheSameRole(_roleToAwake.role))
                    {
                        continue;
                    }
                    
                    if (_currentCharacter.isChained.Value || _currentCharacter.isEliminated.Value)
                    {
                        continue;
                    }
                    
                    currentlyAwakenedCharacters.Add(_currentCharacter);

                    gameManager.AwakeCharacterRpc(_currentCharacter.ownerClientId.Value);
                }
            }
            currentAwakeningMaxTime = currentAwakeningTimer = CalculateAwakeningTimer(currentlyAwakenedCharacters.Select(_character => _character.role).ToList());
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningMaxTimeRpc), new[] {new NetworkSerializableObject(currentAwakeningMaxTime)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningIndexRpc), new NetworkSerializableObject[] {new(currentAwakeningIndex)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningTimerRpc), new NetworkSerializableObject[] {new(currentAwakeningTimer)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
        }

        private void SleepCurrentlyAwakenedCharacters()
        {
            foreach (var _awakenedCharacter in currentlyAwakenedCharacters)
            {
                gameManager.SleepCharacterRpc(_awakenedCharacter.ownerClientId.Value);
                
            }

            currentlyAwakenedCharacters.Clear();
        }

        public float CalculateAwakeningTimer(List<Role> _awakeningRoles)
        {
            if (_awakeningRoles.Count == 0)
            {
                return 0f;
            }
            
            float _maxAwakeningTime = 0f;
            foreach (var _role in _awakeningRoles)
            {
                foreach (var _power in _role.powers)
                {
                    _maxAwakeningTime = Mathf.Max(_maxAwakeningTime, _power.maxWaitTime);
                }
            }
            return _maxAwakeningTime;
        }

        

        public override void OnStateCreated()
        { 
            base.OnStateCreated();
            gameManager.onGameStarted += () =>
            {
                var _localCharacter = gameManager.characterManager.GetLocalCharacter(false);
                if (_localCharacter != null)
                {
                    _localCharacter.onCharacterAwakened += () =>
                    {
                        GameAudioManager.instance.PlayOneShot(awakeningAnnouncementSound);
                        GameAudioManager.instance.PlayEventInstance(awakenedLoopSound, AWAKENED_LOOP_KEY);
                    };
                    _localCharacter.onCharacterSleep += () =>
                    {
                        GameAudioManager.instance.StopEventInstance(AWAKENED_LOOP_KEY);
                    };
                }
            };
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            currentAwakeningIndex = 0;
            AwakeLayer(currentAwakeningIndex);
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningIndexRpc), new NetworkSerializableObject[] {new(currentAwakeningIndex)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningTimerRpc), new NetworkSerializableObject[] {new(currentAwakeningTimer)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
            
            gameManager.characterManager.onCharactersListUpdated += OnCharactersListUpdatedWhileAwakening;
        }

        public override void OnEndStateServer()
        {
            gameManager.characterManager.onCharactersListUpdated -= OnCharactersListUpdatedWhileAwakening;
            base.OnEndStateServer();
        }
        
        private void OnCharactersListUpdatedWhileAwakening(List<Character> _characters)
        {
            var _isAnyCharacterAwakened = _characters.Any(_c => _c.isAwakened.Value);
            if (_isAnyCharacterAwakened)
            {
                return;
            }
            
            GoToNextAwakeLayer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
            currentAwakeningTimer -= Time.deltaTime;
            updateAwakeningTimer -= Time.deltaTime;
            
            if (updateAwakeningTimer <= 0)
            {
                updateAwakeningTimer = UpdateAwakeningTimerInterval;
                gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningTimerRpc), new NetworkSerializableObject[] {new(currentAwakeningTimer)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
            }

            //handle fake skip/used power
            if (currentAwakeningTimer <= currentAwakeningMaxTime / 1.25f)
            {
                var _fakeAwakenedCharacters = gameManager.characterManager.GetCharacters(false)
                    .Where(_c => _c.ownerClientId.Value.IsFakeClientId() && _c.isAwakened.Value);
                foreach (var _fakeAwakenedCharacter in _fakeAwakenedCharacters)
                {
                    float _r = Random.Range(0.0f, 1.0f);
                    if (_r < 0.00045f)
                    {
                        gameManager.SleepCharacterRpc(_fakeAwakenedCharacter.ownerClientId.Value);
                    }
                }
            }
            
            if (currentAwakeningTimer > 0)
            {
                return;
            }
            
            GoToNextAwakeLayer();
        }

        private void GoToNextAwakeLayer()
        {
            currentAwakeningIndex++;
            
            if (currentAwakeningIndex >= awakeningOrder.Count)
            {
                SleepCurrentlyAwakenedCharacters();
                gameManager.NextGameState();
                return;
            }
            
            AwakeLayer(currentAwakeningIndex);
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateAwakeningTimerRpc), 
                new NetworkSerializableObject[] {new(currentAwakeningTimer)}, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.notHost));
        }

        public override void StateUpdateClient()
        {
            base.StateUpdateClient();

            if (gameManager.IsServer)
            {
                return;
            }
            currentAwakeningTimer -= Time.deltaTime;
        }
        
        private void UpdateAwakeningTimerRpc(float _newAwakeningTimer)
        {
            if (gameManager.IsServer)
            {
                return;
            }
            currentAwakeningTimer = _newAwakeningTimer;
        }
        
        private void UpdateAwakeningIndexRpc(int _newAwakeningIndex)
        {
            if (gameManager.IsServer)
            {
                return;
            }
            currentAwakeningIndex = _newAwakeningIndex;
        }
        
        private void UpdateAwakeningMaxTimeRpc(float _newAwakeningMaxTime)
        {
            if (gameManager.IsServer)
            {
                return;
            }
            currentAwakeningMaxTime = _newAwakeningMaxTime;
        }
    }
}

[Serializable]
public class AwakeningLayerObject
{
    public List<RoleDataObject> awakeningCharacters = new();
    
    public RoleDataObject this[int key]
    {
        get { return awakeningCharacters[key]; }
        set { awakeningCharacters[key] = value; }
    }
}