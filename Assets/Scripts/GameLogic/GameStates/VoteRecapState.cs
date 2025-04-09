using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "VoteRecapState", menuName = "GameStates/VoteRecapState")]
    public class VoteRecapState : GameState
    {
        public float recapDuration;
        
        private float recapTimer;
        
        [SerializeField] private GameObject cardPrefab;

        [HideInInspector] public Transform spawnedCard;
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            recapTimer = recapDuration;
            
            if (VoteState.lastVotedPlayer == VoteState.SKIP_VOTE_ID)
            {
                gameManager.NextGameState();
            }
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            if (VoteState.lastVotedPlayer == VoteState.SKIP_VOTE_ID)
            {
                
            }
            else
            {
                DoCardChainingAnimation();
            }
        }

        private async UniTask DoCardChainingAnimation()
        {
            await BoardManager.instance.HideAllCards();
            
            var _cardInfo = BoardManager.instance.AddNewCard();
            spawnedCard = _cardInfo.transform;
                
            Character _votedCharacter = gameManager.characters.First(character => character.ownerClientId == VoteState.lastVotedPlayer);
            _cardInfo.SetInfo(_votedCharacter);
            _cardInfo.ShowPseudoOnly();
            _cardInfo.SetChainedOverlay(false, true);

            spawnedCard.eulerAngles = new Vector3(0, 0, 180);
            await _cardInfo.ShowFrontSide();
            
            await UniTask.Delay(TimeSpan.FromSeconds(1));
            
            
            spawnedCard.DOMoveY(-5, 1f).SetEase(Ease.OutQuint);
            spawnedCard.DOLocalRotate(new Vector3(0, 0, -180), 1f).SetEase(Ease.OutSine).onComplete = () =>
            {
                _cardInfo.ShowPseudoWithRole();
                    
                spawnedCard.DOLocalRotate(new Vector3(0, 0, -360), 1f).SetEase(Ease.InSine);
                spawnedCard.DOLocalMoveY(0, 1f).SetEase(Ease.InQuint).onComplete = () =>
                {
                    spawnedCard.DOPunchScale(new Vector3(0.25f, 0f, 0.1f), 0.35f).onComplete = () =>
                    {
                        _cardInfo.SetChainedOverlay(true);
                    };
                };
            };
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();

            if (!spawnedCard)
            {
                return;
            }

            BoardManager.instance.ShowAllPlayerCards();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
            
            recapTimer -= Time.deltaTime;
            if (recapTimer > 0)
            {
                return;
            }
            
            gameManager.NextGameState();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}