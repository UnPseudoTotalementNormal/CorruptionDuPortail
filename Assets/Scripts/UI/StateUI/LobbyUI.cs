using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

public class LobbyUI : StateUI
{
    public void StartGame()
    {
        TestRpc();
        
        if (NetworkManager.Singleton.IsServer)
        {
            ((LobbyState)owningGameState).OnStartGameButtonPressed();
        }
    }

    [Rpc(SendTo.Everyone)]
    public void TestRpc()
    {
        Debug.Log("cooooolllllll");
    }
}
