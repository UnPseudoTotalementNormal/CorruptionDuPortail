using DG.Tweening;
using GameLogic;
using TMPro;
using UnityEngine;

namespace UI
{
    public class AwakeningStateUI : StateUI
    {
        public TMP_Text awakeningHelpText;

        protected override void OnStateStart()
        {
            base.OnStateStart();
            gameManager.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            gameManager.GetLocalCharacter(false).onCharacterAwakened += ShowAwakeningHelpText;
        }

        private void ShowAwakeningHelpText()
        {
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