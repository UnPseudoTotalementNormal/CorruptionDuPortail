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

            // Story 12.2: prefab-only leaf (lives on GameEndigStateUI.prefab, not GameScene) → lane A
            // physically impossible (prefab can't ref a scene object); recorded OPT-OUT, GameManager.instance
            // façade death deferred to 12.3 alongside the sibling prefab-only leaves (§4g).
            GameManager.instance.ShutOffGameRpc();
        }

    }
}
