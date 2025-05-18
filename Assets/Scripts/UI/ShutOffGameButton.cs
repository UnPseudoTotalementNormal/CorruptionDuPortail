using System;
using GameLogic;
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

            GameManager.instance.ShutOffGameRpc();
        }

    }
}
