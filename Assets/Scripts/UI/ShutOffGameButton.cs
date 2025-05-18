using System;
using Unity.Netcode;
using UnityEngine;

namespace UI
{
    public class ShutOffGameButton : MonoBehaviour
    {
        private void Awake()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                gameObject.SetActive(false);
                return;
            }
            
            GetComponent<CustomButton>().onButtonClicked += ShutOffGame;
        }

        private void ShutOffGame()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }
            
            ShutOffGameRpc();
        }

        [Rpc(SendTo.Everyone)]
        private void ShutOffGameRpc()
        {
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }
    }
}
