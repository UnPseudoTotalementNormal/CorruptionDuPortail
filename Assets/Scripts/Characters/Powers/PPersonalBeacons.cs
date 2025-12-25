using System;
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

        private void Awake()
        {
            if (NetworkManager.IsServer)
            { 
                onPowerReparented += OnPowerReparented;
                IEnumerable<Character> _robots = GameManager.instance.characterManager.GetCharacters().Where(_c => _c.role.roleID == RoleID.Robot);
                foreach (Character _character in _robots)
                {
                    CreateBeaconRpc(_character.OwnerClientId, false);
                }
            }
        }

        private void OnPowerReparented()
        {
            CreateBeaconRpc(OwnerClientId, true);
        }

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
            
            CreateBeaconRpc(_characterClickedId, true);
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
        private void CreateBeaconRpc(ulong _targetClientId, bool _isVisibleOnCard)
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
            Character _beaconedCharacter = GameManager.instance.characterManager.GetCharacter(_newBeacon.targetClientId);
            
            if (_beaconedCharacter.role.roleID == RoleID.Robot)
            {
                ChatManager.instance.AddMessageLocal($"Le robot a un nouvel état de corruption: {_newState}",
                    ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
                return;
            }
            
            if (_beaconedCharacter.role.factionType != FactionType.chosen)
            {
                return;
            }
            ChatManager.instance.AddMessageLocal($"{_newBeacon.targetClientId.GetPlayerName()} a un nouvel état de corruption: {_newState}", 
                ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
        }
    }
}