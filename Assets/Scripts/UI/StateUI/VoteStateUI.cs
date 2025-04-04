using System;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;

namespace UI
{
    public class VoteStateUI : StateUI
    {
        [SerializeField] private TMP_Text timerText;
        
        private void Update()
        {
            UpdateTimerText(((VoteState)owningGameState).voteTimer);
        }
        
        private void UpdateTimerText(float timeLeft)
        {
            timerText.text = timeLeft.ToString("0");
        }
    }
}