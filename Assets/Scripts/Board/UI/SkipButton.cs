#region

using Characters;
using GameLogic;
using UI;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace Board.UI
{
    public class SkipButton : MonoBehaviour
    {
        private CustomButton customButton;

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "SkipButton.characterManager is not wired — wire it in GameScene (the composition root).");
            customButton = GetComponent<CustomButton>();
            customButton.onButtonClicked += OnSkipButtonClicked;
        }

        private void OnSkipButtonClicked()
        {
            CharacterQuery.GetLocalCharacter(false).SleepCharacterServerRpc();
        }
    }
}
