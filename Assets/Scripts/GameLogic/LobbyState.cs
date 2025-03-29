using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameLogic
{
    [CreateAssetMenu(fileName = "LobbyState", menuName = "GameStates/LobbyState")]
    public class LobbyState : GameState
    {
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartState()
        {
            base.OnStartState();
        }

        private void DoStuffRpc(int cool, FixedString64Bytes coolString)
        {
            Debug.Log(coolString + cool.ToString());
        }

        public override void OnEndState()
        {
            base.OnEndState();
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
        }
        
        public void OnStartGameButtonPressed()
        {
            gameManager.NextGameState();
        }
    }
}