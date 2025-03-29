using GameLogic.GameStates;
using Unity.Netcode;

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
