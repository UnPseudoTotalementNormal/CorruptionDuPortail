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

            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += Testttt;
                return;
            }
            
        }

        private void Testttt(ulong obj)
        {
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(DoStuffRpc),
                new[] { new NetworkSerializableObject(69), new NetworkSerializableObject((FixedString64Bytes)"pretty cool huh! ") },
                new StateRpcParams(StateRpcParams.RpcTargetType.single, new []{obj}));
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