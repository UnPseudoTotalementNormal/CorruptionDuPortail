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

        private void AwakeLayer(int layerToAwake)
        {
            currentlyAwakenedCharacters.Clear();
            foreach (RoleDataObject _roleToAwake in awakeningOrder[layerToAwake].awakeningCharacters)
            {
                List<Character> _charactersInGame = gameManager.characters.ToList();
                foreach (Character _curentCharacter in _charactersInGame)
                {
                    if (!_curentCharacter.role.IsTheSameRole(_roleToAwake.role))
                    {
                        continue;
                    }
                    currentlyAwakenedCharacters.Add(_curentCharacter);
                    
                    gameManager.DoStateMethodRpc(GetType().FullName, nameof(AwakeCharacterRpc),
                        new[] {new NetworkSerializableObject(_curentCharacter.ownerClientId)}, 
                        new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{_curentCharacter.ownerClientId}));
                }
            }
            currentAwakeningTimer = CalculateAwakeningTimer(currentlyAwakenedCharacters.Select(character => character.role).ToList());
        }

        public float CalculateAwakeningTimer(List<Role> _awakeningRoles)
        {
            if (_awakeningRoles.Count == 0)
            {
                return 0f;
            }
            
            //todo: calculate the awakening timer based on the characters awakened
            return 5f;
        }

        public void AwakeCharacterRpc(ulong characterClientId)
        {
            Character _character = gameManager.characters.FirstOrDefault(character => character.ownerClientId == characterClientId);
            if (_character == null)
            {
                return;
            }
            
            _character.AwakenCharacter();
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