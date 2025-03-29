using System;
using System.Collections.Generic;
using Extensions;
using UnityEngine;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "AwakeningState", menuName = "GameStates/AwakeningState")]
    public class AwakeningState : GameState
    {
        public List<AwakeningLayerObject> awakeningOrder;
        
        public int currentAwakeningIndex;

        private void AwakeLayer(int layerToAwake)
        {
            foreach (Character characterToAwake in awakeningOrder[layerToAwake].awakeningCharacters)
            {
                foreach (Character characterInGame in gameManager.characters)
                {
                    if (!characterInGame.IsTheSameCharacter(characterToAwake))
                    {
                        continue;
                    }
                    //TODO: Awaken the character
                }
            }
        }

        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
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

        public override void StateUpdate()
        {
            base.StateUpdate();
        }
    }
}

[Serializable]
public class AwakeningLayerObject
{
    public List<Character> awakeningCharacters = new();
    
    public Character this[int key]
    {
        get { return awakeningCharacters[key]; }
        set { awakeningCharacters[key] = value; }
    }
}