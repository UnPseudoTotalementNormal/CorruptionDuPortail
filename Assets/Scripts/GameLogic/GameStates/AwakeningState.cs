#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters;
using Characters.Powers;
using Extensions;
using FMODUnity;
using Network;
using Unity.Netcode;
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
        
        private Dictionary<Character, NetworkVariable<bool>.OnValueChangedDelegate> characterAwakeningCallbacks = new();
        
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

                    _currentCharacter.AwakenCharacterServerRpc();
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
                _awakenedCharacter.SleepCharacterServerRpc();
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
            
            foreach (var _character in CharacterManager.instance.GetCharacters(false))
            {
                // Créer une fonction anonyme avec le paramètre ownerId et la stocker
                NetworkVariable<bool>.OnValueChangedDelegate _callback = (_previousValue, _newValue) => 
                {
                    OnCharacterAwakeningChanged(_previousValue, _newValue, _character.ownerClientId.Value);
                };
                
                characterAwakeningCallbacks[_character] = _callback;
                _character.isAwakened.OnValueChanged += _callback;
            }
        }

        private void OnCharacterAwakeningChanged(bool _previousValue, bool _newValue, ulong _ownerId)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("OnCharacterAwakeningChanged can only be called on the server");
                return;
            }
            
            if (_newValue)
            {
                return;
            }
            
            var _character = gameManager.characterManager.GetCharacter(_ownerId, false);
            int _awakeningIndexForCharacter = awakeningOrder.FindIndex(_layer =>
                _layer.awakeningCharacters.Any(_rdo => _character.role.IsTheSameRole(_rdo.role))
            );
            
            if (_awakeningIndexForCharacter != currentAwakeningIndex)
            {
                return;
            }
            
            currentlyAwakenedCharacters.Remove(_character);
            
            if (currentlyAwakenedCharacters.Count == 0)
            {
                GoToNextAwakeLayer();
            }
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            foreach (var (_character, _callback) in characterAwakeningCallbacks)
            {
                _character.isAwakened.OnValueChanged -= _callback;
            }
            characterAwakeningCallbacks.Clear();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            _ = BoardManager.instance.ShowAllPlayerCards();
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
            
            if (currentAwakeningTimer <= currentAwakeningMaxTime / 1.25f) //handle fake skip/used power
            {
                var _fakeAwakenedCharacters = gameManager.characterManager.GetCharacters(false)
                    .Where(_c => _c.ownerClientId.Value.IsFakeClientId() && _c.isAwakened.Value);
                foreach (var _fakeAwakenedCharacter in _fakeAwakenedCharacters)
                {
                    float _r = Random.Range(0.0f, 1.0f);
                    if (_r < 0.00045f)
                    {
                        _fakeAwakenedCharacter.SleepCharacterServerRpc();
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

        public void OnPowerUsedServer(Power _newPower)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("OnPowerUsedServer can only be called on the server");
                return;
            }
            
            var _character = gameManager.characterManager.GetCharacter(_newPower.ownerClientId.Value, false);
            if (!currentlyAwakenedCharacters.Contains(_character))
            {
                return;
            }

            bool _canPlay = false;
            foreach (var _power in _character.role.powers)
            {
                if (_power.isPassive)
                {
                    continue;
                }
                
                if (_power.CanUse())
                {
                    _canPlay = true;
                    break;
                }
            }

            if (_canPlay)
            {
                return;
            }
            
            _character.SleepCharacterServerRpc();
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