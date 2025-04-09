using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
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
                spawnedCard = Instantiate(cardPrefab, BoardManager.instance.transform).transform;
                spawnedCard.localPosition = new Vector3(0, 0, 20);
                
                Character _votedCharacter = gameManager.characters.First(character => character.ownerClientId == VoteState.lastVotedPlayer);
                var _cardInfo = spawnedCard.GetComponent<Card>();
                _cardInfo.SetInfo(_votedCharacter);
                _cardInfo.ShowPseudoOnly();
                
                spawnedCard.DOLocalMove(Vector3.zero, 2).SetEase(Ease.OutQuint).onComplete = () =>
                {
                    spawnedCard.DOLocalMoveY(10, 1f).SetEase(Ease.OutQuint);
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
                };
            }
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();

            if (!spawnedCard)
            {
                return;
            }
            
            spawnedCard.DOLocalMove(new Vector3(0, 0, 20), 1f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                Destroy(spawnedCard.gameObject);
            };
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