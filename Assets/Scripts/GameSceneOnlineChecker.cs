using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSceneOnlineChecker : MonoBehaviour
{
    private void Start()
    {
        if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient))
        {
            SceneManager.LoadScene("MenuScene");
        }
    }
}
