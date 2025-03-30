using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Extensions;
using UnityEngine;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "AwakeningState", menuName = "GameStates/AwakeningState")]
    public class AwakeningState : GameState
    {
        public List<AwakeningLayerObject> awakeningOrder;
        public List<Role> currentlyAwakenedCharacters = new();
        
        public int currentAwakeningIndex;

        public float currentAwakeningTimer;

        private void AwakeLayer(int layerToAwake)
        {
            currentlyAwakenedCharacters.Clear();
            foreach (RoleDataObject _characterToAwake in awakeningOrder[layerToAwake].awakeningCharacters)
            {
                List<Role> _rolesInGame = gameManager.characters.Select(character => character.role).ToList();
                foreach (Role _curentRoleInGame in _rolesInGame)
                {
                    if (!_curentRoleInGame.IsTheSameRole(_characterToAwake.role))
                    {
                        continue;
                    }
                    currentlyAwakenedCharacters.Add(_curentRoleInGame);
                    //TODO: Awaken the character
                }
            }
            currentAwakeningTimer = CalculateAwakeningTimer();
        }

        public float CalculateAwakeningTimer()
        {
            //todo: calculate the awakening timer based on the characters awakened
            return 5f;
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