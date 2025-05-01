using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Extensions;
using Network;
using UnityEngine;

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

        private void AwakeLayer(int _layerToAwake)
        {
            SleepCurrentlyAwakenedCharacters();

            foreach (RoleDataObject _roleToAwake in awakeningOrder[_layerToAwake].awakeningCharacters)
            {
                List<Character> _charactersInGame = gameManager.characters.ToList();
                foreach (Character _currentCharacter in _charactersInGame)
                {
                    if (!_currentCharacter.role.IsTheSameRole(_roleToAwake.role))
                    {
                        continue;
                    }
                    currentlyAwakenedCharacters.Add(_currentCharacter);
                    
                    gameManager.DoStateMethodRpc(GetType().FullName, nameof(AwakeCharacterRpc),
                        new[] {new NetworkSerializableObject(_currentCharacter.ownerClientId)}, 
                        new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{_currentCharacter.ownerClientId}));
                }
            }
            currentAwakeningTimer = CalculateAwakeningTimer(currentlyAwakenedCharacters.Select(_character => _character.role).ToList());
        }

        private void SleepCurrentlyAwakenedCharacters()
        {
            foreach (var _awakenedCharacter in currentlyAwakenedCharacters)
            {
                gameManager.DoStateMethodRpc(GetType().FullName, nameof(SleepCharacterRpc),
                    new[] {new NetworkSerializableObject(_awakenedCharacter.ownerClientId)}, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{_awakenedCharacter.ownerClientId}));
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

        public void AwakeCharacterRpc(ulong _characterClientId)
        {
            Character _character = gameManager.characters.FirstOrDefault(_c => _c.ownerClientId == _characterClientId);

            _character?.AwakenCharacter();
        }
        
        public void SleepCharacterRpc(ulong _characterClientId)
        {
            Character _character = gameManager.characters.FirstOrDefault(_c => _c.ownerClientId == _characterClientId);

            _character?.SleepCharacter();
        }

        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            currentAwakeningIndex = 0;
            AwakeLayer(currentAwakeningIndex);
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
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
            
            if (currentAwakeningTimer > 0)
            {
                return;
            }
            
            currentAwakeningIndex++;
            if (currentAwakeningIndex >= awakeningOrder.Count)
            {
                SleepCurrentlyAwakenedCharacters();
                gameManager.NextGameState();
                return;
            }
            
            AwakeLayer(currentAwakeningIndex);
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
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