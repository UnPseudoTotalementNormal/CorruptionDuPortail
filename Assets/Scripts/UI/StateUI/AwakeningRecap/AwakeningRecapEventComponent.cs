using System;
using Characters;
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

        // Story 12.2: the character-query slice, pushed by the AwakeningRecapStateUI host (a StateUI that
        // carries the injected ICharacterQuery) when it spawns this event component. Recap event components are
        // deep prefab leaves with no injected base of their own, so the host hands the slice down.
        public ICharacterQuery CharacterQuery { get; set; }

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