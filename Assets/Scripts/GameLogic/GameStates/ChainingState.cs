using System;
using System.Threading.Tasks;
using AudioSystem;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using FMODUnity;
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
            foreach (var _chainingPlayerId in gameManager.chainingManager.chainingPlayers)
            {
                gameManager.chainingManager.ChainCharacterRpc(_chainingPlayerId);
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

            _ = HandleChainingStateClientAsync();
        }

        private async Task HandleChainingStateClientAsync()
        {
            var _chainingCharactersId = gameManager.chainingManager.chainingPlayers;
            foreach (var _chainingCharacterId in _chainingCharactersId)
            {
                var _chainingCharacter = gameManager.characterManager.GetCharacter(_chainingCharacterId);
                await DoCardChainingAnimation(_chainingCharacter);
                await UniTask.Delay(TimeSpan.FromSeconds(1f));
            }

            if (gameManager.IsServer)
            {
                gameManager.chainingManager.chainingPlayers.Clear();
                Debug.Log("ChainingState completed on server, moving to next state.");
                gameManager.NextGameState();
            }
        }

        private async UniTask DoCardChainingAnimation(Character _chainingCharacter)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.25f));
            
            GameAudioManager.instance.PlayOneShot(chainingAnnouncementSound.GetPath());
            await BoardManager.instance.HideAllCards();
            
            var _cardInfo = BoardManager.instance.AddNewCard();
            spawnedCard = _cardInfo.transform;
            var _spawnedCardPivot = _cardInfo.cardPivotTransform;
            
            _cardInfo.SetInfo(_chainingCharacter);
            _cardInfo.ShowPseudoOnly();
            _cardInfo.SetChainedOverlay(false, true);

            await _cardInfo.ShowBackSide(true);
            await _cardInfo.ShowFrontSide();
            
            await UniTask.Delay(TimeSpan.FromSeconds(1));
            
            spawnedCard.DOMoveY(-5, 1f).SetEase(Ease.OutQuint);
            var _tween1 = _spawnedCardPivot.DOLocalRotate(new Vector3(0, 0, -180), 1f).SetEase(Ease.OutSine);
            await UniTask.WaitUntil(() => !_tween1.IsActive());
            
            _ = _cardInfo.ShowPseudoWithRevealedInfo();
            
            _spawnedCardPivot.DOLocalRotate(new Vector3(0, 0, -360), 1f).SetEase(Ease.InSine);
            var _tween2 = spawnedCard.DOLocalMoveY(0, 1f).SetEase(Ease.InQuint);
            await UniTask.WaitUntil(() => !_tween2.IsActive());
            
            var _tween3 = spawnedCard.DOPunchScale(new Vector3(0.25f, 0f, 0.1f), 0.35f);
            await UniTask.WaitUntil(() => !_tween3.IsActive());
            _cardInfo.SetChainedOverlay(true);
            GameAudioManager.instance.PlayOneShot(_cardInfo.roleInfo.onChainingSound.GetPath());
        }
    }
}