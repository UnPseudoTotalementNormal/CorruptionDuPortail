using UnityEngine;

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "AwakeningRecapState", menuName = "GameStates/AwakeningRecapState")]
    public class AwakeningRecapState : GameState
    {
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