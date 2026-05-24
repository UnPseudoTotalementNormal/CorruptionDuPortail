using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers.PowerObjects;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using GameLogic;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

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
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked);
        }

        private void OnCharacterPicked(Character _characterClicked)
        {
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
            SelectionFlowService.instance.CancelSelection();
        }

        [Rpc(SendTo.Everyone)]
        private void CreateBeaconRpc(ulong _targetClientId, bool _isVisibleOnCard)
        {
            PersonalBeaconObject _newBeacon = new(this, _targetClientId);
            personalBeacons.Add(_newBeacon);
            
            // Only the owner processes beacon state changes. Simulated players on host must subscribe too.
            if (!CharacterManager.instance.IsLocalOrSimulated(ownerClientId.Value)) 
            {
                return;
            }
            
            _newBeacon.onCorruptedBeaconChanged += (_newState) => OnCorruptedBeaconChanged(_newBeacon, _newState);
        }

        private void OnCorruptedBeaconChanged(PersonalBeaconObject _newBeacon, bool _newState)
        {
            Character _beaconedCharacter = GameManager.instance.characterManager.GetCharacter(_newBeacon.targetClientId);
            
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