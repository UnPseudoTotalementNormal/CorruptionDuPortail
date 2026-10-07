#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using Extensions;
using FMODUnity;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "GameIntroductionState", menuName = "GameStates/GameIntroductionState")]
    public class GameIntroductionState : GameState
    {
        private bool showPreRoleText;
        private bool showRoleText;
        private bool endedState;
    
        public float timeBeforePreRoleText = 0.5f;
        public float timeBeforeRoleText = 1.5f;
        public float timeBeforeGameStart = 4f;
        public event Action onPreRoleTextShown;
        public event Action onRoleTextShown;

        private float stateTimer = 0;
        
        public EventReference gameOpeningSound;
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            TellAnomaliesTheirFakeRoles();
        }

        // GD wording (Mage Occulte / Abyss passives): the anomalies know as many fake roles (roles of the composition
        // nobody plays) as there are anomalies, a safe bluff. Runs once per game, after role attribution and the
        // knowledge reset; the server ledger re-sends it to a player who rejoins. Fake anomaly roles are left out
        // (not a role an anomaly would claim).
        private void TellAnomaliesTheirFakeRoles()
        {
            if (gameInfoRevealer == null) return;

            List<Character> _characters = CharacterQuery.GetCharacters(false);
            List<ulong> _anomalies = _characters
                .Where(_c => _c && !_c.isFake && _c.role != null && _c.role.factionType == FactionType.anomaly)
                .Select(_c => _c.ownerClientId.Value).ToList();
            List<ulong> _fakes = _characters
                .Where(_c => _c && _c.isFake && _c.role != null && _c.role.factionType != FactionType.anomaly)
                .Select(_c => _c.ownerClientId.Value).ToList();

            foreach ((ulong _viewer, ulong _fake) in AnomalyFakeRoleHint.Pick(_anomalies, _fakes, new UnityRandomProvider()))
            {
                gameInfoRevealer.SendRevealLevelRpc(_fake, nameof(CharacterInfoReveal.isFakeRevealed), RevealLevel.Personal, _viewer, false);
            }
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            GameAudioManager.instance.PlayOneShot(gameOpeningSound.GetPath());
            
            stateTimer = 0;
            showRoleText = false;
            showPreRoleText = false;
            endedState = false;
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
            
            DealBoard();
        }

        // The table this introduction leaves behind: player cards and the role shelf. Also replayed on a peer that
        // rejoins after the introduction (Rejoin 02), which never received this state's end.
        public void DealBoard()
        {
            _ = boardManager.ShowAllPlayerCards();
            charactersBar.ResetCharactersBar(CharacterQuery.GetCharacters());
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
            
            stateTimer += Time.deltaTime;
            if (!showPreRoleText && stateTimer >= timeBeforePreRoleText)
            {
                showPreRoleText = true;
                onPreRoleTextShown?.Invoke();
            }
            
            if (!showRoleText && stateTimer >= timeBeforeRoleText)
            {
                var _eventPath = CharacterQuery.GetLocalCharacter(false).role.onGameStartRoleRevealSound.GetPath();
                GameAudioManager.instance.PlayOneShot(_eventPath);
                showRoleText = true;
                onRoleTextShown?.Invoke();
            }
            
            if (!endedState && gameManager.IsServer && stateTimer >= timeBeforeGameStart)
            {
                endedState = true;
                Loop.NextGameState();
            }
        }
    }
}