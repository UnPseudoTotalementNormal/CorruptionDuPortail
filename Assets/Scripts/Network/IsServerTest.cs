using Unity.Netcode;
using UnityEngine;

public class IsServerTest : MonoBehaviour
{
    public void OnTest()
    {
        Debug.Log(NetworkManager.Singleton.IsServer);
    }
}
