using UI;
using UnityEngine;

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
            GameManager.instance.SleepCharacterRpc(GameManager.instance.GetLocalCharacter(false).ownerClientId);
        }
    }
}
