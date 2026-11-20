using System;
using Board;
using Characters;
using Extensions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace UI.CardUI
{
    /// <summary>
    /// The mark over the local player's own card (T9: Wouh-style paw print, maquettes placeholders v1 U2). Hidden
    /// while the card really moves (deal, shuffle, re-layout, hover lift) or is hovered, shown once it has been still
    /// for <see cref="notMovingDurationNeeded"/>. It used to flicker: the card's own "moving" events fire on any
    /// sub-pixel drift (0.01 u/s) and on the 359°→0° wrap of an Euler angle, so the mark hid and re-showed all the
    /// time. Movement is now measured here, with real thresholds and a still-time that a jitter cannot reset.
    /// </summary>
    public class MeIconCard : MonoBehaviour
    {
        [SerializeField] private Card card;
        [SerializeField] private CanvasGroup canvasGroup;

        [SerializeField] private float shownAlpha = 0.95f;
        [SerializeField] private float fadeDuration = 0.5f;
        [SerializeField] private float notMovingDurationNeeded = 0.5f;
        // A card moving slower than this (world units / s, degrees / s) is still: idle drift, float noise.
        [SerializeField] private float moveSpeedThreshold = 0.4f;
        [SerializeField] private float turnSpeedThreshold = 20f;
        [SerializeField] private float unhoverDelay = 0.25f;

        private bool isShown;
        private bool isMe;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private float lastMoveTime = float.NegativeInfinity;
        private float lastHoverTime = float.NegativeInfinity;

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

            bool _wasMe = isMe;
            isMe = card.characterInfo.ownerClientId.Value == card.CharacterQuery.GetLocalClientId();
            if (isMe != _wasMe)
            {
                Diagnostics.Logs.GameLog.Info("ui", "ui.me-mark.owner", "card", card.characterInfo.ownerClientId.Value, "isMe", isMe);
            }

            if (!isMe)
            {
                TryHide();
                return;
            }

            // A fresh identity waits for the card to settle before showing, like after a move.
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            lastMoveTime = Time.time;
        }

        private void OnCardSetInfo(Character _character)
        {
            UpdateIdentityVisibility();
        }

        private void Update()
        {
            if (!isMe) return;

            Transform _t = transform; // the mark itself: it moves with whatever moves the card (root or visual)
            float _dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float _speed = (_t.position - lastPosition).magnitude / _dt;
            float _turn = Quaternion.Angle(_t.rotation, lastRotation) / _dt;
            lastPosition = _t.position;
            lastRotation = _t.rotation;
            if (_speed > moveSpeedThreshold || _turn > turnSpeedThreshold) lastMoveTime = Time.time;
            if (card.isPointerOver) lastHoverTime = Time.time;

            bool _settled = Time.time - lastMoveTime >= notMovingDurationNeeded;
            bool _hovered = Time.time - lastHoverTime < unhoverDelay;
            if (_settled && !_hovered) TryShow();
            else TryHide();
        }

        private void OnDestroy()
        {
            if (card != null && card.CharacterQuery != null)
            {
                card.CharacterQuery.onLocalIdentityChanged -= UpdateIdentityVisibility;
            }
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
