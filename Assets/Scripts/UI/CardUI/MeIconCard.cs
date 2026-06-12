using System;
using Board;
using Characters;
using Extensions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

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
        private bool isMe;
        
        private void Awake()
        {
            if (canvasGroup != null) canvasGroup.alpha = 0;
            isShown = false;
            isMe = false;
            card.onCardSetInfo += OnCardSetInfo;
        }

        private void Start()
        {
            // Story 12.2: read the character-query slice from the parent Card (pushed by BoardManager.AddNewCard)
            // instead of the CharacterManager façade — same precedent as CardCorruptedText reading card.GameInfoRevealer.
            // Assert localises a future regression of the AddNewCard→Card.Initialize push: the slice must be set
            // before this child's Start (the old global static read had no such ordering dependency).
            Assert.IsNotNull(card.CharacterQuery, "MeIconCard: card.CharacterQuery is null — Card.Initialize (BoardManager.AddNewCard) must run before the card child's Start.");
            card.CharacterQuery.onLocalIdentityChanged += UpdateIdentityVisibility;
            UpdateIdentityVisibility();
        }

        private void UpdateIdentityVisibility()
        {
            if (card.characterInfo == null) return;
            
            isMe = card.characterInfo.ownerClientId.Value == card.CharacterQuery.GetLocalClientId();
            
            if (!isMe)
            {
                isMoving = true; // Force hide
                TryHide();
                UnsubscribeFromMovement();
            }
            else
            {
                isMoving = false;
                notMovingTimer = 0;
                TryShow();
                SubscribeToMovement();
            }
        }

        private void SubscribeToMovement()
        {
            UnsubscribeFromMovement();
            card.onCardStartMoving += OnCardStartMoving;
            card.onCardStopMoving += OnCardStopMoving;
        }

        private void UnsubscribeFromMovement()
        {
            card.onCardStartMoving -= OnCardStartMoving;
            card.onCardStopMoving -= OnCardStopMoving;
        }

        private void OnCardStartMoving() => isMoving = true;
        private void OnCardStopMoving() => isMoving = false;

        private void OnCardSetInfo(Character _character)
        {
            UpdateIdentityVisibility();
        }

        private void Update()
        {
            if (!isMe) return;
            
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

        private void OnDestroy()
        {
            if (card != null && card.CharacterQuery != null)
            {
                card.CharacterQuery.onLocalIdentityChanged -= UpdateIdentityVisibility;
            }
            UnsubscribeFromMovement();
            if (card != null)
            {
                card.onCardSetInfo -= OnCardSetInfo;
            }
        }

        private void TryHide()
        {
            if (!isShown || canvasGroup == null)
            {
                return;
            }
            isShown = false;
            canvasGroup.DoHideGroup(fadeDuration);
        }

        private void TryShow()
        {
            if (isShown || canvasGroup == null)
            {
                return;
            }
            isShown = true;
            canvasGroup.DoShowGroup(fadeDuration, false, false, shownAlpha);
        }
    }
}