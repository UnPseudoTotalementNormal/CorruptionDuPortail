#region

using Unity.Netcode;
using UnityEngine;

#endregion

public class IsServerTest : MonoBehaviour
{
    public void OnTest()
    {
        Debug.Log(NetworkManager.Singleton.IsServer);
    }
}
