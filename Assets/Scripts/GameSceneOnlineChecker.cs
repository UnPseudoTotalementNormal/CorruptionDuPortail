#region

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#endregion

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
