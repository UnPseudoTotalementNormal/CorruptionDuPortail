using System;
using AudioSystem;
using Board;
using Characters;
using Cysharp.Threading.Tasks;
using Extensions;
using FMODUnity;
using UnityEngine;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "ChainingState", menuName = "GameStates/ChainingState")]
    public class ChainingState : GameState
    {
        public EventReference chainingAnnouncementSound;
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            Diagnostics.Logs.GameLog.Guarded(() =>
            {
                var _seats = new System.Collections.Generic.List<ulong>();
                foreach (var _id in chainingManager.chainingPlayers) _seats.Add(_id);
                Diagnostics.Logs.GameLog.Info("chain", "chain.server.start", "seats", _seats);
            });
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
                    await DoChainingReveal(_chainingCharacterId, _chainingCharacter);
                    await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: boardManager.GetCancellationTokenOnDestroy());
                }
                catch (OperationCanceledException)
                {
                    Diagnostics.Logs.GameLog.Warn("chain", "chain.reveal.cancelled", "seat", _chainingCharacterId, "board", (bool)boardManager);
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
                Diagnostics.Logs.GameLog.Info("chain", "chain.server.done", "seats", _chainingCharactersId);
                Loop.NextGameState();
            }
        }

        // T19: the chained cat itself, filmed from the front (lifted, dropped, splatch, frozen). The card that used to
        // turn over on the table is gone (Poyo, 2026-10-21): by day the table is away and the cards are on screen, where
        // the chained player's card shows its role once the reveal lands.
        private async UniTask DoChainingReveal(ulong _chainingCharacterId, Character _chainingCharacter)
        {
            var _boardAlive = boardManager.GetCancellationTokenOnDestroy();
            await UniTask.Delay(TimeSpan.FromSeconds(0.25f), cancellationToken: _boardAlive);

            GameAudioManager.instance.PlayOneShot(chainingAnnouncementSound.GetPath());
            if (chainingManager.RevealDirector != null)
            {
                await chainingManager.RevealDirector.PlayAsync(_chainingCharacterId, _boardAlive);
            }
            else
            {
                Diagnostics.Logs.GameLog.Warn("chain", "chain.reveal.skipped", "seat", _chainingCharacterId, "reason", "no-director");
            }
            if (_chainingCharacter.role != null)
            {
                GameAudioManager.instance.PlayOneShot(_chainingCharacter.role.onChainingSound.GetPath());
            }
        }
    }
}
