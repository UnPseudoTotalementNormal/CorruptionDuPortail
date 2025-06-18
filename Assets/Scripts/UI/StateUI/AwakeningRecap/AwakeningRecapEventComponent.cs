using System;
using DG.Tweening;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;

namespace UI.Components
{
    public class AwakeningRecapEventComponent : MonoBehaviour
    {
        public CanvasGroup eventCanvasGroup;
        public float baseDuration = 5f;

        private void Awake()
        {
            Assert.IsNotNull(eventCanvasGroup, "AwakeningRecapEventComponent requires a CanvasGroup component to be set.");
        }

        public virtual float EvaluateDuration()
        {
            return baseDuration;
        }
        
        public virtual void SetupEvent(AwakeningRecapEvent _recapEvent)
        {
            eventCanvasGroup.alpha = 0;
            HideEvent();
        }

        public virtual void ShowEvent()
        {
            eventCanvasGroup.DOFade(1, 1.25f);
        }

        public virtual void HideEvent()
        {
            eventCanvasGroup.DOFade(0, 1.25f);
        }
    }
}