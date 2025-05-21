#region

using UnityEngine;

#endregion

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
            gameManager.NextGameState();
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
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}