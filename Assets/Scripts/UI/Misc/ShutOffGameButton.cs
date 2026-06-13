#region

using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

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

            // Story 12.3: prefab-only leaf (lives on GameEndigStateUI.prefab) — lane A impossible, so it resolves
            // through the sanctioned CompositionRoot.For(Singleton) instead of the GameManager God-Object façade.
            CompositionRoot.For(NetworkManager.Singleton).GameManager.ShutOffGameRpc();
        }

    }
}
