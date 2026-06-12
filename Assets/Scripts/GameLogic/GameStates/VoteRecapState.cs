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
        
        public event Action onShowVoteRecap;
        public event Action onHideVoteRecap;
        
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
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            OnStartStateClientAsync().Forget();
        }

        private async UniTaskVoid OnStartStateClientAsync()
        {
            try
            {
                await BoardManager.instance.ShowAllPlayerCards();

                await UniTask.Delay(TimeSpan.FromSeconds(1));

                await ShowVoteResult();

                if (VoteState.mostVotedPlayer == VoteState.SKIP_VOTE_ID)
                {
                    if (gameManager.IsServer)
                    {
                        Loop.NextGameState();
                    }
                }
                else
                {

                    Character _chainingCharacter = characterManager.GetCharacter(VoteState.mostVotedPlayer);
                    if (gameManager.IsServer)
                    {
                        Loop.NextGameState();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (changement d'état / destruction) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Erreur dans VoteRecapState.OnStartStateClientAsync: {e}");
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
            onShowVoteRecap?.Invoke();
            await UniTask.Delay(TimeSpan.FromSeconds(5));
            SetVoteCanvasVisibility(false);
            onHideVoteRecap?.Invoke();
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