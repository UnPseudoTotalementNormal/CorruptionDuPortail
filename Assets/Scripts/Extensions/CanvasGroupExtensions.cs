using DG.Tweening;
using UnityEngine;

namespace Extensions
{
    public static class CanvasGroupExtensions
    {
        public static void DoShowGroup(this CanvasGroup _canvasGroup, float _duration = 0.5f,
            bool _interactable = true, bool _blocksRaycasts = true)

        {
            _canvasGroup.DOKill(true);
            _canvasGroup.interactable = _interactable;
            _canvasGroup.blocksRaycasts = _blocksRaycasts;
            _canvasGroup.DOFade(1, _duration);
        }
        
        public static void DoHideGroup(this CanvasGroup _canvasGroup, float _duration = 0.5f,
            bool _interactable = false, bool _blocksRaycasts = false)
        {
            _canvasGroup.DOKill(true);
            _canvasGroup.interactable = _interactable;
            _canvasGroup.blocksRaycasts = _blocksRaycasts;
            _canvasGroup.DOFade(0, _duration);
        }
    }
}