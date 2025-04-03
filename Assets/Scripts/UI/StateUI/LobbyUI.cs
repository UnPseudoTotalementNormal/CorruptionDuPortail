using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

public class LobbyUI : StateUI
{
    public void StartGame()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            ((LobbyState)owningGameState).OnStartGameButtonPressed();
        }
    }
}
