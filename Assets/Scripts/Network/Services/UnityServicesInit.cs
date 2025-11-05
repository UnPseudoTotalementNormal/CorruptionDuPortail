using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Network.Services
{
    public class UnityServicesInit : MonoBehaviour
    {
        private void Start()
        {
            _ = SetupServices();
        }

        private static async Task SetupServices()
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
    }
}
