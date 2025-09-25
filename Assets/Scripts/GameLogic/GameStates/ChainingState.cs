using System;
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
        
        public override async void OnStartStateClient()
        {
            base.OnStartStateClient();

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
            _spawnedCardPivot.DOLocalRotate(new Vector3(0, 0, -180), 1f).SetEase(Ease.OutSine).onComplete = () =>
            {
                _ = _cardInfo.ShowPseudoWithRevealedInfo();
                    
                _spawnedCardPivot.DOLocalRotate(new Vector3(0, 0, -360), 1f).SetEase(Ease.InSine);
                spawnedCard.DOLocalMoveY(0, 1f).SetEase(Ease.InQuint).onComplete = () =>
                {
                    spawnedCard.DOPunchScale(new Vector3(0.25f, 0f, 0.1f), 0.35f).onComplete = () =>
                    {
                        _cardInfo.SetChainedOverlay(true);
                    };
                };
            };
            await UniTask.Delay(TimeSpan.FromSeconds(3.35f));
        }
    }
}