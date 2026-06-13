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
            // Story 12.2: hand the injected character-query slice down to the event component (this host is a
            // StateUI carrying CharacterQuery) so leaves like AwakeningRecapCorruption read it instead of the façade.
            _eventObject.CharacterQuery = CharacterQuery;
            _eventObject.SetupEvent(_recapEvent);
            return _eventObject;
        }
    }
}