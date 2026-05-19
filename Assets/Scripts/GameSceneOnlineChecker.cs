#region

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#endregion

public class GameSceneOnlineChecker : MonoBehaviour
{
    public bool checkIsNotNull = true;
    public bool checkIsConnected = true;
    private void Start()
    {
        bool _isNull = NetworkManager.Singleton == null;
        bool _isNotConnected = !NetworkManager.Singleton?.IsServer == true && !NetworkManager.Singleton?.IsClient == true;
        if ((_isNull && checkIsNotNull) || (_isNotConnected && checkIsConnected))
        {
            SceneManager.LoadScene(0);
        }
    }
}
