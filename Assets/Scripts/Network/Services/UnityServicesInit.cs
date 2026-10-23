using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using Diagnostics.Logs;

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
            string _stage = "init";
            try
            {
                await UnityServices.InitializeAsync();
                GameLog.Info("net", "services.ready");
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    _stage = "sign-in";
                    string _profile = "Player_" + System.Guid.NewGuid().ToString("N").Substring(0, 15);
                    AuthenticationService.Instance.SwitchProfile(_profile);
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log("Signed in anonymously with Player ID: " + AuthenticationService.Instance.PlayerId);
                    GameLog.Info("net", "auth.signedin", "anonymous", true);
                }
            }
            catch (System.Exception e)
            {
                // Fire-and-forget from Awake: without this the failure (no network, UGS down) was invisible.
                GameLog.Warn("net", "services.failed", "stage", _stage, "error", e.GetType().Name, "message", e.Message);
                throw; // unchanged behaviour
            }
        }
    }
}
