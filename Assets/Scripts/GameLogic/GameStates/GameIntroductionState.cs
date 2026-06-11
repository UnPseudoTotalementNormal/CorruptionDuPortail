#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.WinningConditions;
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
            
            _ = BoardManager.instance.ShowAllPlayerCards();
            gameManager.charactersBar.ResetCharactersBar(gameManager.characterManager.GetCharacters());
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
                var _eventPath = gameManager.characterManager.GetLocalCharacter(false).role.onGameStartRoleRevealSound.GetPath();
                GameAudioManager.instance.PlayOneShot(_eventPath);
                showRoleText = true;
                onRoleTextShown?.Invoke();
            }
            
            if (!endedState && gameManager.IsServer && stateTimer >= timeBeforeGameStart)
            {
                endedState = true;
                gameManager.NextGameState();
            }
        }
    }
}