#region

using GameLogic;
using UI;
using UnityEngine;

#endregion

namespace Board.UI
{
    public class SkipButton : MonoBehaviour
    {
        private CustomButton customButton;
        private void Start()
        {
            customButton = GetComponent<CustomButton>();
            customButton.onButtonClicked += OnSkipButtonClicked;
        }

        private void OnSkipButtonClicked()
        {
            GameManager.instance.characterManager.GetLocalCharacter(false).SleepCharacterServerRpc();
        }
    }
}
