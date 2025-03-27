using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    [CreateAssetMenu(fileName = "VoteState", menuName = "GameStates/VoteState")]
    public class VoteState : GameState
    {
        public Dictionary<Character, int> votes;
        public float voteDuration;
        
        private float voteTimer;
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