using UnityEngine;

namespace GameLogic
{
    [CreateAssetMenu(fileName = "GameEndingState", menuName = "GameStates/GameEndingState")]
    public class GameEndingState : GameState
    {
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