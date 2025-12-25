using System.Collections.Generic;
using System.Linq;
using Characters.Powers.PowerObjects;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using FocusSystem;
using GameLogic;
using Network;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PPersonalBeacons : Power
    {
        List<PersonalBeaconObject> personalBeacons = new();
        
        public override void StartUse()
        {
            base.StartUse();
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, IsTargetValid);
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            OnCharacterClickedRpc(_characterClicked.ownerClientId.Value);
            
            OnUsed();
        }
        
        [Rpc(SendTo.Server)]
        private void OnCharacterClickedRpc(ulong _characterClickedId)
        {
            if (!IsTargetValid(_characterClickedId))
            {
                return;
            }
            personalBeacons.Any(_p => _p.targetClientId == _characterClickedId);
            
            CreateBeaconRpc(_characterClickedId);
            //TODO: spawn beacon logic here!
        }

        protected override void StopUse()
        {
            base.StopUse();
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }
        
        private bool IsTargetValid(ulong _targetId)
        {
            return TargetUtils.IsTargetValid(_targetId, targetIncludeFlags) && personalBeacons.All(_p => _p.targetClientId != _targetId);
        }

        [Rpc(SendTo.Everyone)]
        private void CreateBeaconRpc(ulong _targetClientId)
        {
            PersonalBeaconObject _newBeacon = new(this, _targetClientId);
            personalBeacons.Add(_newBeacon);
            if (NetworkManager.LocalClientId != ownerClientId.Value) //only the owner cares about beacon state changes
            {
                return;
            }
            _newBeacon.onCorruptedBeaconChanged += (_newState) => OnCorruptedBeaconChanged(_newBeacon, _newState);
        }

        private void OnCorruptedBeaconChanged(PersonalBeaconObject _newBeacon, bool _newState)
        {
            Debug.Log($"Beacon for {_newBeacon.targetClientId} corrupted state changed to {_newState}");
            ChatManager.instance.AddMessageLocal($"{_newBeacon.targetClientId.GetPlayerName()} a un nouvel état de corruption: {_newState}", ChatManager.SERVER_CLIENT_ID);
        }
    }
}