using System;
using AudioSystem;
using Board;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using FMODUnity;
using TransformComposition;
using UnityEngine;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "ChainingState", menuName = "GameStates/ChainingState")]
    public class ChainingState : GameState
    {
        private Transform spawnedCard;
        public EventReference chainingAnnouncementSound;
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            foreach (var _chainingPlayerId in chainingManager.chainingPlayers)
            {
                chainingManager.ChainCharacterRpc(_chainingPlayerId);
            }
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            HandleChainingStateClientAsync().Forget(); // UniTaskVoid: an exception reaches the log (a discarded Task swallowed it)
        }

        private async UniTaskVoid HandleChainingStateClientAsync()
        {
            // NET-06: snapshot the ids now. The host clears the replicated list when ITS animation ends; enumerating the
            // live NetworkList across awaits truncated the loop on any slower client (chain animations skipped).
            var _chainingCharactersId = new System.Collections.Generic.List<ulong>();
            foreach (var _id in chainingManager.chainingPlayers)
            {
                _chainingCharactersId.Add(_id);
            }
            foreach (var _chainingCharacterId in _chainingCharactersId)
            {
                var _chainingCharacter = CharacterQuery.GetCharacter(_chainingCharacterId);
                // [LEAVE] Phase 2 — the id may reference a character that was already removed (a leaver), so
                // GetCharacter returns null. Skip it: the animation loop must never NRE on an absent character.
                if (_chainingCharacter == null)
                {
                    Debug.Log($"[LEAVE] ChainingState: character {_chainingCharacterId} no longer present — skipping its chaining animation.");
                    continue;
                }

                // The board (scene) can go away mid-animation (this client dropped or left): the animation is tied to
                // the board's and the card's lifetime and simply stops, instead of touching destroyed cards.
                try
                {
                    await DoCardChainingAnimation(_chainingCharacter);
                    await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: boardManager.GetCancellationTokenOnDestroy());
                }
                catch (OperationCanceledException)
                {
                    if (!boardManager)
                    {
                        return; // the scene went away: nothing left to animate or advance
                    }
                    // Only that card was destroyed (board rebuilt): go on, the server must still advance the loop.
                }
            }

            if (gameManager.IsServer)
            {
                chainingManager.chainingPlayers.Clear();
                // Left meanwhile (a leaver's victory re-check jumped to GameEndingState): advancing from there wrapped
                // the loop back to the lobby. Only a live chaining advances. Checked by TYPE, not IsStateActive(): the
                // loop holds two ChainingState instances and DoStateMethodRpc runs this flow on the first one of the
                // type, whichever of them is current.
                if (!(gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is ChainingState))
                {
                    Debug.Log("[LEAVE] ChainingState: no longer the current state when its animation ended — not advancing.");
                    return;
                }
                Debug.Log("ChainingState completed on server, moving to next state.");
                Loop.NextGameState();
            }
        }

        private async UniTask DoCardChainingAnimation(Character _chainingCharacter)
        {
            var _boardAlive = boardManager.GetCancellationTokenOnDestroy();
            await UniTask.Delay(TimeSpan.FromSeconds(0.25f), cancellationToken: _boardAlive);
            
            GameAudioManager.instance.PlayOneShot(chainingAnnouncementSound.GetPath());
            await boardManager.HideAllCards().AttachExternalCancellation(_boardAlive);
            
            Card _cardInfo = boardManager.AddNewCard();
            var _alive = _cardInfo.GetCancellationTokenOnDestroy();
            _cardInfo.SetCanShowBackInfo(false);
            spawnedCard = _cardInfo.transform;
            
            _cardInfo.SetInfo(_chainingCharacter);
            _cardInfo.ShowPseudoOnly();
            _cardInfo.SetChainedOverlay(false, true);

            await _cardInfo.ShowBackSide(true).AttachExternalCancellation(_alive);
            await _cardInfo.ShowFrontSide().AttachExternalCancellation(_alive);
            
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _alive);
            
            var chainingLayer = _cardInfo.visualComponents.compositor.GetLayer("ChainingFlip");
            Vector3 rot = Vector3.zero;
            
            Vector3 targetWorldPos = spawnedCard.position;
            targetWorldPos.y = -5f;
            Vector3 targetLocalPos = spawnedCard.parent.InverseTransformPoint(targetWorldPos);
            float targetLocalY = targetLocalPos.y - spawnedCard.localPosition.y;
            
            chainingLayer.DOLocalMoveY(targetLocalY, 1f).SetEase(Ease.OutQuint).SetLink(spawnedCard.gameObject);
            var _tween1 = DOTween.To(() => rot, x => { rot = x; chainingLayer.localEulerAngles = rot; }, new Vector3(0, 0, -180), 1f).SetEase(Ease.OutSine)
                .SetLink(spawnedCard.gameObject);
            await UniTask.WaitUntil(() => !_tween1.IsActive(), cancellationToken: _alive);
            
            _ = _cardInfo.ShowPseudoWithRevealedInfo(false, false);
            
            var _tween2 = DOTween.To(() => rot, x => { rot = x; chainingLayer.localEulerAngles = rot; }, new Vector3(0, 0, -360), 1f).SetEase(Ease.InSine)
                .SetLink(spawnedCard.gameObject);
            var _tweenMove = chainingLayer.DOLocalMoveY(0, 1f).SetEase(Ease.InQuint).SetLink(spawnedCard.gameObject);
            await UniTask.WaitUntil(() => !_tween2.IsActive(), cancellationToken: _alive);
            
            var _tween3 = spawnedCard.DOPunchScale(new Vector3(0.25f, 0f, 0.1f), 0.35f).SetLink(spawnedCard.gameObject);
            await UniTask.WaitUntil(() => !_tween3.IsActive(), cancellationToken: _alive);
            _cardInfo.SetChainedOverlay(true);
            GameAudioManager.instance.PlayOneShot(_cardInfo.roleInfo.onChainingSound.GetPath());
        }
    }
}