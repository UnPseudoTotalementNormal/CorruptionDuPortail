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
        
        private void Start()
        {
            
            GameManager.instance.characterManager.onCharactersListUpdated += UpdateMessageLeftText;
        }

        private void UpdateMessageLeftText(List<Character> _obj)
        {
            int _messageLeft = GameManager.instance.characterManager.GetLocalCharacter(false).messageLeft;
            messageLeftText.text = $"restant: {_messageLeft.ToString()}";
        }
    }
}