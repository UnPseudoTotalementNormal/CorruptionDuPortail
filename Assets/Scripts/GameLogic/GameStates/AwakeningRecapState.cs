#region

using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Network;
using UI;
using UI.Components;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "AwakeningRecapState", menuName = "GameStates/AwakeningRecapState")]
    public class AwakeningRecapState : GameState
    {
        [SerializeField] private List<AwakeningRecapEvent> recapEvents;
        
        private List<AwakeningRecapEventComponent> spawnedRecapEvents = new();
        private int currentEventIndex = 0;
        
        [HideInInspector] public float currentEventTimer = 0f;
        
        private const int NONE_EVENT_INDEX = -1;
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
            
            Assert.IsNotNull(stateUI, "AwakeningRecapState requires a StateUI prefab to be set.");
            AwakeningRecapStateUI _ui = stateUI as AwakeningRecapStateUI;
            Assert.IsNotNull(_ui, "AwakeningRecapStateUI is not set in the StateUI prefab.");
            foreach (AwakeningRecapEvent _recapEvent in recapEvents)
            {
                spawnedRecapEvents.Add(_ui.SetupAwakeningEvent(_recapEvent));
            }
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(SetEventIndex), 
                new NetworkSerializableObject[] { new(NONE_EVENT_INDEX) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));

            currentEventIndex = 0;
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(PlayEvent),
                new NetworkSerializableObject[] { new(currentEventIndex) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }

        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
            
            currentEventTimer -= Time.deltaTime;
            if (currentEventTimer <= 0f)
            {
                int _nextIndex = currentEventIndex + 1;
                if (_nextIndex < recapEvents.Count)
                {
                    gameManager.DoStateMethodRpc(GetType().FullName, nameof(PlayEvent),
                        new NetworkSerializableObject[] { new(_nextIndex) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
                }
                else
                {
                    gameManager.NextGameState();
                }
            }
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }

        private void SetEventIndex(int _newIndex)
        {
            currentEventIndex = _newIndex;
        }
        
        private void PlayEvent(int _newIndex) //TODO: REPLACE THAT WITH STATE UI, this won't work
        {
            int _oldIndex = currentEventIndex;
            currentEventIndex = _newIndex;

            if (_oldIndex != NONE_EVENT_INDEX)
            {
                HideEvent(_oldIndex);
            }
            ShowEvent(_newIndex);
            
            currentEventTimer = spawnedRecapEvents[_newIndex].EvaluateDuration();
        }

        private void ShowEvent(int _index)
        {
            spawnedRecapEvents[_index]?.ShowEvent();
        }

        private void HideEvent(int _index)
        {
            spawnedRecapEvents[_index]?.HideEvent();
        }
    }
    
    [Serializable]
    public class AwakeningRecapEvent
    {
        public string eventName;
        
        public AwakeningRecapEventComponent eventPrefab;
    }
}

