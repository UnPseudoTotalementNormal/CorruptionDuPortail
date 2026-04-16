using System;
using System.Collections.Generic;
using Board;
using System.Linq;
using Characters.Powers.PowerObjects;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PPersonalBeacons : Power
    {
        List<PersonalBeaconObject> personalBeacons = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => personalBeacons.All(_p => _p.targetClientId != ctx.targetId));
        }

        private void Awake()
        {
            if (NetworkManager.IsServer)
            { 
                onPowerReparented += OnPowerReparented;
                IEnumerable<Character> _robots = GameManager.instance.characterManager.GetCharacters().Where(_c => _c.role.roleID == RoleID.Robot);
                foreach (Character _character in _robots)
                {
                    GameManager.instance.gameInfoRevealer.SendRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed),
                        RevealLevel.Personal, ownerClientId.Value);
                }
            }
        }

        private void OnPowerReparented()
        {
            CreateBeaconRpc(ownerClientId.Value, true);
        }

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));
        }

        private void OnCardClicked(Card _card)
        {
            Character _characterClicked = _card.characterInfo;
            
            if (!CheckIsTargetValid(_characterClicked.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            
            OnCharacterClickedRpc(_characterClicked.ownerClientId.Value);
            
            OnUsed();
        }
        
        [Rpc(SendTo.Server)]
        private void OnCharacterClickedRpc(ulong _characterClickedId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _characterClickedId);
            
            CreateBeaconRpc(_characterClickedId, true);
        }

        protected override void StopUse()
        {
            base.StopUse();
            BoardManager.instance.onCardClicked -= OnCardClicked;
            FocusManager.instance.UnfocusAll();
        }

        [Rpc(SendTo.Everyone)]
        private void CreateBeaconRpc(ulong _targetClientId, bool _isVisibleOnCard)
        {
            PersonalBeaconObject _newBeacon = new(this, _targetClientId);
            personalBeacons.Add(_newBeacon);
            
            // Only the owner processes beacon state changes. Simulated players on host must subscribe too.
            if (!CharacterManager.instance.IsLocalOrSimulated(ownerClientId.Value)) 
            {
                Debug.Log("Not subscribing to corrupted beacon changes, not the owner or host simulating");
                return;
            }
            
            Debug.Log("Subscribing to corrupted beacon changes");
            _newBeacon.onCorruptedBeaconChanged += (_newState) => OnCorruptedBeaconChanged(_newBeacon, _newState);
        }

        private void OnCorruptedBeaconChanged(PersonalBeaconObject _newBeacon, bool _newState)
        {
            Character _beaconedCharacter = GameManager.instance.characterManager.GetCharacter(_newBeacon.targetClientId);
            
            Debug.Log("Beaconed character corruption state changed: " + _beaconedCharacter.GetOwnerPseudo() + " New state: " + _newState);
            
            // Do not show local visual/chat cues if the Host is not currently possessing the owner
            if (CharacterManager.instance.GetLocalClientId() != ownerClientId.Value) return;

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