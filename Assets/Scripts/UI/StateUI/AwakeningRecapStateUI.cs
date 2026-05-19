using System.Collections.Generic;
using GameLogic.GameStates;
using UI.Components;
using UnityEngine;

namespace UI
{
    public class AwakeningRecapStateUI: StateUI
    {
        public Transform recapEventsParent;
        public AwakeningRecapEventComponent SetupAwakeningEvent(AwakeningRecapEvent _recapEvent)
        {
            AwakeningRecapEventComponent _eventObject = Instantiate(_recapEvent.eventPrefab, recapEventsParent);
            _eventObject.SetupEvent(_recapEvent);
            return _eventObject;
        }
    }
}