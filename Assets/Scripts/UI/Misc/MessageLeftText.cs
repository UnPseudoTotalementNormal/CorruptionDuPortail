using System.Collections.Generic;
using Characters;
using GameLogic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

namespace UI
{
    public class MessageLeftText : MonoBehaviour
    {
        public const int INFINITE_MESSAGE_THRESHOLD = 1000;
        
        public TMP_Text messageLeftText;
        // Story 7.4 lane A: scene-wired, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;
        private Character localCharacter;
        private bool isSubscribed = false;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "MessageLeftText.characterManager is not wired — wire it in GameScene (the composition root).");
            localCharacter = characterManager.GetLocalCharacter(false);
            if (localCharacter != null)
            {
                localCharacter.messageLeft.OnValueChanged += OnMessageLeftChanged;
                isSubscribed = true;
                UpdateMessageLeftText(localCharacter.messageLeft.Value, localCharacter.messageLeft.Value);
            }
        }

        private void OnDestroy()
        {
            if (localCharacter != null && isSubscribed)
            {
                localCharacter.messageLeft.OnValueChanged -= OnMessageLeftChanged;
                isSubscribed = false;
            }
        }

        private void OnMessageLeftChanged(int _previous, int _current)
        {
            UpdateMessageLeftText(_current, _current);
        }

        private void UpdateMessageLeftText(int _previous, int _current)
        {
            if (_current > INFINITE_MESSAGE_THRESHOLD)
            {
                messageLeftText.text = "restant: ∞";
                return;
            }
            messageLeftText.text = $"restant: {_current}";
        }
    }
}