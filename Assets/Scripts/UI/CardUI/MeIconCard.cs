using System;
using Board;
using Characters;
using Extensions;
using Unity.Netcode;
using UnityEngine;

namespace UI.CardUI
{
    public class MeIconCard : MonoBehaviour
    {
        [SerializeField] private Card card;
        [SerializeField] private CanvasGroup canvasGroup;

        [SerializeField] private float shownAlpha = 0.95f;
        [SerializeField] private float fadeDuration = 0.5f;
        [SerializeField] private float notMovingDurationNeeded = 0.5f;
        
        private float notMovingTimer;
        private bool isMoving;

        private bool isShown;
        
        private void Awake()
        {
            card.onCardSetInfo += OnCardSetInfo;
        }

        private void OnCardSetInfo(Character _character)
        {
            isShown = true;
            canvasGroup.alpha = 0;
            TryHide();
            
            if (_character.ownerClientId.Value != NetworkManager.Singleton.LocalClientId)
            {
                isMoving = true;
                return;
            }
            
            card.onCardStartMoving += () => isMoving = true;
            card.onCardStopMoving += () => isMoving = false;
        }

        private void Update()
        {
            if (isMoving)
            {
                notMovingTimer = notMovingDurationNeeded;
                TryHide();
                return;
            }

            notMovingTimer -= Time.deltaTime;
            if (notMovingTimer > 0)
            {
                TryHide();
                return;
            }

            if (card.isPointerOver)
            {
                TryHide();
                return;
            }
            
            TryShow();
        }

        private void TryHide()
        {
            if (!isShown)
            {
                return;
            }
            isShown = false;
            canvasGroup.DoHideGroup(fadeDuration);
        }

        private void TryShow()
        {
            if (isShown)
            {
                return;
            }
            isShown = true;
            canvasGroup.DoShowGroup(fadeDuration, false, false, shownAlpha);
        }
    }
}