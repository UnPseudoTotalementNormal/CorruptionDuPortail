using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Network.Services
{
    public class UnityServicesInit : MonoBehaviour
    {
        private void Awake()
        {
            _ = SetupServices();
        }

        
        private static async Task SetupServices()
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                string _profile = "Player_" + System.Guid.NewGuid().ToString("N").Substring(0, 15);
                AuthenticationService.Instance.SwitchProfile(_profile);
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Signed in anonymously with Player ID: " + AuthenticationService.Instance.PlayerId);
            }
        }
    }
}
