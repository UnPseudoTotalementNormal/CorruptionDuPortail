#region

using System.Collections.Generic;
using System.Linq;
using Characters.WinningConditions;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "GameIntroductionState", menuName = "GameStates/GameIntroductionState")]
    public class GameIntroductionState : GameState
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
            
            _ = BoardManager.instance.ShowAllPlayerCards();
            gameManager.charactersBar.ResetCharactersBar(gameManager.GetCharacters());
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