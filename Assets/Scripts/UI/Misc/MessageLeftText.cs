using System.Collections.Generic;
using Characters;
using GameLogic;
using TMPro;
using UnityEngine;

namespace UI
{
    public class MessageLeftText : MonoBehaviour
    {
        public TMP_Text messageLeftText;
        private Character localCharacter;
        private bool isSubscribed = false;

        private void Start()
        {
            localCharacter = GameManager.instance.characterManager.GetLocalCharacter(false);
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
            messageLeftText.text = $"restant: {_current}";
        }
    }
}