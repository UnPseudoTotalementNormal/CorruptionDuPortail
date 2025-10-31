#region

using System;
using System.Linq;
using AudioSystem;
using Board.UI.VoteCanvas;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using FMODUnity;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "VoteRecapState", menuName = "GameStates/VoteRecapState")]
    public class VoteRecapState : GameState
    {
        public float recapDuration; //useless rn
        
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
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override async void OnStartStateClient()
        {
            base.OnStartStateClient();
            await BoardManager.instance.ShowAllPlayerCards();

            await UniTask.Delay(TimeSpan.FromSeconds(1));
            
            await ShowVoteResult();
            
            if (VoteState.mostVotedPlayer == VoteState.SKIP_VOTE_ID)
            {
                if (gameManager.IsServer)
                {
                    gameManager.NextGameState();
                }
            }
            else
            {
                
                Character _chainingCharacter = gameManager.characterManager.GetCharacter(VoteState.mostVotedPlayer);
                if (gameManager.IsServer)
                {
                    gameManager.NextGameState();
                }
            }
        }

        private void SetVoteCanvasVisibility(bool visible)
        {
            foreach (var _card in BoardManager.instance.visibleCards)
            {
                var _voteCanvas = _card.GetComponentInChildren<VoteCanvas>();
                if (_voteCanvas)
                {
                    if (visible)
                    {
                        _voteCanvas.ShowCanvas();
                    }
                    else
                    {
                        _voteCanvas.HideCanvas();
                    }
                }
            }
        }

        private async UniTask ShowVoteResult()
        {
            foreach (var _card in BoardManager.instance.visibleCards)
            {
                var _voteCanvas = _card.GetComponentInChildren<VoteCanvas>();
                if (_voteCanvas)
                {
                    _voteCanvas.ShowVoteCount(true);
                }
            }

            SetVoteCanvasVisibility(true);
            await UniTask.Delay(TimeSpan.FromSeconds(5));
            SetVoteCanvasVisibility(false);
            await UniTask.Delay(TimeSpan.FromSeconds(1));
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();

            if (!spawnedCard)
            {
                return;
            }

            _ = BoardManager.instance.ShowAllPlayerCards();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}