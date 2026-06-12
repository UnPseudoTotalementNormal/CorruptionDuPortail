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
                // Story 7.4: pre-spawn (Awake) read — the base Power.characterManager is not resolved until
                // OnNetworkSpawn, so resolve from the composition root here. Behaviour-identical (delegates to
                // CharacterManager.For), removes the GameManager hub-hop (deleted in 7.5). Proper fix: Epic 11.
                IEnumerable<Character> _robots = CompositionRoot.For(NetworkManager).CharacterManager.GetCharacters().Where(_c => _c.role.roleID == RoleID.Robot);
                foreach (Character _character in _robots)
                {
                    gameInfoRevealer.SendRevealLevelRpc(_character.ownerClientId.Value, nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed),
                        RevealLevel.Personal, ownerClientId.Value);
                }
            }
        }

        private void OnPowerReparented()
        {
            CreateBeaconRpc(ownerClientId.Value, true);
        }

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
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
            roleTargetSystem.NewTargeting(ownerClientId.Value, _characterClickedId);
            
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
            PersonalBeaconObject _newBeacon = new(this, _targetClientId, characterManager);
            personalBeacons.Add(_newBeacon);
            
            // Only the owner processes beacon state changes. Simulated players on host must subscribe too.
            if (!characterManager.IsLocalOrSimulated(ownerClientId.Value)) 
            {
                return;
            }
            
            _newBeacon.onCorruptedBeaconChanged += (_newState) => OnCorruptedBeaconChanged(_newBeacon, _newState);
        }

        private void OnCorruptedBeaconChanged(PersonalBeaconObject _newBeacon, bool _newState)
        {
            Character _beaconedCharacter = characterManager.GetCharacter(_newBeacon.targetClientId);
            
            // Do not show local visual/chat cues if the Host is not currently possessing the owner
            if (characterManager.GetLocalClientId() != ownerClientId.Value) return;

            if (_beaconedCharacter.role.roleID == RoleID.Robot)
            {
                chatManager.AddMessageLocal($"Le robot a un nouvel état de corruption: {_newState}",
                    ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
                return;
            }
            
            if (_beaconedCharacter.role.factionType != FactionType.chosen)
            {
                return;
            }
            chatManager.AddMessageLocal($"{_newBeacon.targetClientId.GetPlayerName()} a un nouvel état de corruption: {_newState}", 
                ChatManager.SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
        }
    }
}