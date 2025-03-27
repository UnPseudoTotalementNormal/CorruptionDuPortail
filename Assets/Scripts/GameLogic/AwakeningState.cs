using System;
using System.Collections.Generic;
using Extensions;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    [CreateAssetMenu(fileName = "AwakeningState", menuName = "GameStates/AwakeningState")]
    public class AwakeningState : GameState
    {
        public List<Character> awakeningOrder;
        [HideInInspector] public List<Character> sortedAwakeningCharacters;
        
        public int currentAwakeningIndex;

        public List<Character> GetAwakeningPlayerSortedList()
        {
            List<Character> sortedList = gameManager.characters;
            
            sortedList.Sort((a, b) => awakeningOrder.IndexOf(a).CompareTo(awakeningOrder.IndexOf(b)));
            
            return sortedList;
        }

        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartState()
        {
            base.OnStartState();
        }

        public override void OnEndState()
        {
            base.OnEndState();
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
        }
    }
}