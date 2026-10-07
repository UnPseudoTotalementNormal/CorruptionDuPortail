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
                // Tied to the board's lifetime: a client that drops or leaves mid-recap unloads the scene while this
                // awaits; the recap then stops (OperationCanceledException, silent) instead of touching destroyed cards.
                var _boardAlive = boardManager.GetCancellationTokenOnDestroy();
                await boardManager.ShowAllPlayerCards().AttachExternalCancellation(_boardAlive);

                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _boardAlive);

                await ShowVoteResult(_boardAlive);

                // The recap may have been left meanwhile (a leaver's grace expired and the victory re-check jumped to
                // GameEndingState): advancing from there wrapped the loop back to the lobby. Only a live recap advances.
                // Checked by TYPE (DoStateMethodRpc runs this flow on the first instance of the type).
                if (gameManager.IsServer && !(gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is VoteRecapState))
                {
                    Debug.Log("[LEAVE] VoteRecapState: no longer the current state when its recap ended — not advancing.");
                }
                else if (VoteState.mostVotedPlayer == VoteState.SKIP_VOTE_ID)
                {
                    if (gameManager.IsServer)
                    {
                        Loop.NextGameState();
                    }
                }
                else
                {

                    Character _chainingCharacter = CharacterQuery.GetCharacter(VoteState.mostVotedPlayer);
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
            foreach (var _card in boardManager.visibleCards)
            {
                if (!_card)
                {
                    continue;
                }
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

        private async UniTask ShowVoteResult(System.Threading.CancellationToken _boardAlive)
        {
            foreach (var _card in boardManager.visibleCards)
            {
                if (!_card)
                {
                    continue;
                }
                var _voteCanvas = _card.GetComponentInChildren<VoteCanvas>();
                if (_voteCanvas)
                {
                    _voteCanvas.ShowVoteCount(true);
                }
            }

            SetVoteCanvasVisibility(true);
            onShowVoteRecap?.Invoke();
            await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: _boardAlive);
            SetVoteCanvasVisibility(false);
            onHideVoteRecap?.Invoke();
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _boardAlive);
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();

            if (!spawnedCard)
            {
                return;
            }

            _ = boardManager.ShowAllPlayerCards();
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