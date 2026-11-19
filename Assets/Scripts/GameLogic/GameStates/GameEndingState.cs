#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using Network;
using UnityEngine;
using Diagnostics.Logs;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "GameEndingState", menuName = "GameStates/GameEndingState")]
    public class GameEndingState : GameState
    {
        private Dictionary<WinningTeam, ulong[]> winningTeams = new();

        /// <summary>Winners this peer received (read-only; autoplay state export).</summary>
        public IReadOnlyDictionary<WinningTeam, ulong[]> WinningTeams => winningTeams;

        public void SetWinnersServer(Dictionary<WinningTeam, HashSet<ulong>> _winningTeams)
        {
            var _winnersArray = _winningTeams
                .Select(kvp => new KeyValuePair<WinningTeam, ulong[]>(kvp.Key, kvp.Value.ToArray()))
                .ToArray();
            GameLog.Guarded(() => GameLog.Snapshot("game.end", "winners",
                _winnersArray.Select(_kvp => $"{_kvp.Key}: {string.Join(",", _kvp.Value)}").ToList()));
        
            gameManager.DoStateMethodRpc(typeof(GameEndingState).FullName, nameof(SetWinnersClientRpc),
                new NetworkSerializableObject[]
                {
                    new(_winnersArray)
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
        }
        
        public void SetWinnersClientRpc(KeyValuePair<WinningTeam, ulong[]>[] _winnersArray)
        {
            winningTeams = _winnersArray.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public async UniTaskVoid GameEndingAnimation()
        {
            // T9 maquettes menus v1 E3: each player reads their own result first (EndGameAnnounce), then every card
            // turns over at its place, the winners gold and the losers dimmed (before: only the winners' cards).
            float _start = Time.realtimeSinceStartup;
            var _winners = new HashSet<ulong>(winningTeams.Values.SelectMany(_ids => _ids));
            ulong _local = CharacterQuery.GetLocalClientId();
            var _announce = stateUI != null ? stateUI.GetComponentInChildren<UI.EndGame.EndGameAnnounce>(true) : null;
            if (_announce != null)
            {
                _announce.Play(winningTeams.Keys.ToList(), _winners.Contains(_local));
            }
            else
            {
                GameLog.Warn("ui", "end.announce.missing", "reason", "no EndGameAnnounce on the state UI");
            }

            // Tied to the board's lifetime: "Terminer la partie" during the animation unloads the scene (B10 / N4 family).
            var _boardAlive = boardManager.GetCancellationTokenOnDestroy();
            if (await boardManager.HideAllCards().AttachExternalCancellation(_boardAlive).SuppressCancellationThrow())
            {
                return;
            }

            var _characters = CharacterQuery.GetCharacters(false).Where(_c => _c != null && !_c.isFake).ToList();
            foreach (var _character in _characters)
            {
                var _newCard = boardManager.AddNewCard();
                _newCard.SetInfo(_character);
                _ = _newCard.ShowPseudoWithRevealedInfo();
                _ = _newCard.ShowBackSide(true);
            }

            float _wait = UI.EndGame.EndGameAnnounce.CardsAt - (Time.realtimeSinceStartup - _start);
            if (_wait > 0f && await UniTask.Delay(TimeSpan.FromSeconds(_wait), cancellationToken: _boardAlive).SuppressCancellationThrow())
            {
                return;
            }

            boardManager.PlaceAllCardsToPosition();
            GameLog.Info("game", "end.cards", "count", boardManager.visibleCards.Count, "winners", _winners.Count);
            foreach (var _card in boardManager.visibleCards.ToList())
            {
                _ = _card.ShowFrontSide();
                bool _won = _card.characterInfo != null && _winners.Contains(_card.characterInfo.ownerClientId.Value);
                if (_announce != null) _announce.MarkCard(_card, _won);
                if (await UniTask.Delay(TimeSpan.FromSeconds(UI.EndGame.EndGameAnnounce.CardStagger), cancellationToken: _boardAlive)
                        .SuppressCancellationThrow())
                {
                    return;
                }
            }
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            
            foreach (var _character in CharacterQuery.GetCharacters(false))
            {
                gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId.Value, 
                    nameof(CharacterInfoReveal.isRoleRevealed),
                    RevealLevel.Public, false);
            }
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(GameEndingAnimation),
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }

        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            // Rejoin 02: the game is over, the session token has nothing left to open.
            Network.RejoinSessionStore.Clear();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
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