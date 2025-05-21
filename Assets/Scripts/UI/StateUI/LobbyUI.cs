#region

using GameLogic.GameStates;
using Unity.Netcode;

#endregion

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
