using DG.Tweening;
using GameLogic;
using TMPro;
using UnityEngine;

namespace UI
{
    public class AwakeningStateUI : StateUI
    {
        public TMP_Text awakeningHelpText;

        public override void SetupStateUI(GameManager gameManager, GameState gameState)
        {
            base.SetupStateUI(gameManager, gameState);
            Loop.onGameStarted += OnGameStarted;
        }

        protected override void OnStateStart()
        {
            base.OnStateStart();
            awakeningHelpText.alpha = 0;
        }

        private void OnGameStarted()
        {
            characterManager.GetLocalCharacter(false).isAwakened.OnValueChanged += OnAwakeningChanged;
        }

        private void OnAwakeningChanged(bool _previousValue, bool _newValue)
        {
            if (_newValue)
            {
                ShowAwakeningHelpText();
            }
        }

        private void ShowAwakeningHelpText()
        {
            if (!awakeningHelpText)
            {
                return;
            }
            
            awakeningHelpText.DOKill(true);
            awakeningHelpText.text = "Vous vous éveillez...";
            awakeningHelpText.alpha = 0;
            awakeningHelpText.transform.localScale = Vector3.zero;
            awakeningHelpText.transform.DOScale(1, 0.75f).SetEase(Ease.OutBack);
            awakeningHelpText.DOFade(1, 0.75f).onComplete += () =>
            {
                awakeningHelpText.DOFade(0, 0.5f).SetDelay(2f);
            };
        }
    }
}